"""飞书长连接旁路：收事件 → 往 _Tasks/唤醒/ 丢信号文件。

为什么是 Python 旁路而不是 C#：飞书的「长连接」收事件没有官方 .NET SDK，
自己实现那套私有 WebSocket 协议不划算，而**旁路的输出接口就是一个文件**——
接的是 P8 批次 1 已经做好的文件唤醒源（决策 82），所以这个进程崩了、换了、
以后换成 C# 重写，引擎侧一个字都不用改。

长连接的好处是**不需要公网回调地址**，本机 NAT 后面也能收事件。

密钥从 Config/创作管线/本机.json 读，**只进 SDK 的构造参数，不打印、不写日志**
（决策 5、78）。
"""

import json
import os
import sys
import time
from pathlib import Path

import lark_oapi as lark

REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
LOCAL_CONFIG = REPOSITORY_ROOT / "Config" / "创作管线" / "本机.json"
WAKE_DIRECTORY = REPOSITORY_ROOT / "_Tasks" / "唤醒"


def log(message):
    """日志一律走 stderr——stdout 留给别的用途，且绝不打印密钥。"""
    print(message, file=sys.stderr, flush=True)


def read_credentials():
    """读应用标识与密钥。读不到就退出，不许用空值硬连（那会得到一个查不到根因的握手失败）。"""
    if not LOCAL_CONFIG.exists():
        log(f"本机配置不存在：{LOCAL_CONFIG}")
        sys.exit(2)
    data = json.loads(LOCAL_CONFIG.read_text(encoding="utf-8"))
    app_id = data.get("下游配置", {}).get("feishu", {}).get("应用标识", "")
    app_secret = data.get("飞书应用密钥", "")
    if not app_id or not app_secret:
        log("本机配置里缺 应用标识 或 飞书应用密钥")
        sys.exit(2)
    return app_id, app_secret


def write_wake_signal(event_kind, payload):
    """
    落一个唤醒信号文件。文件名带毫秒时间戳与事件类型，保证同一秒多个事件不互相覆盖。

    信号内容只留「什么事件、什么时候、原始载荷」——**不做任何判定**。
    判定是引擎的事（决策 81：外壳不做判定）。
    """
    WAKE_DIRECTORY.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%dT%H%M%S", time.gmtime()) + f"-{int(time.time() * 1000) % 1000:03d}"
    target = WAKE_DIRECTORY / f"{stamp}-{event_kind}.json"
    body = {
        "来源": "feishu-长连接",
        "事件": event_kind,
        "收到时间": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "载荷": payload,
    }
    target.write_text(json.dumps(body, ensure_ascii=False, indent=2), encoding="utf-8")
    log(f"唤醒信号已落盘：{target.name}")
    return target


def on_bitable_record_changed(data) -> None:
    """多维表格记录变更——需求编辑端有人改了东西，该唤醒引擎拉一次。"""
    write_wake_signal("多维表格记录变更", json.loads(lark.JSON.marshal(data)))


def on_message_received(data) -> None:
    """收到消息——助手形态要用。"""
    write_wake_signal("收到消息", json.loads(lark.JSON.marshal(data)))


def main():
    app_id, app_secret = read_credentials()
    handler = (
        lark.EventDispatcherHandler.builder("", "")
        .register_p2_application_bot_menu_v6(lambda data: write_wake_signal("机器人菜单", json.loads(lark.JSON.marshal(data))))
        .register_p2_im_message_receive_v1(on_message_received)
        .register_p2_drive_file_bitable_record_changed_v1(on_bitable_record_changed)
        .build()
    )
    log("长连接旁路启动，等事件（Ctrl+C 退出）")
    log(f"唤醒目录：{WAKE_DIRECTORY}")
    client = lark.ws.Client(app_id, app_secret, event_handler=handler, log_level=lark.LogLevel.INFO)
    client.start()


if __name__ == "__main__":
    main()

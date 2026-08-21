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
CONVERSATION_DIRECTORY = REPOSITORY_ROOT / "_Tasks" / "会话"


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


def write_signal(directory, event_kind, payload, conversation=None):
    """
    落一个信号文件。文件名带毫秒时间戳与事件类型，保证同一秒多个事件不互相覆盖。

    信号内容只留「什么事件、什么时候、原始载荷」——**不做任何判定**。
    判定是引擎的事（决策 81：外壳不做判定）。

    `conversation` 是**归一块**：把飞书事件里那几个字段翻成引擎认识的中文键。
    归一放在这里而不是引擎里，是因为「事件长什么样」是下游特有知识（决策 93）——
    引擎只读归一块，换一个消息下游，引擎侧一个字都不用改。
    """
    directory.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%dT%H%M%S", time.gmtime()) + f"-{int(time.time() * 1000) % 1000:03d}"
    target = directory / f"{stamp}-{event_kind}.json"
    body = {
        "来源": "feishu-长连接",
        "事件": event_kind,
        "收到时间": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    if conversation is not None:
        body["会话"] = conversation
    body["载荷"] = payload
    target.write_text(json.dumps(body, ensure_ascii=False, indent=2), encoding="utf-8")
    log(f"信号已落盘：{target.name}")
    return target


def write_wake_signal(event_kind, payload):
    """唤醒引擎的信号，落 _Tasks/唤醒/。"""
    return write_signal(WAKE_DIRECTORY, event_kind, payload)


def normalize_message(payload):
    """
    把 im.message.receive_v1 的载荷翻成归一的「会话」块。

    取不到的字段一律给空串——**不许猜**。引擎那边会因为「会话标识为空」直接判这条没法处理，
    那比拿一个编出来的标识去回话强得多。
    """
    event = (payload or {}).get("event", {}) or {}
    message = event.get("message", {}) or {}
    sender_id = ((event.get("sender", {}) or {}).get("sender_id", {}) or {})

    text = ""
    raw_content = message.get("content", "")
    if raw_content:
        try:
            text = (json.loads(raw_content) or {}).get("text", "") or ""
        except (ValueError, TypeError):
            # content 不是合法 JSON 时留空并记一笔：宁可让引擎回「只认文字」，也不许把原文当正文。
            log("消息 content 不是合法 JSON，正文按空处理")

    return {
        "会话标识": message.get("chat_id", "") or "",
        "发件人标识": sender_id.get("open_id", "") or "",
        "消息标识": message.get("message_id", "") or "",
        "消息类型": message.get("message_type", "") or "",
        "会话类型": message.get("chat_type", "") or "",
        "文本": text,
    }


def on_bitable_record_changed(data) -> None:
    """多维表格记录变更——需求编辑端有人改了东西，该唤醒引擎拉一次。"""
    write_wake_signal("多维表格记录变更", json.loads(lark.JSON.marshal(data)))


def on_message_received(data) -> None:
    """
    收到消息 → 落**会话目录**，不落唤醒目录（决策 95）。

    两个目录刻意分开：唤醒信号的消费者是引擎守护，会话消息的消费者是助手常驻会话，
    两个消费者盯同一个目录必然互相抢信号——谁先归档谁赢，另一个永远收不到。
    助手写完需求草稿之后会自己往唤醒目录投一个信号，链路仍然接得上。
    """
    payload = json.loads(lark.JSON.marshal(data))
    write_signal(CONVERSATION_DIRECTORY, "收到消息", payload, normalize_message(payload))


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
    log(f"会话目录：{CONVERSATION_DIRECTORY}")
    client = lark.ws.Client(app_id, app_secret, event_handler=handler, log_level=lark.LogLevel.INFO)
    client.start()


if __name__ == "__main__":
    main()

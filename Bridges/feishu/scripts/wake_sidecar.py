"""飞书长连接旁路：收事件 → 往 _Tasks/wake/ 丢信号文件。

为什么是 Python 旁路而不是 C#：飞书的「长连接」收事件没有官方 .NET SDK，
自己实现那套私有 WebSocket 协议不划算，而**旁路的输出接口就是一个文件**——
接的是 P8 批次 1 已经做好的文件唤醒源（决策 82），所以这个进程崩了、换了、
以后换成 C# 重写，引擎侧一个字都不用改。

长连接的好处是**不需要公网回调地址**，本机 NAT 后面也能收事件。

密钥从 Tools/CreationPipeline/Config/local.json 读，**只进 SDK 的构造参数，不打印、不写日志**
（决策 5、78）。
"""

import json
import os
import sys
import time
from pathlib import Path

import lark_oapi as lark

REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
LOCAL_CONFIG = REPOSITORY_ROOT / "Tools" / "CreationPipeline" / "Config" / "local.json"
WAKE_DIRECTORY = REPOSITORY_ROOT / "_Tasks" / "wake"
CONVERSATION_DIRECTORY = REPOSITORY_ROOT / "_Tasks" / "conversations"

# 旁路自己的状态目录。**刻意不放进 conversations/**：那个目录里的每个直属 *.json
# 都会被助手当成一条待回的消息取走，状态文件搁那儿等于凭空多出一条假消息。
SIDECAR_DIRECTORY = REPOSITORY_ROOT / "_Tasks" / "sidecar"
LOCK_FILE = SIDECAR_DIRECTORY / "wake_sidecar.lock"
SEEN_EVENTS_FILE = SIDECAR_DIRECTORY / "seen-events.json"

# 记住多少个已见事件标识。飞书重投与本机重启都在这个窗口里，
# 留太多只是白占内存——按每天几百条算，2000 够用一周。
SEEN_EVENTS_CAPACITY = 2000

# 已见事件标识：list 管淘汰顺序，set 管 O(1) 查。两个一起改，别只改一个。
SEEN_EVENT_LIST = []
SEEN_EVENT_SET = set()


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


EVENT_SLUGS = {
    "收到消息": "message",
    "多维表格记录变更": "bitable-record-changed",
    "机器人菜单": "bot-menu",
}


def acquire_single_instance_lock():
    """
    抢单实例锁，抢不到就退出。

    为什么必须有：旁路跑两份的时候，同一条消息会落两个会话文件，助手就会**把同一个需求建两遍**
    （REQ-0003 与 REQ-0004 就是这么来的）。靠「记得别启动两次」防不住，得让第二份自己起不来。

    用的是操作系统级的文件锁而不是 PID 文件：进程被 kill -9 时锁由内核自动释放，
    不会留下一个谁都不敢删的僵尸锁文件。
    """
    SIDECAR_DIRECTORY.mkdir(parents=True, exist_ok=True)
    handle = os.open(str(LOCK_FILE), os.O_RDWR | os.O_CREAT)
    try:
        if os.name == "nt":
            import msvcrt

            msvcrt.locking(handle, msvcrt.LK_NBLCK, 1)
        else:
            import fcntl

            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        os.close(handle)
        log(f"已经有一份旁路在跑了（锁：{LOCK_FILE}），这一份退出——两份同时收事件会把消息收两遍。")
        sys.exit(3)

    os.write(handle, str(os.getpid()).encode("ascii"))
    # 句柄**故意不关**：锁的生命周期就是这个进程的生命周期。
    return handle


def load_seen_events():
    """读已见事件标识。文件坏了就当空的重来——去重是优化，不许因为它启动不了。"""
    if not SEEN_EVENTS_FILE.exists():
        return []
    try:
        data = json.loads(SEEN_EVENTS_FILE.read_text(encoding="utf-8"))
    except (ValueError, OSError):
        log("已见事件表读不动，按空表重来")
        return []
    return [str(item) for item in data] if isinstance(data, list) else []


def remember_event(seen_list, seen_set, event_id):
    """记下一个事件标识并落盘，超容量就把最老的挤掉。"""
    seen_list.append(event_id)
    seen_set.add(event_id)
    while len(seen_list) > SEEN_EVENTS_CAPACITY:
        seen_set.discard(seen_list.pop(0))
    try:
        SIDECAR_DIRECTORY.mkdir(parents=True, exist_ok=True)
        # 先写临时文件再替换：写到一半断电也不会留下一个半截的表。
        temporary = SEEN_EVENTS_FILE.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(seen_list, ensure_ascii=False), encoding="utf-8")
        os.replace(str(temporary), str(SEEN_EVENTS_FILE))
    except OSError as error:
        log(f"已见事件表写不下去（{error}），这一条不影响处理")


def read_event_identifier(payload):
    """取事件标识。取不到给空串——空串一律不去重，宁可重一条也不许漏一条。"""
    return ((payload or {}).get("header", {}) or {}).get("event_id", "") or ""


def is_duplicate(payload):
    """这条事件是不是已经收过了。顺带把它记下来。"""
    event_id = read_event_identifier(payload)
    if not event_id:
        log("事件里没有 event_id，这一条不做去重")
        return False
    if event_id in SEEN_EVENT_SET:
        return True
    remember_event(SEEN_EVENT_LIST, SEEN_EVENT_SET, event_id)
    return False


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
    event_slug = EVENT_SLUGS.get(event_kind, "event")
    stamp = time.strftime("%Y%m%dT%H%M%S", time.gmtime()) + f"-{int(time.time() * 1000) % 1000:03d}"
    # 文件名不放事件名的中文：事件名在文件内容里（「事件」字段），
    # 文件名只要能排序与不撞名就够了（决策 1：路径全 ASCII）。
    target = directory / f"{stamp}-{event_slug}.json"
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


def handle_event(event_kind, data, directory=None, normalize=None):
    """
    所有事件的统一入口：先去重，再落信号。

    去重放在**落盘之前**：一旦落了盘，下游就分不清「用户真发了两遍」与「同一条被投了两遍」了。
    """
    payload = json.loads(lark.JSON.marshal(data))
    if is_duplicate(payload):
        log(f"事件 {read_event_identifier(payload)} 收过了，丢弃（{event_kind}）")
        return
    conversation = normalize(payload) if normalize is not None else None
    write_signal(directory or WAKE_DIRECTORY, event_kind, payload, conversation)


def on_bitable_record_changed(data) -> None:
    """多维表格记录变更——需求编辑端有人改了东西，该唤醒引擎拉一次。"""
    handle_event("多维表格记录变更", data)


def on_message_received(data) -> None:
    """
    收到消息 → 落**会话目录**，不落唤醒目录（决策 95）。

    两个目录刻意分开：唤醒信号的消费者是引擎守护，会话消息的消费者是助手常驻会话，
    两个消费者盯同一个目录必然互相抢信号——谁先归档谁赢，另一个永远收不到。
    助手写完需求草稿之后会自己往唤醒目录投一个信号，链路仍然接得上。
    """
    handle_event("收到消息", data, CONVERSATION_DIRECTORY, normalize_message)


def main():
    # 锁要在读密钥之前抢：第二份进程连密钥都不该去读。
    acquire_single_instance_lock()
    SEEN_EVENT_LIST.extend(load_seen_events())
    SEEN_EVENT_SET.update(SEEN_EVENT_LIST)
    log(f"已见事件表载入 {len(SEEN_EVENT_LIST)} 条")

    app_id, app_secret = read_credentials()
    handler = (
        lark.EventDispatcherHandler.builder("", "")
        .register_p2_application_bot_menu_v6(lambda data: handle_event("机器人菜单", data))
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

"""飞书长连接旁路：收事件 → 往 _Tasks/wake/ 丢信号文件。

为什么是 Python 旁路而不是 C#：飞书的「长连接」收事件没有官方 .NET SDK，
自己实现那套私有 WebSocket 协议不划算，而**旁路的输出接口就是一个文件**——
接的是 P8 批次 1 已经做好的文件唤醒源（决策 82），所以这个进程崩了、换了、
以后换成 C# 重写，引擎侧一个字都不用改。

长连接的好处是**不需要公网回调地址**，本机 NAT 后面也能收事件。

单实例是**两把锁**，管的不是一件事：仓库内那把拦「同一个仓库起了两份」，
应用级那把拦「两个仓库连了同一个飞书应用」——后者仓库内的锁根本看不见，
而它的后果一样是同一条消息收两遍、需求建两遍。想让两个项目同时开着，
就给它们各建一个飞书应用：应用不同，应用级的锁自然也不同档，谁也不挡谁。

密钥从 Tools/CreationPipeline/Config/local.json 读，**只进 SDK 的构造参数，不打印、不写日志**
（决策 5、78）。
"""

import hashlib
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

# 应用级锁放**仓库外**：它要跨仓库生效，搁在任何一个仓库里另一个仓库都看不到。
# 落在 HSGFrameRun 下面是跟着影子拷贝的运行目录走，同一台机器上只此一份。
APPLICATION_LOCK_DIRECTORY = (
    Path(os.environ.get("LOCALAPPDATA") or Path.home() / ".cache")
    / "HSGFrameRun"
    / "sidecar-locks"
)

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
    "卡片按钮": "card-action",
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

    这把锁**只管这一个仓库**。两个仓库连同一个飞书应用是另一回事，归
    acquire_application_lock 管——那时两边各抢各的仓库锁，都抢得到。
    """
    SIDECAR_DIRECTORY.mkdir(parents=True, exist_ok=True)
    handle = os.open(str(LOCK_FILE), os.O_RDWR | os.O_CREAT)
    if not _try_lock(handle):
        os.close(handle)
        log(f"这个仓库已经有一份旁路在跑了（锁：{LOCK_FILE}），这一份退出——两份同时收事件会把消息收两遍。")
        sys.exit(3)

    os.write(handle, str(os.getpid()).encode("ascii"))
    # 句柄**故意不关**：锁的生命周期就是这个进程的生命周期。
    return handle


def _try_lock(handle):
    """非阻塞抢一个文件锁，抢到返回 True。跨平台的那两行差别就藏在这里。"""
    try:
        if os.name == "nt":
            import msvcrt

            msvcrt.locking(handle, msvcrt.LK_NBLCK, 1)
        else:
            import fcntl

            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        return False
    return True


def application_lock_key(app_id):
    """
    应用标识对应的锁档位。

    取哈希而不是直接拿 app_id 当文件名：锁文件落在仓库外的公共目录里，
    文件名不该把「这台机器上有哪些飞书应用」写在明面上。前 16 位足够不撞。
    """
    return hashlib.sha256(app_id.encode("utf-8")).hexdigest()[:16]


def describe_application_lock_holder(holder_file):
    """占着这把锁的是谁。读不出来就给一句实话，别编。"""
    try:
        data = json.loads(holder_file.read_text(encoding="utf-8"))
    except (ValueError, OSError):
        return "（占用方没留下记录）"
    return f"仓库 {data.get('仓库', '不详')}（进程 {data.get('进程', '不详')}）"


def acquire_application_lock(app_id):
    """
    按**飞书应用**抢锁，抢不到就退出。

    为什么仓库内那把锁不够：它是仓库里的一个文件，两个仓库各有各的，都抢得到。
    可长连接是按应用连的——两个仓库配了同一个 app_id，飞书会把同一条消息投给两条连接，
    于是消息收两遍、需求建两遍（REQ-0003 与 REQ-0004 就是这么来的），而两边的锁都显示正常。

    档位按 app_id 分：**给每个项目单独建一个飞书应用，两边就能同时开着**，
    这也是想并行开多个项目时该走的路——各是各的机器人，连回话都不会串。
    """
    APPLICATION_LOCK_DIRECTORY.mkdir(parents=True, exist_ok=True)
    key = application_lock_key(app_id)
    lock_file = APPLICATION_LOCK_DIRECTORY / f"{key}.lock"
    holder_file = APPLICATION_LOCK_DIRECTORY / f"{key}.holder.json"

    handle = os.open(str(lock_file), os.O_RDWR | os.O_CREAT)
    if not _try_lock(handle):
        os.close(handle)
        log("这个飞书应用已经被占着了：" + describe_application_lock_holder(holder_file))
        log("  两个仓库连同一个应用，同一条消息会被收两遍、需求建两遍，所以这一份退出。")
        log("  要么去那个仓库停掉（panel-stop.bat）；")
        log("  要么给这个项目单独建一个飞书应用，把 local.json 里的")
        log("  「下游配置 → feishu → 应用标识」与「飞书应用密钥」换成新应用的——两边就能同时开。")
        sys.exit(3)

    # 占用记录单独一个文件，不写进锁文件本身：锁住的那个字节范围别人读不了，
    # 而这条记录**就是给被挡下来的那一份看的**。
    try:
        holder_file.write_text(
            json.dumps(
                {"仓库": str(REPOSITORY_ROOT), "进程": os.getpid(), "抢到时间": time.strftime("%Y-%m-%dT%H:%M:%S")},
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
    except OSError as error:
        # 记录写不下去不影响收消息，只影响下一次被挡时的那句话说得清不清楚。
        log(f"应用锁的占用记录写不下去（{error}），不影响收消息")

    log(f"应用级单实例锁已抢到（档位 {key}）")
    # 句柄故意不关，同仓库内那把锁。
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
    """
    取事件标识。取不到给空串——空串一律不去重，宁可重一条也不许漏一条。

    卡片回传（card.action.trigger）的载荷没有 header.event_id，带的是 event.token，
    所以按顺序退一步取它；两者都没有才认输。
    """
    body = payload or {}
    event_id = ((body.get("header", {}) or {}).get("event_id", "")) or ""
    if event_id:
        return event_id
    return ((body.get("event", {}) or {}).get("token", "")) or ""


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


def extract_post_text(content):
    """
    把富文本（post）的嵌套段落抠成纯文本，顺带把里面的图片 key 收出来。

    post 的 content 是「段落数组的数组」，每个节点带 tag：text / a / at / img / media …
    只有 text 与 a 有可读文字，img 与 media 只有 key。
    """
    lines = []
    attachments = []
    paragraphs = content.get("content") or []
    for paragraph in paragraphs:
        parts = []
        for node in paragraph or []:
            if not isinstance(node, dict):
                continue
            tag = node.get("tag", "")
            if tag in ("text", "a", "at"):
                parts.append(node.get("text", "") or "")
            elif tag == "img" and node.get("image_key"):
                attachments.append({"类型": "image", "key": node["image_key"], "文件名": ""})
            elif tag == "media" and node.get("file_key"):
                attachments.append({
                    "类型": "file",
                    "key": node["file_key"],
                    "文件名": node.get("file_name", "") or "",
                })
        line = "".join(parts).strip()
        if line:
            lines.append(line)

    title = (content.get("title") or "").strip()
    if title:
        lines.insert(0, title)

    return "\n".join(lines), attachments


def extract_content(message_type, content):
    """
    按消息类型抠出「文本 + 附件」。

    **各种消息都要认得**：人发一张图配一句话（post）、直接甩一个文件、发段语音——
    这些都是「他对助手说的一句话」。只认 text 的话，助手会回一句「我只认文字消息」，
    而人明明已经把要说的说清楚了。

    认不出的类型不报错、也不硬猜正文：文本给空、附件给空，
    引擎那边会照实说「这条我处理不了」并带上类型名——那比把原始 JSON 当正文强。
    """
    if message_type == "text":
        return (content.get("text", "") or ""), []

    if message_type == "post":
        return extract_post_text(content)

    if message_type == "image":
        key = content.get("image_key", "") or ""
        return "", ([{"类型": "image", "key": key, "文件名": ""}] if key else [])

    # file / audio / media(视频) / sticker 都是一个 file_key 加可选文件名。
    key = content.get("file_key", "") or ""
    if key:
        return "", [{
            "类型": "file",
            "key": key,
            "文件名": content.get("file_name", "") or "",
        }]

    return "", []


def normalize_message(payload):
    """
    把 im.message.receive_v1 的载荷翻成归一的「会话」块。

    取不到的字段一律给空串——**不许猜**。引擎那边会因为「会话标识为空」直接判这条没法处理，
    那比拿一个编出来的标识去回话强得多。

    附件只归一到「有哪几个 key」为止，**不在这里下载**：
    下载要调飞书的接口，那是桥的事（决策 93）；旁路只把下游的形状翻成引擎认识的形状。
    """
    event = (payload or {}).get("event", {}) or {}
    message = event.get("message", {}) or {}
    sender_id = ((event.get("sender", {}) or {}).get("sender_id", {}) or {})
    message_type = message.get("message_type", "") or ""

    text = ""
    attachments = []
    raw_content = message.get("content", "")
    if raw_content:
        try:
            content = json.loads(raw_content) or {}
            text, attachments = extract_content(message_type, content)
        except (ValueError, TypeError):
            # content 不是合法 JSON 时留空并记一笔：宁可让引擎说「这条处理不了」，
            # 也不许把原文当正文。
            log("消息 content 不是合法 JSON，正文与附件都按空处理")

    return {
        "会话标识": message.get("chat_id", "") or "",
        "发件人标识": sender_id.get("open_id", "") or "",
        "消息标识": message.get("message_id", "") or "",
        "消息类型": message_type,
        "会话类型": message.get("chat_type", "") or "",
        "文本": text,
        "附件": attachments,
    }


def normalize_card_action(payload):
    """
    把 card.action.trigger 的载荷翻成归一的「会话」块。

    与消息不同的是它没有正文，带回来的是**点了哪个按钮、按钮上挂了什么值**。
    动作名从 value.动作 取——那是发卡时引擎写进去的键（见 MessageReplier.BuildCardJson）。

    会话标识取 context.open_chat_id：回话要发回**卡片所在的那个会话**，
    取不到就给空串，引擎会因为「会话标识为空」判这条没法处理——那比编一个标识强。
    """
    event = (payload or {}).get("event", {}) or {}
    action = event.get("action", {}) or {}
    context = event.get("context", {}) or {}
    operator = event.get("operator", {}) or {}
    value = action.get("value", {}) or {}
    if not isinstance(value, dict):
        # 按钮的 value 也可能被配成字符串。那时动作名无从谈起，如实留空。
        log("卡片按钮的 value 不是对象，动作按空处理")
        value = {}

    return {
        "会话标识": context.get("open_chat_id", "") or "",
        "发件人标识": operator.get("open_id", "") or "",
        "消息标识": context.get("open_message_id", "") or "",
        "消息类型": "card_action",
        "文本": "",
        "按钮动作": value.get("动作", "") or "",
        "按钮携带": value,
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


def on_card_action(data):
    """
    卡片按钮被点了 → 落**会话目录**，与消息同一条队列。

    同一条队列是刻意的：按钮点击也是「这个人对助手说的一句话」，
    分两个队列就要再写一套取信号、归档、隔离与重试，而那套已经有了。

    这里同步回一个 toast：飞书的按钮点下去要有即时反馈，
    而真正的处理是助手常驻会话下一轮的事（可能几秒后）。没有 toast，人会以为没点着、连点几下。
    """
    handle_event("卡片按钮", data, CONVERSATION_DIRECTORY, normalize_card_action)
    return {"toast": {"type": "info", "content": "收到，正在处理…"}}


def main():
    # 锁要在读密钥之前抢：第二份进程连密钥都不该去读。
    acquire_single_instance_lock()
    SEEN_EVENT_LIST.extend(load_seen_events())
    SEEN_EVENT_SET.update(SEEN_EVENT_LIST)
    log(f"已见事件表载入 {len(SEEN_EVENT_LIST)} 条")

    app_id, app_secret = read_credentials()

    # 应用级锁只能在读完配置之后抢——档位是从 app_id 算出来的。
    # 仓库内那把已经在前面挡掉了同仓库的第二份，这里挡的是「别的仓库连了同一个应用」。
    acquire_application_lock(app_id)

    handler = (
        lark.EventDispatcherHandler.builder("", "")
        .register_p2_application_bot_menu_v6(lambda data: handle_event("机器人菜单", data))
        .register_p2_im_message_receive_v1(on_message_received)
        .register_p2_card_action_trigger(on_card_action)
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

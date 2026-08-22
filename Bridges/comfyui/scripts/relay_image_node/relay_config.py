"""找回仓库、读本机配置、读探测清单。

这个模块是节点与仓库之间唯一的接缝，三条纪律写在这里，别绕过去：

1. **密钥只从 local.json 现读，绝不复制到宿主目录、绝不做成节点参数。**
   workflow.json 是会被保存、导出、发给别人的；密钥一旦成了 widget 的值就跟着它到处跑。
   装进宿主的那份 link.json 里只有仓库根路径一项，没有地址、更没有密钥。

2. **模型清单不自己探，只读 `bridge.probe` 探出来的那份产出。**
   那条命令是这份清单唯一的生产者。节点再探一次就是第二本账，两本账迟早各说各话；
   而且 ComfyUI 每打开一次界面就要问一遍节点定义，在那条路上发 HTTP 会把整个界面拖住。

3. **读不到就说读不到，不猜。** 没有清单就只剩「自动」一档并说清去跑哪条命令，
   不拿任何写死的模型名冒充「至少有一个能用」。
"""

import json
import os

# 「模型」这一格的哨兵值：填它表示不钉死，每次调用现挑。
# 与 C# 侧 Template.Toolkit.CreationPipeline.ModelSelection.AutoSentinel 必须一字不差。
AUTO_SENTINEL = "自动"

# 线上生图那个 driver 的名字。清单、成功记账、地址、密钥全按它取。
IMAGE_DRIVER_NAME = "oaiimage"

# 密钥在 local.json 顶层的键名。
IMAGE_SECRET_FIELD = "生图密钥"

# 装进宿主时写下的「回仓库的路」。
LINK_FILE_NAME = "link.json"


class RelayConfigError(Exception):
    """配置读不出来。消息是给人看的，**永远不许带上密钥的任何内容**（长度、前缀也不许）。"""


def _package_directory():
    """本包所在目录。软链装的时候它指回仓库里的源目录，拷贝装的时候它就在宿主目录下。"""
    return os.path.dirname(os.path.abspath(__file__))


def _read_json(path):
    """读一份 JSON；文件不在给 None，坏了抛 RelayConfigError（这两支不许混）。"""
    if not os.path.isfile(path):
        return None
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except json.JSONDecodeError as error:
        raise RelayConfigError("{} 不是合法 JSON：{}".format(path, error)) from error
    except OSError as error:
        raise RelayConfigError("{} 读不出来：{}".format(path, error)) from error


def repository_root():
    """
    找回仓库根，两条路按顺序试：

    一、包目录里的 link.json——安装器写的，拷贝装的时候这是唯一的线索。
    二、从包目录往上走，找哪一级底下有 Tools/CreationPipeline/Config/——
        软链装、或者直接把仓库里的包挂进 custom_nodes 时走这条。

    两条都不成就抛，并说清该怎么补。**不猜任何路径。**
    """
    package_directory = _package_directory()

    link = _read_json(os.path.join(package_directory, LINK_FILE_NAME))
    if isinstance(link, dict):
        root = str(link.get("仓库根", "")).strip()
        if root and os.path.isdir(root):
            return os.path.abspath(root)
        if root:
            raise RelayConfigError(
                "{} 里记的仓库根不存在了：{}。重装一次这个包就会重写它。".format(LINK_FILE_NAME, root)
            )

    current = package_directory
    while True:
        if os.path.isdir(os.path.join(current, "Tools", "CreationPipeline", "Config")):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent

    raise RelayConfigError(
        "找不到仓库：包目录里没有可用的 {}，往上也没找到 Tools/CreationPipeline/Config/。"
        "重装一次这个包（bridge.script.install --Driver comfyui --Name relay_image_node）就会补上。".format(
            LINK_FILE_NAME
        )
    )


def _local_settings(root):
    """读本机配置。文件不在时给一句明确的话，别让调用方拿着 None 去猜。"""
    settings = _read_json(os.path.join(root, "Tools", "CreationPipeline", "Config", "local.json"))
    if settings is None:
        raise RelayConfigError(
            "本机配置不存在：Tools/CreationPipeline/Config/local.json。"
            "照同目录的 local.example.json 建一份，把地址与密钥填上。"
        )
    if not isinstance(settings, dict):
        raise RelayConfigError("本机配置的顶层不是一个对象。")
    return settings


def endpoint_and_secret(root):
    """
    取线上生图的地址与密钥。

    返回 (地址, 密钥)。**密钥只往 Authorization 头里去**，调用方不许把它写进
    任何日志、异常、返回值或界面文案。缺哪一样就点名说缺哪一样——
    但只说键名，一个字节的值都不带出来（决策 5、78）。
    """
    settings = _local_settings(root)

    downstream = settings.get("下游配置")
    section = downstream.get(IMAGE_DRIVER_NAME) if isinstance(downstream, dict) else None
    endpoint = str(section.get("地址", "")).strip() if isinstance(section, dict) else ""
    secret = str(settings.get(IMAGE_SECRET_FIELD, "")).strip()

    missing = []
    if not endpoint:
        missing.append("「下游配置.{}.地址」".format(IMAGE_DRIVER_NAME))
    if not secret:
        missing.append("「{}」".format(IMAGE_SECRET_FIELD))

    if missing:
        raise RelayConfigError(
            "本机配置里还缺 {}。在面板的 {} 卡里填，或跑 bridge.config.set / bridge.secret.set。".format(
                "、".join(missing), IMAGE_DRIVER_NAME
            )
        )

    return endpoint.rstrip("/"), secret


def configured_timeout(root, default_seconds=180):
    """取配的超时秒；没配或不是数就用默认值。"""
    try:
        settings = _local_settings(root)
    except RelayConfigError:
        return default_seconds

    downstream = settings.get("下游配置")
    section = downstream.get(IMAGE_DRIVER_NAME) if isinstance(downstream, dict) else None
    if not isinstance(section, dict):
        return default_seconds

    value = section.get("超时秒", default_seconds)
    try:
        return max(1, int(value))
    except (TypeError, ValueError):
        return default_seconds


def _probe_directory(root):
    """探测产出的目录：_Generated/Probes/<driver>/。跟着机器走，不进 git。"""
    return os.path.join(root, "_Generated", "Probes", IMAGE_DRIVER_NAME)


def probed_model_names(root):
    """
    上次探测回来的模型清单，序数序。探测产出不在、坏了、或里面没有模型时一律给空表——
    **空表是一个诚实的答案**，调用方据此只给「自动」一档并指路去重探，不许拿写死的名字填空。
    """
    try:
        payload = _read_json(os.path.join(_probe_directory(root), "probe-result.json"))
    except RelayConfigError:
        return []

    if not isinstance(payload, dict):
        return []

    names = payload.get("模型")
    if not isinstance(names, list):
        return []

    return sorted({str(name) for name in names if isinstance(name, str) and name.strip()})


def last_good_model(root):
    """上次在这台机器上真跑成功用的那个模型；没记过给空串。"""
    try:
        payload = _read_json(os.path.join(_probe_directory(root), "last-good-model.json"))
    except RelayConfigError:
        return ""

    if not isinstance(payload, dict):
        return ""

    return str(payload.get("模型", "")).strip()


def remember_last_good_model(root, model_name):
    """
    记一笔「这个模型在这台机器上真跑成功过」，写的是 C# 侧「自动」那一档读的同一个文件——
    **同一本账**，所以在 ComfyUI 里点出来的「自动」与管线跑出来的「自动」永远是同一个答案。

    写盘失败一声不吭：这只是让「自动」挑得更准的一条线索，不是业务数据，
    为它把一次已经成功的出图变成失败不合算（照抄 C# 侧 ModelSelection 的同一条规矩）。
    """
    name = (model_name or "").strip()
    if not name:
        return

    try:
        directory = _probe_directory(root)
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, "last-good-model.json"), "w", encoding="utf-8") as handle:
            json.dump({"契约版本": "1.0.0", "模型": name}, handle, ensure_ascii=False, indent=2)
    except OSError:
        pass


def resolve_model(root, chosen):
    """
    解析这次到底发哪个模型，并给一句「凭什么是它」的账。

    返回 (模型名, 账)。**模型名为空串表示一个 model 参数都不发**，由中转按它自己的默认来——
    这与「回落到某个写死的模型」是两回事：各家中转开通的模型不一样，替它猜只会撞上「参数非法」。

    规矩与 C# 侧 ModelSelection.Resolve 对齐：点名的盖过一切；「自动」先认上次真跑成功的那个，
    它不在清单里了才退到清单序数序第一项。
    """
    picked = (chosen or "").strip()

    if picked and picked != AUTO_SENTINEL:
        return picked, "本次调用点名了模型「{}」".format(picked)

    names = probed_model_names(root)
    if not names:
        return "", (
            "选的是「{}」，但 {} 的探测清单是空的：这次一个 model 参数都不发，由中转按它自己的默认来。"
            "要拿到清单，先跑 bridge.probe --Driver {}（或在面板那张卡上点「重探」）。".format(
                AUTO_SENTINEL, IMAGE_DRIVER_NAME, IMAGE_DRIVER_NAME
            )
        )

    remembered = last_good_model(root)
    if remembered and remembered in names:
        return remembered, (
            "选的是「{}」：挑了「{}」——这台机器上次用它真跑成功过，而且它还在清单里（共 {} 项）。".format(
                AUTO_SENTINEL, remembered, len(names)
            )
        )

    stale = "（上次成功用的「{}」已经不在清单里了）".format(remembered) if remembered else ""
    return names[0], (
        "选的是「{}」：从 {} 项里挑了「{}」（清单序数序第一项）{}。"
        "**清单里混着别的域的模型时这一挑可能不对**——第一次用先在下拉里点名一个，成功过一次之后「自动」就跟着它走。".format(
            AUTO_SENTINEL, len(names), names[0], stale
        )
    )


def model_widget_options():
    """
    「模型」那一格下拉的选项。第一项永远是「自动」。

    这个函数会被 ComfyUI 在**每次请求节点定义时**调到（打开界面、按 R 刷新都会），
    所以它**只读本地文件、绝不发网络请求**——在这条路上发 HTTP，中转慢一秒界面就卡一秒。
    读不出来时不抛：抛了会让整个节点从列表里消失，那比少几个选项糟得多。
    """
    try:
        return [AUTO_SENTINEL] + probed_model_names(repository_root())
    except RelayConfigError:
        return [AUTO_SENTINEL]
    except OSError:
        return [AUTO_SENTINEL]

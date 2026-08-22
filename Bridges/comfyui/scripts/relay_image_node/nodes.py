"""「中转生图」节点：在 ComfyUI 的图里直接调本仓库配好的线上生图中转。

一个节点两用：`image` 口不接线走文生图（/images/generations），接了走图生图（/images/edits）。
输出是标准的 IMAGE，直接接「保存图像」「预览图像」都行。

**这个节点不碰任何本地权重**——没有 UNet、没有 CLIP、没有 VAE，所以本机缺底模也能出图。
"""

import io

import numpy
import torch
from PIL import Image

from . import relay_client, relay_config


class RelayImageNode:
    """
    中转生图。

    「模型」那一格的选项来自 `bridge.probe --Driver oaiimage` 探回来的清单，外加一档「自动」。
    清单是本地文件读出来的，**节点定义这条路上不发任何网络请求**——那条路每打开一次界面就走一遍，
    在上面等中转就是让整个 ComfyUI 界面跟着卡。换了中转地址之后要刷新清单：
    跑一次 bridge.probe（或在面板那张卡上点「重探」），回来按 R 刷新节点定义。
    """

    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "模型": (
                    relay_config.model_widget_options(),
                    {
                        "tooltip": "选「自动」= 不钉死，每次现挑：先认这台机器上次真跑成功的那个，"
                        "它不在清单里了才退到清单第一项；清单空的时候一个 model 参数都不发，由中转按默认来。"
                        "清单来自 bridge.probe --Driver oaiimage，不是这个节点自己探的。"
                    },
                ),
                "提示词": (
                    "STRING",
                    {
                        "multiline": True,
                        "default": "",
                        "tooltip": "要画什么。图生图时它描述的是「把这张图改成什么样」。",
                    },
                ),
                "张数": (
                    "INT",
                    {"default": 1, "min": 1, "max": 8, "tooltip": "一次要几张（对应接口的 n）。"},
                ),
                "尺寸": (
                    "STRING",
                    {
                        "default": "",
                        "tooltip": "形如 1024x1024。**留空就一个 size 参数都不发**，由中转按它自己的默认来——"
                        "各家模型认的档位不一样，这里刻意不给写死的候选，免得撞上「参数非法」。",
                    },
                ),
            },
            "optional": {
                "image": (
                    "IMAGE",
                    {"tooltip": "接了这一口就走图生图（/images/edits），不接走文生图（/images/generations）。"},
                ),
            },
        }

    RETURN_TYPES = ("IMAGE", "STRING")
    RETURN_NAMES = ("图", "账")
    FUNCTION = "生成"
    CATEGORY = "中转"
    DESCRIPTION = "调本仓库配好的线上生图中转出图；不接 image 口走文生图，接了走图生图。不用本地权重。"

    def 生成(self, 模型, 提示词, 张数, 尺寸, image=None):
        root = relay_config.repository_root()
        endpoint, secret = relay_config.endpoint_and_secret(root)
        timeout_seconds = relay_config.configured_timeout(root)

        model_name, note = relay_config.resolve_model(root, 模型)
        size = (尺寸 or "").strip()

        if image is None:
            results = relay_client.generate(
                endpoint, secret, model_name, 提示词, 张数, size, timeout_seconds
            )
            route = "文生图 /images/generations"
        else:
            results = relay_client.edit(
                endpoint,
                secret,
                model_name,
                提示词,
                张数,
                size,
                _tensor_to_png(image),
                "input.png",
                timeout_seconds,
            )
            route = "图生图 /images/edits"

        # 只有**真正用上了模型的调用**成功之后才记账：没发 model 参数的那次，
        # 记下来等于把中转的默认冒充成「我们挑的那个」。
        if model_name:
            relay_config.remember_last_good_model(root, model_name)

        tensor = _images_to_tensor(results)
        账 = "{}；模型：{}；{}；回了 {} 张（取自 {}）".format(
            route,
            model_name if model_name else "（没发 model 参数）",
            note,
            len(results),
            "、".join(sorted({source for _, source in results})),
        )
        return (tensor, 账)


def _tensor_to_png(image):
    """
    把 ComfyUI 的 IMAGE 张量的**第一张**编成 PNG 字节。

    IMAGE 是 [批, 高, 宽, 通道] 的 float32，值域 0~1。接口只收一张参考图，
    所以批里有多张时取第一张——这比把整批拼成一张、或者悄悄多调几次都更好预测。
    """
    frame = image[0] if image.ndim == 4 else image
    array = numpy.clip(frame.cpu().numpy() * 255.0, 0, 255).astype(numpy.uint8)

    mode = "RGBA" if array.shape[-1] == 4 else "RGB"
    if array.shape[-1] == 1:
        array = numpy.repeat(array, 3, axis=-1)
        mode = "RGB"

    buffer = io.BytesIO()
    Image.fromarray(array, mode).save(buffer, format="PNG")
    return buffer.getvalue()


def _images_to_tensor(results):
    """
    把中转回来的图片字节转成 ComfyUI 的 IMAGE 张量 [批, 高, 宽, 3]，float32、0~1。

    一批里尺寸不一致时**当场报错，不悄悄丢**：张量堆不起来是事实，
    偷偷只留第一张会让人以为「要 4 张只回了 1 张」是中转的毛病。
    """
    frames = []
    for index, (payload, _) in enumerate(results):
        try:
            picture = Image.open(io.BytesIO(payload)).convert("RGB")
        except Exception as error:  # noqa: BLE001
            raise relay_client.RelayCallError(
                "中转回来的第 {} 张图解不开（不是一张能读的图片）：{}".format(index + 1, error)
            ) from None
        frames.append(numpy.asarray(picture).astype(numpy.float32) / 255.0)

    shapes = {frame.shape for frame in frames}
    if len(shapes) > 1:
        raise relay_client.RelayCallError(
            "中转这次回来的 {} 张图尺寸不一致（{}），堆不成一个批。"
            "把「张数」改成 1，或把「尺寸」填一个固定值再试。".format(
                len(frames), "、".join("{}x{}".format(shape[1], shape[0]) for shape in sorted(shapes))
            )
        )

    return torch.from_numpy(numpy.stack(frames, axis=0))

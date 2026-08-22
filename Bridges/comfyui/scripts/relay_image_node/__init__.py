"""ComfyUI 自定义节点包：中转生图。

这个文件同时是**装没装的判据**——plugin.json 里「标志文件」指的就是它。

ComfyUI 只在启动时扫一遍 custom_nodes，所以装完（或改完源码之后重装）都要重启 ComfyUI。
导入失败时不许把异常抛出去：抛出去 ComfyUI 会在控制台刷一大段栈、而节点直接从列表里消失，
人只会看见「节点不见了」，看不见为什么。这里把原因收成一个占位说明，让它在日志里说清楚。
"""

NODE_CLASS_MAPPINGS = {}
NODE_DISPLAY_NAME_MAPPINGS = {}

try:
    from .nodes import RelayImageNode

    NODE_CLASS_MAPPINGS["RelayImage"] = RelayImageNode
    NODE_DISPLAY_NAME_MAPPINGS["RelayImage"] = "中转生图"
except Exception as error:  # noqa: BLE001
    print(
        "[relay_image_node] 节点没能加载：{}。"
        "它要 torch / numpy / Pillow（ComfyUI 自带），"
        "还要能找回仓库（包目录里的 link.json）。重装一次这个包会重写 link.json："
        "bridge.script.install --Driver comfyui --Name relay_image_node".format(error)
    )

__all__ = ["NODE_CLASS_MAPPINGS", "NODE_DISPLAY_NAME_MAPPINGS"]

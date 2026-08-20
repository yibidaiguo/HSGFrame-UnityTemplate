"""加工站能力探测脚本：由 BridgeBlender 以 blender --background --factory-startup --python probe.py 方式驱动。

只做一件事：把加工站的节点能力打印成约定前缀行 BRIDGE_RESULT <json>，
其余一切输出走 sys.stderr——stdout 上多一个字节都可能让调用方的协议解析散架。

探测结果是 CapabilityProbeResult.LoadFromFile 要的形状：
{ "节点": [{ "名", "版本", "hash" }], "模型": [], "lora": [] }
版本一律空串（决策 31：版本不比对，编个假版本号比空着糟）。
"""
import json
import sys


def main():
    result = {
        "节点": [{"名": "blender", "版本": "", "hash": ""}],
        "模型": [],
        "lora": []
    }
    # 唯一允许出现在 stdout 上的内容。其余一律走 stderr。
    print("BRIDGE_RESULT " + json.dumps(result, ensure_ascii=False))
    sys.exit(0)


if __name__ == "__main__":
    main()

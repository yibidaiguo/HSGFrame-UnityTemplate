# 配置包导入说明

按下面四步把本配置包导入下游平台：

1. 在下游平台新建助手。
2. 把「system-prompt.md」全文贴进系统提示框。
3. 把「知识」目录下这 5 个文件逐个上传为知识库文件：conflicts.md、design-digest.md、examples.md、glossary.md、modules.md。
4. 回到本仓库跑一次门禁对账，确认指纹一致。

> 警告：fingerprint.json 变了就必须重新走一遍本流程，否则助手用的是过期知识。

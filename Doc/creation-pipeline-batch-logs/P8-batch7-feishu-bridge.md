# P8 批次 7 · 飞书桥：真发卡片通了，真建表卡在文档权限

> 上游：[创作管线P8计划](../creation-pipeline-p8-plan.md)。
> 销的是第六节那张表里「bridge-feishu 实现」这一行，**销掉一半**。

## 一、结论先写

| 动作 | 状态 |
|---|---|
| `bridge.card --DryRun false` 真发一张选片卡 | ✅ **真发成功**，`message_id=om_x100b674333a040a0c1c27e7cf00a4d5` |
| `bridge.apply --DryRun true` 干跑列建表计划 | ✅ 1 张表「需求」15 个字段，类型码全列出来 |
| `bridge.apply --DryRun false` **真建表** | ✅ **真建成**（补权限后），`需求` 表 `table_id=tblDRswnGISAAsbW` |
| 幂等重跑（第二次全跳过） | ✅ 「建了 0 张、跳过 1 张」，**没多出一张** |
| 字段类型码真验 | ✅ **去飞书读回来核对过**（下面第一节之二） |
| 反向验证（表格标识指到不存在的） | ✅ 报 `下游报错` 带飞书 msg 与 log_id，没报「建好了」 |
| 门禁全量 | ✅ `gate.ps1` **PASS 全绿** |

**交批时真建表被 `91403` 挡着，用户补了文档编辑权之后 Claude 自己补跑的，代码一个字没改。**

### 一之二、字段类型码这次是真验的，不是照文档写的

建完之后**去飞书把表读回来**（`GET .../tables/{id}/fields`），不信本地的报告：

```
共 15 个字段：
  id          type=1  Text            类型        type=3  SingleSelect
  状态         type=3  SingleSelect    锁定        type=7  Checkbox
  （其余 11 个 type=1 Text）
```

单选的选项也逐字对上了：
`类型` = 系统、修改、缺陷；`状态` = 草稿、已确认、进行中、待验收、已完成、已作废，
与 `_Generated/Bridges/feishu/建表描述.json` 里定义的完全一致。

**所以 `文本=1 / 单选=3 / 复选框=7` 这三个码现在是实证过的。**
**`数字=2` 与 `多选=4` 仍然只是照文档写的**——本次的 schema 里没有这两种字段，
碰不到。**别把这两个数当已验证的。**

## 二、91403 是什么，怎么确诊的

Claude 自己拿真凭据打了一组对照：

```
GET  /open-apis/bitable/v1/apps/{app_token}         → code=0  success
POST /open-apis/bitable/v1/apps/{app_token}/tables  → code=91403  Forbidden
```

**关键是要把两种 403 分开**：

- **缺 scope** → `99991672`，飞书会**逐条列出缺哪个权限**，还给一条直达申请链接。
  P8 早些时候遇到过一次，照链接点完就好了。
- **缺文档权限** → `91403 Forbidden`，**一条 permission_violations 都不列**。

现在是后者：应用有 `bitable:app` 这个租户级 scope（所以读得了），
但它在**那份多维表格文档上**只有阅读权。

**修法**：打开那张多维表格 → 右上角 `···` → 添加文档应用（或分享 → 添加协作者，
搜应用名）→ 权限给**可编辑**。

## 三、落了什么

| 文件 | 内容 |
|---|---|
| `Bridges/feishu/src/BridgeFeishu/FeishuClient.cs` | 鉴权 + token 缓存（到期前 5 分钟换）+ 错误映射 |
| `Bridges/feishu/src/BridgeFeishu/TableProvisioner.cs` | 建表，**先列表再建、同名跳过** |
| `Bridges/feishu/src/BridgeFeishu/CardSender.cs` | 发卡片 |
| `Tools/Cli/CommandHost/Commands/BridgeCommands.cs` | `bridge.apply` / `bridge.card` |
| `Config/创作管线/下游.json` | 追加三个 port 的路由（**已有三条一字未动**，核对过 diff） |

**两条命令的 `DryRun` 默认值都是 `true`**（核对过）。
真写别人的工作区，默认就该是不写，要写得显式说。

## 四、验收时清掉的一处泄漏

执行端做反向验证时，把用户的 `Config/创作管线/本机.json` **整份复制**到了
`_Scratch/反向验证-fs/` 下——那份里有真的应用密钥。它自己在返回里如实报了这件事。

Claude 的处置：

1. 立刻删掉整个 `_Scratch/反向验证-fs/` 与调试用的 `_Scratch/探针/`
   （后者的 stdout 曾打印过一次 tenant_access_token）。
2. **全仓搜一遍**有没有第二份副本 —— 只剩 `本机.json` 本身。
3. **`git log --all -S <密钥>` 确认它从未进过 git 历史。**

`_Scratch/` 本来就在 `.gitignore` 里，所以没有入库风险；但密钥多一份落盘就多一分暴露面。
**下次派活的任务书要写死：反向验证造临时配置时，密钥字段填假值，不许整文件复制。**

## 四点五、门禁全绿，边界却破了一处（决策 93）

执行端多写了一个任务书里没有的文件：`Tools/CreationPipeline/DownstreamFieldTypeCodec.cs`，
里面装的是**飞书多维表格建表接口的数字类型码**（文本=1、数字=2、单选=3、多选=4、复选框=7）。

它一个「飞书」字都没提，类名叫「Downstream」，注释一律称「目标平台」，
所以**下游边界门禁全绿**——那道门禁只逐行 grep driver 名。
但那几个数出了飞书毫无意义，换一个需求编辑端就要改引擎，
正是决策 11「供给引擎完全泛化」要拦的东西。

已挪到 `Bridges/feishu/src/BridgeFeishu/FeishuFieldTypeCodec.cs`，
测试因此要引桥工程（csproj 里加了一条 ProjectReference 并注明原因）。

**教训写进决策 93**：这道边界最终只能靠人看。
审 `Tools/CreationPipeline/` 的新文件时问一句
**「这段知识换一个下游还成立吗？」**——不成立就该住进桥里。
门禁能自动拦住的只是最蠢的那种越界。

## 五、已知缺口

- **`数字=2` 与 `多选=4` 两个类型码仍未实证**——本次 schema 里没有这两种字段。
- **`表单` 那三项没做**（建表描述里有 3 个表单分组）。飞书对表单视图的开放接口另说。
- **`pull` / `push` 没做**（需求编辑端的读写记录），归下一批。
- **助手 port 的 `package` 没做**——`bridge.provision` 至今只离线产配置包，
  真导入还是「未验证」（P1 批次 6 的结论到现在没变）。

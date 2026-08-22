# 创作管线 · 要你亲手填的东西

> 环境我自己装，**只有密钥和账号标识必须你来填**——我全程不碰密钥的值。

## 一、怎么填

**推荐：在面板上填，不用碰 JSON。** 起面板（`pwsh Tools/start.ps1`）→ 打开
`http://localhost:8766/panel#/packages`「桥接包」页 → 每个下游的卡片上就是它的配置字段，
地址、可执行文件、超时秒这些**预填着当前值，改完点「保存」**；密钥给的是一个
**永远空着的密码框**，粘贴进去点保存即可——面板只把值写进文件，页面上永远只显示
「已配 / 未配」，不回显、不报长度（决策 78 的读侧红线仍然立着）。清掉某一项点「清空」。

底下这套手填 JSON 的办法照旧管用，写在这里是给「面板起不来」时兜底的。

`Tools/CreationPipeline/Config/local.json` **已经建好了**，里面预填了两条机器路径
（ComfyUI 地址、Blender 可执行文件）——那两条是 Claude 装的，值它知道。
你只要**往里面加**，不用重建。用记事本打开就行：

```
notepad "Tools\CreationPipeline\Config\local.json"
```

（在仓库根目录下打开；这个文件已进 `.gitignore`，不会被提交。）

三条注意，每条都踩过：

1. **哪样拿不到就把那几行整条删掉，别留空串 `""`。**
   面板判密钥配没配只看**键在不在**（决策 78），留空串它会显示「已配」——
   那是假绿，比没填还糟。
2. **最后一项后面不能有逗号**，JSON 会当场报错。
3. 存 UTF-8（记事本默认就是）。

存完跟 Claude 说一声，它跑一次只查「键在不在」的检查，
把哪几批能开工报回来——**它不会读你的值**。

填完之后，面板的「桥接包」页（`http://localhost:8766/panel#/packages`）自己就能看：
每个编辑器与每个下游的**本体装没装、要往它里面塞的包装没装、还差什么、下一步干嘛**。
命令行同一份账：`bridge.inventory`。那一页只报告与指路，不代你安装，也不读密钥的值。

**厂商的编辑器插件**（Tripo3d_Unity_Bridge 那种双击解包进 `Assets/` 的 .unitypackage）
包管理器看不见，得先在 `Tools/CreationPipeline/Config/editor-plugins.json` 里声明一条：
写清**装完之后哪个路径会出现**（「标志路径」），页面才判得了它装没装。
路径没填就是「未验」——那是「还没查」，不是「没装」。UPM 包不用往这里写，manifest.json 已经管着。

## 二、要填什么，去哪拿

| 键 | 去哪拿（具体到点哪里） | 缺了会怎样 |
|---|---|---|
| `飞书应用密钥` | [open.feishu.cn](https://open.feishu.cn) → 开发者后台 → 我的应用 → 你的应用 → **凭证与基础信息** → App Secret（点「显示」） | bridge-feishu 试跑跑不了 |
| `下游配置.feishu.应用标识` | 同一页的 **App ID**，`cli_` 开头。不是密钥，一起填省事 | 同上 |
| `下游配置.feishu.多维表格标识` | **浏览器地址栏**里抠：`https://…/base/bascnXXXX?table=…`，`/base/` 后面、`?` 前面那一整段 | 铺表跑不了 |
| `模型生成密钥` | [platform.tripo3d.ai](https://platform.tripo3d.ai) → 登录 → **API Keys** → 新建 | bridge-tripo 试跑跑不了 |
| `执行后端密钥` | 你另配的那个 LLM key | AI 对抗预审、语义冲突比对跑不了 |
| `下游配置.oaicompat.地址` + `.模型` | 那个 key 的 **OpenAI 兼容 base URL** 与模型 id。**判断 base URL 对不对只有一条标准：它加上 `/chat/completions` 能收 POST**。DeepSeek 就是 `https://api.deepseek.com/v1` + `deepseek-chat` | 同上 |
| `生图密钥` | 线上生图那个中转的 API Key。**跟 `执行后端密钥` 是两把钥匙**——同一个中转也各算各的，别指望填一处两处都通 | **生图整条路跑不了**：oaiimage 是「生图」域的默认 driver，`bridge.generate` 不给 `--Driver` 时走的就是它。要回本地那条路加 `--Driver comfyui` |
| `下游配置.oaiimage.地址` + `.模型` | 那个 key 的 **OpenAI 兼容 base URL** 与图像模型 id。**判断 base URL 对不对只有一条标准：它加上 `/images/generations` 能收 POST**（`/v1` 结尾那一段要带上）。模型填 `gpt-image-1` 或 `dall-e-3`；填之前先跑一次 `bridge.probe --Driver oaiimage`，它只查 `/models`、不出图不花钱，回来的清单里有哪个就填哪个 | 同上 |

**填完的完整长相**（照抄，把占位换成真值；填不了的整条删掉）：

```json
{
  "飞书应用密钥": "粘贴 App Secret",
  "模型生成密钥": "粘贴 tripo API Key",
  "执行后端密钥": "粘贴 LLM key",
  "生图密钥": "粘贴线上生图 key",

  "下游配置": {
    "comfyui": { "地址": "http://127.0.0.1:8188", "超时秒": 900 },
    "blender": { "可执行文件": "D:/Tools/Blender/blender.exe", "超时秒": 900 },
    "feishu": {
      "应用标识": "cli_粘贴 App ID",
      "多维表格标识": "粘贴 bascn 那一段",
      "超时秒": 60
    },
    "tripo": { "地址": "https://openapi.tripo3d.ai/v3", "超时秒": 600 },
    "oaicompat": { "地址": "https://api.deepseek.com/v1", "模型": "deepseek-chat", "超时秒": 120 },
    "oaiimage": { "地址": "https://中转域名/v1", "模型": "gpt-image-1", "尺寸": "1024x1024", "超时秒": 180 }
  }
}
```

## 三、飞书那边还要做三件事（在开放平台点，不在这里填）

1. **开权限**：`bitable:app`（多维表格读写）、`im:message`（发消息卡片）、
   `contact:user.id.readonly`（成员表要 open_id）。开完要**发布版本并等审核通过**，
   否则 token 拿得到但调用一律 403。
2. **把机器人拉进那张多维表格**：多维表格 → 右上角「…」→ 添加文档应用 → 选你的自建应用。
   不加这一步，app_token 对得上也读不到表。
3. **事件订阅**（只有唤醒事件源那一项要）：**已经不用你配回调地址了**——
   走长连接，本机 NAT 后面也能收事件，消息事件已经真跑通（P8 批次 9）。

4. **把应用在那张多维表格上的权限从「可编辑」提到「可管理」。**
   这一条是**表格记录变更事件**唯一还差的东西：
   订阅云文档事件那个接口**只认文档拥有者与文档管理者**，
   我们的应用现在是 `edit`，所以一直回 `1069603 forbidden`（真调接口读出来的）。
   点法：打开那张多维表格 → 右上角「分享 / 协作者」→ 找到你的自建应用 →
   权限改成「可管理」（有的版本叫「完全访问」）→ 保存。
   改完跟我说一声，我重跑一次订阅接口补验，**代码一个字都不用改**。
   我没有自己去改它——那是在你的工作区里给应用提权，得你自己点。

## 四、执行后端那个 key：任何 OpenAI 兼容服务都行

驱动按**协议**写，不按厂商写（决策 80），所以 DeepSeek / 通义 / 智谱 / 自建 vLLM
随便哪个都能接，只要它认 `POST {地址}/chat/completions`。
示例文件里预填的是 DeepSeek 的地址和模型名，不是你的，按你自己的 key 改。

## 五、我这边同时在干的（不用你管）

| 事 | 状态 |
|---|---|
| Blender 4.2 绿色装到 `D:\Tools\Blender` | 装 |
| ComfyUI + CUDA 版 torch 装到 `D:\Tools\ComfyUI` | 装 |
| SDXL 底模 6.9G 下到 ComfyUI 的 checkpoints | 下 |
| Impact-Pack 节点（依赖清单点名的那个） | 装 |

全部绿色安装，不写系统 PATH、不动注册表、不提权。

---

> 填完不用叫我，我会在做到对应批次时自己去读。
> 只有**飞书事件订阅**那一条会回来问你（见第三节第 3 点）。

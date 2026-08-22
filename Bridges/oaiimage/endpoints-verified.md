# oaiimage 端点实证表

> 这份表的规矩跟 tripo 那份一样：**每一行都要是真发过一次请求、拿到真回包**记下来的，
> 不是照文档抄的（决策 94）。还没实证的一律写进最后一节，不许混进上面的表装成已验。

`{base}` = 本机配置 `下游配置.oaiimage.地址`，形如 `https://<中转域名>/v1`。
下游是一个 **OpenAI 兼容的 GPT 中转**，不是 openai.com 本体——所以官方文档只能当参考，
以真回包为准。

## 桥用到的三个端点

| 端点 | 方法 | 内容类型 | 桥里的用处 |
|---|---|---|---|
| `{base}/models` | GET | — | `caps` 动作。只列模型，不产图、**不花钱**，是面板「试跑一次」的落点 |
| `{base}/images/generations` | POST | `application/json` | `generate` 动作，预设的「接口」是 `generations` 时走这条 |
| `{base}/images/edits` | POST | `multipart/form-data` | `generate` 动作，预设的「接口」是 `edits` 时走这条 |

提交体形状：

```
POST {base}/images/generations
{"model":"…","prompt":"…","n":1,"size":"1024x1024"}

POST {base}/images/edits            （multipart/form-data）
image=<参考图字节>  prompt=…  model=…  n=1  size=1024x1024
```

## `response_format` 一个字都不发

这是本桥最容易踩、也最不容易看出来的一条：

- **`gpt-image-1` 不认 `response_format`**。传了它报未知参数当场 400，
  而它的回包**恒为 `b64_json`**——它根本没有 url 那一路。
- **`dall-e-3` 认 `response_format`，而且默认回的是 `url`**（一个有时效的临时地址）。

中转背后挂什么模型不由我们决定，甚至可能随时间换。所以桥的做法是：
**请求侧对这个参数不表态（压根不发），解析侧 `b64_json` 与 `url` 两种都吃**
（`ImageClient.ParseImages`）。只写一种迟早炸。

取 url 那一路用的是**另一个不带任何请求头的 HttpClient**：
图片 URL 常常指向对象存储的另一个域，把 `Authorization` 带过去等于把密钥发给第三方。

## 本接口不收 seed

OpenAI 图像接口**没有 seed 参数**，两个端点都没有。因此：

- 溯源边车的「随机种」**恒为空串**，不是忘了填；
- 边车的「机检结果」里写死一句「本接口不收种子，同样提示词不保证复现」；
- 调用方要是给了 `--Seed`，桥**不发它**，并在 stderr 与边车的「未发出的种子」里点名说清。

不这么做的话，边车看上去是齐全的，而照着它根本重现不出那张图——
那是最糟的一种缺（决策 26 的反面）。

## 也没有 prompt id

图像接口的回包只有 `created` 与 `data`，**没有任何任务 id**。
协议响应里那个 `prompt_id` 是**桥本地现编的**（`oaiimage-<guid>`），
只为把同一次调用出的几张图串起来，拿它回下游查任何东西都查不到。

## 错误码映射

照 oaicompat 的 `SendChatCompletion` 对齐，按 **HTTP 状态码**判分支
（这个中转不像 tripo 那样在 body 里另给一套业务码；哪天发现它给了，这一节要重写）：

| HTTP | 桥映射成 | 可重试 |
|---|---|---|
| 连不上（DNS / 拒连 / TLS 失败） | `下游不可达` | 是 |
| 401 / 403 | `凭据无效` | 否 |
| 429 | `限流` | 是 |
| 5xx | `下游报错` | 是 |
| 其余 4xx | `下游报错`，人话带服务端 `error.message` | 否 |
| 超时 | `超时` | 是 |

## 与官方 API 的差异（中转特有，必须一条条实证）

中转不是 openai.com，以下几处**每换一个中转都要重新验**：

1. **`/models` 列出来的东西不一定能用**。中转常常把上游全部模型原样透出来，
   里面混着它自己并没有开通的。所以 `caps` 的产出只是「候选清单」，不是「能用清单」。
2. **`size` 的可选档位跟着背后的模型走**。`gpt-image-1` 是 1024x1024 / 1024x1536 / 1536x1024，
   `dall-e-3` 是 1024x1024 / 1792x1024 / 1024x1792。预设里的「尺寸」写成「规格」时是拿
   资产请求的宽高现算的，算出来不在档位里会被下游按参数非法退回。
3. **`n` 未必按标准实现**。`dall-e-3` 官方只认 `n=1`；中转有的会自己拆成多次调用，有的直接报错。
4. **`/images/edits` 的字段名**。官方是 `image`（`gpt-image-1` 还支持 `image[]` 多图），
   本桥只发单个 `image`。中转若只转发不改写，这一路就与官方一致。

## multipart 的字段名必须自己加引号

实证发现，**不能用 `MultipartFormDataContent.Add(content, name)` 那个重载**：
.NET 把字段名交给 `ContentDispositionHeaderValue`，而 `image` 这种合法 token 会被原样写成
`name=image`，**不带引号**。抓到的请求体里字段列表是空的就是这么来的。

RFC 7578 要求带引号。宽松的解析器无所谓，严格的会把整个表单判成没有 `image` 字段，
回过来的是那句 `image is a required parameter`——指不到「引号」两个字上。
所以桥自己拼 `Content-Disposition`（`ImageClient.AddFormPart`），按最严的那一档写。

## 已经真跑过的 vs. 还没实证的

**已经真跑过**（2026-08-22，对着一个本地假下游，见下）：

| 走通的路 | 证据 |
|---|---|
| `caps` → `GET /models` | 回 3 个模型，写盘 + 响应载荷两份一致 |
| `generate` → `generations`，回 `b64_json` | 2 张图落盘，宽高从 PNG 头读出 64×48 |
| `generate` → `edits`，回 `url` | multipart 六段齐全，取图后落盘，宽高 32×16 |
| 取 url 时**不带** `Authorization` | 假下游那一侧收到的授权头是 `null` |
| 401 → `凭据无效` | 换个错密钥即复现 |
| 连不上 → `下游不可达`（可重试） | 指向不存在的主机 |
| 预设两个方向的锚点槽拦截 | edits 不给参考图、generations 给了参考图，各报各的话 |
| 中文「命名」落成 ASCII 文件名 | 「主菜单图标」→ `variant-01.png` |
| 中文键在非交互式宿主下的 UTF-8 往返 | 整条链路走 `bridge.probe` / `bridge.generate` 命令层 |

那个假下游是 `scratchpad/stub_server.py`，**不入库**：它照 OpenAI 的形状回包，
证明的是**桥这一侧**的请求形状、两种回包解析、落盘与边车，
**不证明任何一个真中转的行为**。

**还没对着真中转跑过**——本机 `local.json` 里既没有 `生图密钥`，也没有 `下游配置.oaiimage`。
第一次真跑通之后必须回来补的：

- `/models` 回包的实际形状（现在按 `data[].id` 取，取不到就报「没有 data 数组」）；
- `generations` 与 `edits` 各自回的是 `b64_json` 还是 `url`，以及背后到底是哪个模型；
- 那个中转对 `size` / `n` 的实际接受范围；
- 4xx 里有没有 body 业务码——有的话上面那张错误码表要按业务码重写，
  就像 tripo 那份表最后收敛成的样子。

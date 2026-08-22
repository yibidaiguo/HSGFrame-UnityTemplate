# tripo v3 端点实证表

> 这份表里的每一行都是**真发过一次请求、拿到真回包**记下来的，不是照文档抄的。
> 由来：桥当初整个对着 v2 写，base URL 是凭印象写进任务书的（待办 3）。
> 规矩因此立死：**外部 API 的主机、版本与端点形状，一律以真回包为准**（决策 94）。

实证时间：2026-08-21。账号 API 可用积分 0（`balance: 0.00`），
所以**凡是要花积分的那一步都停在 403/2010**——这恰好是最好的形状证据：
参数关过了才轮得到积分关。

| 端点 | 方法 | 真回包 | 说明 |
|---|---|---|---|
| `{base}/account/balance` | GET | `200 {"code":0,"status":"success","data":{"balance":0.00,"frozen":0.00}}` | 唯一一个能真跑到成功的 |
| `{base}/generation/text-to-model` | POST | `403 {"code":2010,…"You don't have enough credit…"}` | 形状过关，卡积分 |
| `{base}/generation/image-to-model` | POST | 同上 | 带真图 URL 时形状过关，卡积分 |
| `{base}/tasks/{task_id}` | GET | `404 {"code":2001,"message":"The task is not found"}` | 端点在，任务不属于本账号 |
| `{base}/upload` | POST | `404 {"code":4001,"message":"No endpoint found: POST /v3/upload"}` | **v3 没有这个端点** |
| `{base}/upload/sts` | POST | `404 {"code":4001,…}` | **v3 也没有这个端点** |

`{base}` = `https://openapi.tripo3d.ai/v3`。**v2 的 `api.tripo3d.ai` + `/v2/openapi` 已作废。**

## 提交体形状（两条路都实证过）

```
POST {base}/generation/text-to-model
{"prompt":"…","model":"v3.0-20250812","texture":false,"pbr":false,"face_limit":3000}

POST {base}/generation/image-to-model
{"model":"v3.0-20250812","file":{"type":"png","url":"https://…"},"texture":false,"pbr":false,"face_limit":3000}
```

- v2 的 `type` 与 `model_version` 两个键 **v3 不认**，换成了 `model`。
- `file` 走「下游自己去取的 URL」这一路。**本地图片没法直传**——
  `/upload` 与 `/upload/sts` 都回 4001，所以要么图先有公网地址，要么这条路暂时用不了。
  证据：URL 不可达时回 `1004 input image is not accessible`，可达时直接进 2010。

## `model` 只认这四个值

```
P1-20260311 · v2.5-20250123 · v3.0-20250812 · v3.1-20260211
```

这不是从文档抄的，是服务端自己列的：拿 `tripo-v3.1`（官方快速开始页上写的那个）去试，
回 `400 {"code":1004,"message":"invalid model 'tripo-v3.1', allowed values: …"}`。
**官方文档那一页自己写错了。**

## 清单是探出来的，不是抄来的（caps 动作）

上面那四个值现在**不再抄进代码当白名单用**，而是每次探测时问服务端要一遍。
`caps` 动作走的就是这一条路：

```
POST {base}/generation/text-to-model
{"prompt":"catalog probe","model":"__catalog_probe__","texture":false,"pbr":false,"face_limit":3000}
```

`__catalog_probe__` 是**故意不可能合法**的哨兵值，它唯一的作用是被拒。服务端按
`1004` 退回来，那句报错里带着 `allowed values: …`——清单就在那里。

**为什么这不花积分**：参数关在积分关之前（上表里 `text-to-model` 带合法参数才走到 2010）。
桥里对这一点有两道硬约束：

- 回包**不是** `1004` → 照它本来的错误码报（1005 是密钥问题、2010 是积分问题），不冒充「清单读不出来」；
- 回包是 **2xx** → 当成事故报出来：哨兵没被拒，意味着可能真提交了一个任务，
  人话里直接让人去控制台看一眼、并换一个更不可能合法的哨兵值。

解析不出 `allowed values` 时**报失败，不返回空清单**——「没探到」与「探过了、是空的」
在上层是两句完全不同的话。

**这一节还没实证**（2026-08-22）：这台机器上 `模型生成密钥` 现在被服务端拒（`HTTP 401`），
探测跑到「凭据无效」就停了，**没能拿到一句真的 1004 回包**。
解析规则是照上面那句历史原文写的（`allowed values:` 之后按逗号切）。
**拿到能用的密钥后第一件事就是重跑一次 `bridge.probe --Driver tripo`，
把真 message 原文抄到这里，并核对分隔符与措辞。**

## 错误码语义

| code | HTTP | 含义 | 桥映射成 |
|---|---|---|---|
| 1004 | 400 | 参数非法 | `请求不合协议`（是我们发的形状不对，不是账号问题） |
| 1005 | 403 | 密钥无权访问此资源 | `凭据无效` |
| 2001 | 404 | 任务不存在 | `下游报错` |
| 2010 | 403 | API 积分不足 | `额度不足`（写明「不是代码坏了」） |
| 4001 | 404 | 端点不存在 | `下游报错`（写明「base URL 或版本写错了」） |

**桥按 `code` 判分支，不按 HTTP 状态码判**——同一个 403 底下 2010 与 1005 是两件事，
同一个 404 底下 2001 与 4001 也是两件事。

## 还没实证的

- **成功回包的 `output` 形状**：提交那一步一直卡在 2010，所以从来没有一个任务跑到 success。
  桥现在按 `model` / `pbr_model` / `base_model` 三个候选键挨个试，取到哪个用哪个。
  **第一次真跑到成功，必须核对实际键名并把这段收敛成实证过的那一个。**
- **`queued` / `running` / `success` 这几个状态字符串**同理，只有单测覆盖，没有真回包。

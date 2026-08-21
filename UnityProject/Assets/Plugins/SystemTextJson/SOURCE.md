# System.Text.Json 的 dll 从哪来

Unity 6000.3 的运行时不带 `System.Text.Json`（2026-08-15 阶段 16a 实测：
同一批 Logic 源码在 Unity 侧报 `CS0246: JsonPropertyName` / `CS0234: System.Text.Encodings`）。
方案模块 1 把它定为存档序列化的默认，所以这里把 dll 快照进工程 ——
与 `Tools/Deps/Unity.Mathematics.dll` 是同一个手法。

| 项 | 值 |
|---|---|
| 包版本 | `System.Text.Json` 8.0.5（NuGet） |
| 目标框架 | `netstandard2.0`（Unity 的 netstandard2.1 profile 吃得下） |
| 取法 | 建一个 netstandard2.0 类库引这个包，`dotnet publish` 后从产物里取 |

只放了四个 dll：`System.Text.Json` / `System.Text.Encodings.Web` / `Microsoft.Bcl.AsyncInterfaces` / `System.Runtime.CompilerServices.Unsafe`。
`System.Buffers`、`System.Memory`、`System.Numerics.Vectors`、`System.Threading.Tasks.Extensions`
**刻意没放** —— Unity 的 netstandard shim 里已经有它们，再放一份会撞类型。

`System.Runtime.CompilerServices.Unsafe` 是**踩坑补上的**：Unity 的 shim 只在编译期提供它的 facade，
运行时解析不到，症状是「编译通过、但程序集静默不加载」——
EditMode 测试会从 2 条变成 0 条而退出码仍是 0。补上这个 dll 才真正可用。将来升级 Unity 或升级这个包时，先按这个最小集合试，报缺再逐个补。

**升级方式**：重跑上面的取法，覆盖这三个 dll，然后跑
`./Template/Tools/Gates/gate-unity.ps1` 确认 Unity 侧仍然编译得过。

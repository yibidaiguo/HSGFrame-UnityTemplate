# 内置依赖来源

这个目录放**编译要用、但不从网上现取**的二进制。目标只有一条：
**执行机不联网也能编。** 许可账在 [LICENSES.md](LICENSES.md)（生成物）。

目录里有两类东西，别混：

| 谁 | 是什么 | 谁管它 |
|---|---|---|
| `lib/` `native/` `props/` `nupkg/` `LICENSES.md` | **生成物**，由 `fetch.ps1` 按清单产出 | 改清单再重跑，不要手改 |
| `Unity.Mathematics.dll` 与 NodeGraph 三个 dll | **手工快照**，上游没发 NuGet 包 | 见下面「手工快照」一节 |

## 一条命令重取全部

```bash
pwsh -NoProfile -File Tools/Deps/fetch.ps1
```

要联网——**它就是「联网取一次，之后不用再联网」的那一次**。
清单是 [`packages.txt`](packages.txt)（能铺平的）与 [`packages-feed.txt`](packages-feed.txt)（铺不平的）。
改版本号 → 重跑 → `lib/` `props/` `LICENSES.md` 一起重生成，`git diff` 看得见换了哪些 dll。

只重取一个束：`fetch.ps1 -Bundle SvgSkia`。换平台：`fetch.ps1 -Rid linux-x64`。

## 为什么是两条路而不是一条

- **能铺平的**（包里只有程序集）→ `lib/` + `props/<束>.props`，用 `<Reference><HintPath>` 挂。
  引用面干净，连还原都不用。
- **铺不平的**（包里还带 MSBuild targets、分析器、测试宿主）→ `nupkg/` 收原样 `.nupkg`，
  当本地 NuGet 源用（仓库根 `NuGet.config` 里 `<clear />` 掉了 nuget.org）。
  `Microsoft.NET.Test.Sdk` 少了它 `dotnet test` 根本不认这个工程是测试工程，
  这种东西 `HintPath` 挂不上去。

两条合起来才是「不联网也能编」。只做第一条的话，`dotnet test` 仍然要联网还原 xunit。

## 传递依赖必须一起搬——这是这套东西存在的理由

手数传递依赖必然漏，**漏一个的症状是「换台机器才炸」**。所以 `fetch.ps1` 不数：
它建一个临时工程、让 NuGet 解闭包、把解出来的整个闭包搬过来。

两个真踩到的例子，说明「凭印象数」为什么不行：

- 以为 ImageSharp 在 net8.0 带 `System.Text.Encoding.CodePages`——**不带**。
  那是 netstandard2.0 那一档的事，net8.0 内置了。
- 以为 Roslyn 就两个程序集——在 net8.0 是两个，
  在 **netstandard2.0 是十个**（`System.Collections.Immutable` 那一串）。
  而源生成器只能是 netstandard2.0。所以清单里目标框架是**一列数据**，不是全局开关。

## 原生库只搬了 win-x64

`Svg.Skia` 带 SkiaSharp 与 HarfBuzzSharp 的原生库，每平台一份。
`native/win-x64/` 下只有这一个平台的两个文件（`libSkiaSharp.dll` 11.6 MB、`libHarfBuzzSharp.dll` 2 MB）。

**这是有意的取舍，不是漏了。** 上游那几个包把 x86 / x64 / arm64 装在一个包里，
Win32 那个包解开有 285 MB；三个平台全搬进 git 是十倍的代价换一个现在没有的需求。

代价写清楚：**在别的平台上，这条 SVG→PNG 的链会在运行时报找不到 `libSkiaSharp`。**
不是编译期报错，是运行期——所以真要换平台，先跑 `fetch.ps1 -Rid <目标>`
（要联网），把那一份 `native/<rid>/` 一起提交，再改 `props/` 里指向的 rid。

其余四个束（ImageSharp / ClosedXML / Scriban / Roslyn）纯托管，跨平台没有这个问题。

## 内置依赖与 PackageReference 有一处不一样

`PackageReference` 会顺着 `ProjectReference` 传给上游工程，**`<Reference>` 不会**。
所以谁用谁自己 `<Import>`，不能指望从被引的工程漏过来。
症状是编译时报 `CS0246: 未能找到类型或命名空间名`，改法是给那个工程也加一行 `<Import>`。

## 体积

`lib/` 29 MB + `native/` 14 MB + `nupkg/` 20 MB ≈ **63 MB 进 git**。
大头是 `libSkiaSharp.dll`（11.6 MB）、`DocumentFormat.OpenXml.dll`（6 MB）、
Roslyn 两个（8.5 MB）、`Microsoft.CodeCoverage` 的包（9 MB）。
这是「内置化」的标价——换来的是执行机不联网也能编、且版本不会随上游漂。
将来嫌大，能砍的地方按性价比排：先看 ClosedXML 那条链还用不用得上（12 MB），
再考虑把 `native/` 挪到 git-lfs。

## 网页资源那一档（`web/`）

给**浏览器**用的 js/css，不走 NuGet，所以另立一份清单与一个脚本：

```bash
pwsh -NoProfile -File Tools/Deps/fetch-web.ps1
```

清单是 [`packages-web.txt`](packages-web.txt)（文件名 / 版本 / 下载地址）。
每个文件旁边落一份 `.version`，记的是取的哪一版、从哪儿取的——
文件名里不带版本号（页面按固定名字引它），不记的话「现在装的是哪一版」只能翻 git 历史。

| 文件 | 版本 | 许可 | 谁用它 |
|---|---|---|---|
| `model-viewer.min.js` | @google/model-viewer 3.5.0 | Apache-2.0 | `model.viewer` 出的可交互模型预览页 |

**为什么内置而不是写个 CDN 地址**：这些页面是我们自己的 HTTP 服务发出去的。
一个由本机服务托管、却要连外网才显示的页面，坏起来的样子是「点开一片空白」，
而那时人第一反应是「预览功能坏了」，不会想到是 CDN 连不上。
何况这台机器上国内域名与国外域名的通断本来就不一致（BUG-0003）。

## 手工快照（`Unity.Mathematics.dll` 与 NodeGraph 三个）

这四个不在 `fetch.ps1` 管的范围里——上游还没发 NuGet 包，只能手工取。

| 文件 | 版本 | 来源 |
|---|---|---|
| `Unity.Mathematics.dll` | Unity 6000.3.11f1 随附 | 编辑器 `Data/Resources/PackageManager/BuiltInPackages`，或工程 `Library/ScriptAssemblies` |
| `NodeEditor.Runtime.dll` | NodeGraph 0.1.5 | `D:\Projects\Unity\GraphTest` 的 `Release/Packages/NodeEditor.Runtime.csproj` |
| `NodeEditor.UnityShim.dll` | 同上 | `Release/Packages/NodeEditor.UnityShim.csproj` |
| `Dialogue.Runtime.dll` | 同上 | `Release/Packages/Dialogue.Runtime.csproj` |

NodeGraph 三个的重取步骤：上游仓库是只读的，**编译不要在它里面落 `bin/` 与 `obj/`**。
把三个 csproj 与两个包的 `Runtime/` + `Shim~/` 复制到临时目录，在那里编：

```bash
dotnet build Dialogue.Runtime.csproj -c Release -o out --nologo
```

`Dialogue` 会连带把 `NodeEditor` 两个一起产出，再把 `out/` 下三个 dll 拷回本目录。
`NodeEditor.UnityShim.dll` 只含 Unity 序列化特性的空实现，纯 .NET 侧编译要它，
Unity 侧则由真的 `UnityEngine` 提供——上游用 `Shim~` 目录名让 Unity 天然忽略它。

上游发出 NuGet 包之后，这三条应当挪进 `packages.txt`，与其余五个束同一条路。

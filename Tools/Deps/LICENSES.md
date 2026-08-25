# 内置依赖的许可

**由 `Tools/Deps/fetch.ps1` 生成，不要手改。**

这份表记的是 `lib/` 与 `native/` 里那些二进制的许可。
内置化之后 dll 进了 git，我们的身份就从「引用第三方库」变成「分发第三方库」——
这两件事的许可义务不一样，所以这份账必须存在，且必须跟着 `packages.txt` 一起变。

共 50 个包（5 个束的闭包合起来，去重后）。

| 包 | 版本 | 许可 | 属于哪个束 |
|---|---|---|---|
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | 0.104.2 | MIT | ClosedXML |
| [ClosedXML.Parser](https://github.com/ClosedXML/ClosedXML.Parser) | 1.2.0 | MIT | ClosedXML |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | 3.1.1 | MIT | ClosedXML |
| [DocumentFormat.OpenXml.Framework](https://github.com/dotnet/Open-XML-SDK) | 3.1.1 | MIT | ClosedXML |
| [ExcelNumberFormat](https://github.com/andersnm/ExcelNumberFormat) | 1.1.0 | MIT | ClosedXML |
| [ExCSS](https://github.com/TylerBrinks/ExCSS) | 4.3.1 | MIT | SvgSkia |
| [HarfBuzzSharp](https://go.microsoft.com/fwlink/?linkid=868515) | 14.2.0 | MIT | SvgSkia |
| [HarfBuzzSharp.NativeAssets.Linux](https://go.microsoft.com/fwlink/?linkid=868515) | 14.2.0 | MIT | SvgSkia |
| [HarfBuzzSharp.NativeAssets.macOS](https://go.microsoft.com/fwlink/?linkid=868515) | 14.2.0 | MIT | SvgSkia |
| [HarfBuzzSharp.NativeAssets.Win32](https://go.microsoft.com/fwlink/?linkid=868515) | 14.2.0 | MIT | SvgSkia |
| [Microsoft.CodeAnalysis.Analyzers](https://github.com/dotnet/roslyn-analyzers) | 3.3.3 | MIT | Roslyn |
| [Microsoft.CodeAnalysis.Common](https://github.com/dotnet/roslyn) | 4.3.1 | MIT | Roslyn |
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) | 4.3.1 | MIT | Roslyn |
| [Microsoft.CodeCoverage](https://github.com/microsoft/vstest) | 17.11.1 | MIT | 本地源 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.11.1 | MIT | 本地源 |
| [Microsoft.NETCore.Platforms](https://dot.net/) | 1.1.0 | http://go.microsoft.com/fwlink/?LinkId=329770 | Roslyn |
| [Microsoft.TestPlatform.ObjectModel](https://github.com/microsoft/vstest) | 17.11.1 | MIT | 本地源 |
| [Microsoft.TestPlatform.TestHost](https://github.com/microsoft/vstest) | 17.11.1 | MIT | 本地源 |
| [NETStandard.Library](https://dot.net/) | 2.0.3 | https://github.com/dotnet/standard/blob/master/LICENSE.TXT | Roslyn |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.1 | MIT | 本地源 |
| RBush | 4.0.0 | MIT | ClosedXML |
| [Scriban](https://scriban.github.io/) | 7.2.6 | BSD-2-Clause | Scriban |
| [ShimSkiaSharp](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MIT | SvgSkia |
| [SixLabors.Fonts](https://github.com/SixLabors/Fonts) | 1.0.0 | Apache-2.0 | ClosedXML |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | 2.1.13 | Apache-2.0 | ImageSharp |
| [SkiaSharp](https://go.microsoft.com/fwlink/?linkid=868515) | 4.148.0 | MIT | SvgSkia |
| [SkiaSharp.NativeAssets.macOS](https://go.microsoft.com/fwlink/?linkid=868515) | 4.148.0 | MIT | SvgSkia |
| [SkiaSharp.NativeAssets.Win32](https://go.microsoft.com/fwlink/?linkid=868515) | 4.148.0 | MIT | SvgSkia |
| [Svg.Animation](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MIT | SvgSkia |
| [Svg.Custom](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MS-PL | SvgSkia |
| [Svg.Model](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MIT | SvgSkia |
| [Svg.SceneGraph](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MIT | SvgSkia |
| [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) | 5.2.1 | MIT | SvgSkia |
| [System.Buffers](https://dot.net/) | 4.5.1 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | Roslyn |
| [System.Collections.Immutable](https://dot.net/) | 6.0.0 | MIT | Roslyn |
| [System.IO.Packaging](https://dot.net/) | 8.0.1 | MIT | ClosedXML |
| [System.Memory](https://dot.net/) | 4.5.4 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | Roslyn |
| [System.Numerics.Vectors](https://dot.net/) | 4.4.0 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | Roslyn |
| [System.Reflection.Metadata](https://github.com/dotnet/runtime) | 5.0.0 | MIT | Roslyn |
| [System.Runtime.CompilerServices.Unsafe](https://dot.net/) | 6.0.0 | MIT | Roslyn |
| [System.Text.Encoding.CodePages](https://dot.net/) | 6.0.0 | MIT | Roslyn |
| [System.Threading.Tasks.Extensions](https://dot.net/) | 4.5.4 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | Roslyn |
| xunit | 2.9.2 | Apache-2.0 | 本地源 |
| [xunit.abstractions](https://github.com/xunit/xunit) | 2.0.3 | https://raw.githubusercontent.com/xunit/xunit/master/license.txt | 本地源 |
| xunit.analyzers | 1.16.0 | Apache-2.0 | 本地源 |
| xunit.assert | 2.9.2 | Apache-2.0 | 本地源 |
| xunit.core | 2.9.2 | Apache-2.0 | 本地源 |
| xunit.extensibility.core | 2.9.2 | Apache-2.0 | 本地源 |
| xunit.extensibility.execution | 2.9.2 | Apache-2.0 | 本地源 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | 本地源 |

## 要盯着的两条

- **SixLabors.ImageSharp 钉在 2.1.x** —— 2.1 是 Apache-2.0 的最后一支。
  3.x 起换成 Six Labors 分割许可（开源与小企业免费，商用要买）。
  升级这一个包等于换许可，**不是版本号问题**，要单独拍板。
- **SkiaSharp / HarfBuzzSharp 带原生库**，各自的 `THIRD-PARTY-NOTICES.txt`
  列了 Skia 与 HarfBuzz 上游一长串依赖的许可。分发这些 `.dll` 时那份 notice 也要带上，
  已随包收在 `NOTICES/` 下（21 份）。

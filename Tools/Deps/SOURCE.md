# 依赖快照来源

这个目录放的是**纯 .NET 侧编译要用、而 Unity 侧由 UPM 包提供**的程序集快照。
同一份能力在两侧各有一条获取路径，这里记的是 dotnet 那一条的出处，便于随上游升级重取。

Unity 侧不看这个目录（它在 `Assets/` 之外），所以不会与 UPM 包里的同名程序集重复定义。

| 文件 | 版本 | 来源 | 重新生成的办法 |
|---|---|---|---|
| `Unity.Mathematics.dll` | Unity 6000.3.11f1 随附 | Unity 编辑器安装目录 | 从同版本编辑器的 `Data/Resources/PackageManager/BuiltInPackages` 或工程 `Library/ScriptAssemblies` 取 |
| `NodeEditor.Runtime.dll` | NodeGraph 0.1.5 | `D:\Projects\Unity\GraphTest` 的 `Release/Packages/NodeEditor.Runtime.csproj` | 见下面「重取步骤」 |
| `NodeEditor.UnityShim.dll` | 同上 | `Release/Packages/NodeEditor.UnityShim.csproj` | 同上 |
| `Dialogue.Runtime.dll` | 同上 | `Release/Packages/Dialogue.Runtime.csproj` | 同上 |

## NodeGraph 三个 dll 的重取步骤

上游仓库是只读的，**编译不要在它里面落 `bin/` 与 `obj/`**。做法是把源码复制到临时目录再编译：

```bash
# 1. 把三个 csproj 与两个包的 Runtime/ + Shim~/ 复制到一个临时目录
# 2. 在临时目录里编译，Dialogue 会连带把 NodeEditor 两个一起产出
dotnet build Dialogue.Runtime.csproj -c Release -o out --nologo
# 3. 把 out/ 下三个 dll 拷回本目录
```

`NodeEditor.UnityShim.dll` 只含 Unity 序列化特性的空实现，纯 .NET 侧编译要它，
Unity 侧则由真的 `UnityEngine` 提供——上游用 `Shim~` 目录名让 Unity 天然忽略它。

## 为什么是 dll 快照而不是 NuGet 或跨仓库 ProjectReference

上游还没发 NuGet 包；跨仓库 `ProjectReference` 会让模板不能独立 clone。
dll 快照与 `Unity.Mathematics.dll` 是同一手法，已经验证可行。
上游发出 NuGet 包之后，这三条应当换成 `PackageReference`。

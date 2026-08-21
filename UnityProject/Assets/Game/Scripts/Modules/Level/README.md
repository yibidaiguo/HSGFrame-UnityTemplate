# 模块 · Level（关卡）

一句话职责：把关卡定义（区块 + 逻辑实体摆放）在「纯数据」与「Unity 场景」两种形态之间往返。

## 公开面

**模块外只准用 `Contracts/`**（R2：公开面 = Contracts + Events，其余都是私有）。

| 类型 | 在哪 | 干什么 |
|---|---|---|
| `ILevelEntityView` | `Contracts/` | 关卡实体的只读视图：编号、类别、位置、参数字典 |
| `ILevelEntityCatalog` | `Contracts/` | 当前关卡全部实体的名录，按编号或类别检索 |
| `ILevelEntityResourceMap` | `Contracts/` | 实体类别 → YooAsset 资源地址的只读映射 |
| `LevelEntityCatalogRegistry` | `Contracts/` | 名录的挂靠点：装配方 `Publish`，模块外读 `Current` |

模块内部（**模块外引到即违规**）：`Data/` 是零 UnityEngine 的纯数据形态
（定义 / 序列化 / 校验 / 仓库 / 两个契约的纯 C# 实现，可在纯 dotnet 下跑测试）；
`View/` 是场景侧（`LogicEntityMarker` 标记、`LevelEntityResourceMapAsset` 映射资产、
`LevelEntitySpawner` 运行时装配器）。
`Contracts/` 与 `Data/` 归 `Game.Logic`；`View/` 靠 `Game.View.asmref` 归并进 `Game.View`。

## 依赖了谁

- 不订阅任何模块的事件，也不引用别的模块。
- 反向：`Toolkit.Editor` 的 `LevelSceneBuilder` / `LevelSceneExporter` 在「场景 ↔ 关卡 JSON」两个方向上做转换。

## 常用命令

导出场景为关卡 JSON：`unity-cmd.ps1 -ExecuteMethod Template.Toolkit.Editor.LevelSceneCommandLine.ExportFromCommandLine`；
生成运行时资产：`unity-cmd.ps1 -ExecuteMethod Template.Toolkit.Editor.RuntimeAssetScaffoldCommandLine.ScaffoldFromCommandLine`。

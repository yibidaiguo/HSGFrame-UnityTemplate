# 各模块已经实现了什么

下面是从代码里抽出来的**公开面摘要**（接口、事件、公开方法），不是全部代码。
聊需求时先看这里：**已经有的别当成新需求**，顺着既有实现往下谈。
拿不准某个细节时，如实说「我看的是接口摘要，具体实现要看代码」——
**不许因为摘要里没写就断言项目里没有**。

## Combat

**公开类型**
- class DamageService —— 伤害结算服务。
- class UnitState —— 单位的运行时状态。
- class UnitTemplate —— 单位的静态配置数据。

**公开成员**
- `public static void Apply(UnitState target, int damageAmount)`

## Level

**公开类型**
- interface ILevelEntityCatalog —— 当前关卡里全部实体的只读名录，模块外按编号或类别检索。
- interface ILevelEntityResourceMap
- interface ILevelEntityView
- class LevelEntityCatalogRegistry
- class LevelChunk —— 关卡区块：区块名与其包含的逻辑实体摆放。
- class LevelDataException —— 关卡数据读取或解析失败时抛出，消息按四要素书写。
- class LevelDefinition —— 关卡元信息：关卡名、环境名与区块清单。
- class LevelEntityCatalog —— 关卡实体名录的实现：收下一批标记，按编号与类别建索引。
- class LevelEntityResourceMap
- class LevelRepository —— 关卡仓库：关卡元信息与区块分开按需读盘，读过的留在内存里复用，可按块卸载。
- class LevelSerializer —— 关卡 JSON 与内存模型的双向转换。
- class LevelValidator —— 关卡结构校验：检查区块清单一致性、实体编号唯一性与必填字段。
- struct LevelVector3 —— 纯 C# 三分量结构，表示逻辑实体的位置，与 Unity.Mathematics 保持无关，以便在服务器侧运行。
- class LogicEntityPlacement —— 单个逻辑实体的摆放：编号、类别、位置、朝向角度与自由参数。
- class EntityParameterEntry —— 实体自由参数的一条键值，做成可序列化的类是为了让参数在 Inspector 里看得见。
- class LevelEntityResourceEntry —— 「实体类别 → 资源地址」映射里的一条。
- class LevelEntityResourceMapAsset
- class LevelEntitySpawner
- class LogicEntityMarker

**公开成员**
- `public static ILevelEntityCatalog Current { get; private set; }`
- `public static void Publish(ILevelEntityCatalog catalog)`
- `public static void Clear()`
- `public string ChunkName { get; set; }`
- `public List<LogicEntityPlacement> Placements { get; set; } = new List<LogicEntityPlacement>();`
- `public string LevelName { get; set; }`
- `public string EnvironmentName { get; set; }`
- `public List<string> ChunkNames { get; set; } = new List<string>();`
- `public IReadOnlyList<ILevelEntityView> Entities => _entities;`
- `public bool TryFind(string entityId, out ILevelEntityView entity)`
- `public IReadOnlyList<ILevelEntityView> FindByKind(string entityKind)`
- `public IReadOnlyList<string> EntityKinds => _entityKinds;`
- `public bool TryGetResourceAddress(string entityKind, out string resourceAddress)`
- `public string LevelDirectory => _levelDirectory;`
- `public IReadOnlyList<string> LoadedChunkNames => _loadedChunkNames;`
- `public int FileReadCount => _fileReadCount;`
- `public LevelDefinition LoadLevel()`
- `public LevelChunk LoadChunk(string chunkName)`
- `public bool UnloadChunk(string chunkName)`
- `public IReadOnlyList<string> Validate()`
- `public static string ToJson(LevelDefinition level) => JsonSerializer.Serialize(level, _options);`
- `public static LevelDefinition LevelFromJson(string json) => JsonSerializer.Deserialize<LevelDefinition>(json, _options);`
- `public static string ToJson(LevelChunk chunk) => JsonSerializer.Serialize(chunk, _options);`
- `public static LevelChunk ChunkFromJson(string json) => JsonSerializer.Deserialize<LevelChunk>(json, _options);`


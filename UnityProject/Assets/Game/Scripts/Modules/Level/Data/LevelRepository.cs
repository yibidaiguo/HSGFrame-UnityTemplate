using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Level.Data
{
    /// <summary>关卡仓库：关卡元信息与区块分开按需读盘，读过的留在内存里复用，可按块卸载。</summary>
    public sealed class LevelRepository
    {
        private const string LevelDefinitionFileName = "level.json";

        private readonly string _levelDirectory;
        private readonly Dictionary<string, LevelChunk> _chunksByName = new Dictionary<string, LevelChunk>();
        private readonly List<string> _loadedChunkNames = new List<string>();
        private LevelDefinition _levelDefinition;
        private int _fileReadCount;

        /// <summary>用一个关卡目录建仓库，构造阶段只记路径，读盘推迟到真正取数据时。</summary>
        /// <param name="levelDirectory">单个关卡的目录，里面放 level.json 与各区块 json。</param>
        public LevelRepository(string levelDirectory)
        {
            _levelDirectory = levelDirectory;
        }

        /// <summary>本仓库对应的关卡目录。</summary>
        public string LevelDirectory => _levelDirectory;

        /// <summary>已经加载在内存里的区块名，按首次加载的先后排列。</summary>
        public IReadOnlyList<string> LoadedChunkNames => _loadedChunkNames;

        /// <summary>累计读盘次数，用来观察重复取同一块时是否走了内存复用。</summary>
        public int FileReadCount => _fileReadCount;

        /// <summary>取关卡元信息，首次调用读盘，之后复用内存里的那一份。</summary>
        public LevelDefinition LoadLevel()
        {
            if (_levelDefinition != null)
            {
                return _levelDefinition;
            }

            if (!Directory.Exists(_levelDirectory))
            {
                throw new LevelDataException(ComposeError(
                    _levelDirectory,
                    "关卡目录不存在",
                    "在 Levels 下建一个关卡目录，并在里面放 level.json",
                    "Levels/Village"));
            }

            string levelPath = Path.Combine(_levelDirectory, LevelDefinitionFileName);
            if (!File.Exists(levelPath))
            {
                throw new LevelDataException(ComposeError(
                    levelPath,
                    "关卡文件不存在",
                    "在关卡目录下建一份 level.json",
                    "Levels/Village/level.json"));
            }

            string json = File.ReadAllText(levelPath);
            _fileReadCount++;

            try
            {
                _levelDefinition = LevelSerializer.LevelFromJson(json);
            }
            catch (JsonException exception)
            {
                throw new LevelDataException(
                    ComposeError(levelPath, "关卡 json 文本解析失败", "核对关卡 json 内容是否为合法 JSON", "Levels/Village/level.json"),
                    exception);
            }

            return _levelDefinition;
        }

        /// <summary>按需取一个区块，首次调用读盘，之后复用内存里的那一份。</summary>
        public LevelChunk LoadChunk(string chunkName)
        {
            if (_chunksByName.TryGetValue(chunkName, out LevelChunk cached))
            {
                return cached;
            }

            LevelDefinition level = LoadLevel();
            if (!level.ChunkNames.Contains(chunkName))
            {
                throw new LevelDataException(ComposeError(
                    chunkName,
                    "区块名未登记进关卡清单",
                    "把这个区块名加进 level.json 的区块清单，或改取一个已登记的区块",
                    "block-gate"));
            }

            string chunkPath = Path.Combine(_levelDirectory, chunkName + ".json");
            if (!File.Exists(chunkPath))
            {
                throw new LevelDataException(ComposeError(
                    chunkPath,
                    "区块文件不存在",
                    "在关卡目录下建一份同名的区块 json，或把这个区块名从 level.json 的区块清单里去掉",
                    "Levels/Village/block-gate.json"));
            }

            string json = File.ReadAllText(chunkPath);
            _fileReadCount++;

            LevelChunk chunk;
            try
            {
                chunk = LevelSerializer.ChunkFromJson(json);
            }
            catch (JsonException exception)
            {
                throw new LevelDataException(
                    ComposeError(chunkPath, "区块 json 文本解析失败", "核对区块 json 内容是否为合法 JSON", "Levels/Village/block-gate.json"),
                    exception);
            }

            _chunksByName[chunkName] = chunk;
            _loadedChunkNames.Add(chunkName);
            return chunk;
        }

        /// <summary>把一个区块从内存里摘掉，确实摘掉了返回 true，本来就不在内存里返回 false。</summary>
        public bool UnloadChunk(string chunkName)
        {
            if (!_chunksByName.Remove(chunkName))
            {
                return false;
            }

            _loadedChunkNames.Remove(chunkName);
            return true;
        }

        /// <summary>把关卡清单里登记的区块全部加载，返回区块名到区块的字典。</summary>
        public IReadOnlyDictionary<string, LevelChunk> LoadAllChunks()
        {
            LevelDefinition level = LoadLevel();
            foreach (string chunkName in level.ChunkNames)
            {
                LoadChunk(chunkName);
            }

            return new Dictionary<string, LevelChunk>(_chunksByName);
        }

        /// <summary>加载全部区块后跑一遍结构校验，返回中文问题清单，全通过时为空清单。</summary>
        public IReadOnlyList<string> Validate()
        {
            LevelDefinition level = LoadLevel();
            IReadOnlyDictionary<string, LevelChunk> chunksByName = LoadAllChunks();
            return LevelValidator.Validate(level, chunksByName);
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}

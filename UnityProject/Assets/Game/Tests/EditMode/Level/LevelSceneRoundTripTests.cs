using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Template.Logic.Data.Level;
using Template.Presentation.Level;
using Template.Toolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Presentation.Level.Tests.EditMode
{
    /// <summary>关卡场景构建与导出的往返测试：构建、分块启停、导出回 JSON、规范化文本比对。</summary>
    public class LevelSceneRoundTripTests
    {
        private const string LevelName = "村庄";
        private const string ScenePath = "Assets/Game/Scenes/World/临时_关卡往返测试.unity";

        private static string _templateRoot;
        private static string _sourceLevelDirectory;
        private static string _exportDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _templateRoot = TemplateRootLocator.Find();
            Assert.IsNotNull(_templateRoot, "找不到模板根：未定位到 Tools/Gates/Config/gate-config.json，关卡往返测试无法继续");

            _sourceLevelDirectory = Path.Combine(_templateRoot, "Levels", LevelName);
            LevelSceneBuilder.Build(_sourceLevelDirectory, ScenePath);

            var sourceLevel = new LevelRepository(_sourceLevelDirectory).LoadLevel();
            _exportDirectory = Path.Combine(Path.GetTempPath(), "关卡往返导出_" + Guid.NewGuid().ToString("N"));
            LevelSceneExporter.Export(ScenePath, _exportDirectory, sourceLevel.EnvironmentName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            if (!string.IsNullOrEmpty(_exportDirectory) && Directory.Exists(_exportDirectory))
            {
                Directory.Delete(_exportDirectory, recursive: true);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void BuildCreatesTwoChunkRootsUnderLevelRoot()
        {
            var levelRoot = GetLevelRoot();
            Assert.AreEqual(2, levelRoot.transform.childCount);
        }

        [Test]
        public void BuildCreatesTwentyFourMarkedEntities()
        {
            var levelRoot = GetLevelRoot();
            var markers = levelRoot.GetComponentsInChildren<LogicEntityMarker>(includeInactive: true);
            Assert.AreEqual(24, markers.Length);
        }

        [Test]
        public void VillageGuardCarriesExpectedTransformKindAndParameters()
        {
            var guard = FindEntity("区块_村口", "村口_守卫_01");
            Assert.IsNotNull(guard, "找不到 村口_守卫_01 实体物体");

            var position = guard.transform.localPosition;
            Assert.AreEqual(12.5f, position.x, 0.001f);
            Assert.AreEqual(0f, position.y, 0.001f);
            Assert.AreEqual(-3.25f, position.z, 0.001f);
            Assert.AreEqual(90f, guard.transform.localEulerAngles.y, 0.001f);

            var marker = guard.GetComponent<LogicEntityMarker>();
            Assert.IsNotNull(marker, "村口_守卫_01 缺 LogicEntityMarker 组件");
            Assert.AreEqual("NPC", marker.EntityKind);

            var parameters = marker.ToParameterDictionary();
            Assert.IsTrue(parameters.ContainsKey("阵营"), "村口_守卫_01 的参数缺 阵营");
            Assert.AreEqual("友方", parameters["阵营"]);
        }

        [Test]
        public void DisablingChunkDeactivatesItsEntitiesButKeepsOtherChunksActive()
        {
            var squareChunk = FindChunk("区块_广场");
            var villageChunk = FindChunk("区块_村口");
            Assert.IsNotNull(squareChunk, "找不到 区块_广场 区块根");
            Assert.IsNotNull(villageChunk, "找不到 区块_村口 区块根");

            squareChunk.gameObject.SetActive(false);

            Assert.IsFalse(squareChunk.gameObject.activeInHierarchy);
            foreach (Transform child in squareChunk)
            {
                Assert.IsFalse(child.gameObject.activeInHierarchy, $"区块_广场 的子物体 {child.name} 应当随父级失活");
            }

            Assert.IsTrue(villageChunk.gameObject.activeInHierarchy);
        }

        [Test]
        public void RoundTripKeepsLevelNameAndChunkOrder()
        {
            var sourceLevel = new LevelRepository(_sourceLevelDirectory).LoadLevel();
            var exportedLevel = new LevelRepository(_exportDirectory).LoadLevel();

            Assert.AreEqual(sourceLevel.LevelName, exportedLevel.LevelName);
            Assert.AreEqual(sourceLevel.ChunkNames, exportedLevel.ChunkNames);
        }

        [Test]
        public void RoundTripKeepsEntityCountIdsAndKinds()
        {
            var source = ReadAllPlacementsOrdered(_sourceLevelDirectory);
            var exported = ReadAllPlacementsOrdered(_exportDirectory);

            Assert.AreEqual(source.Count, exported.Count);
            for (var index = 0; index < source.Count; index++)
            {
                Assert.AreEqual(source[index].EntityId, exported[index].EntityId);
                Assert.AreEqual(source[index].EntityKind, exported[index].EntityKind);
            }
        }

        [Test]
        public void RoundTripKeepsPositionsAndRotationsWithinTolerance()
        {
            var source = ReadAllPlacementsOrdered(_sourceLevelDirectory);
            var exported = ReadAllPlacementsOrdered(_exportDirectory);

            Assert.AreEqual(source.Count, exported.Count);
            for (var index = 0; index < source.Count; index++)
            {
                Assert.AreEqual(source[index].Position.X, exported[index].Position.X, 0.001f);
                Assert.AreEqual(source[index].Position.Y, exported[index].Position.Y, 0.001f);
                Assert.AreEqual(source[index].Position.Z, exported[index].Position.Z, 0.001f);
                Assert.AreEqual(source[index].RotationAngle, exported[index].RotationAngle, 0.001f);
            }
        }

        [Test]
        public void RoundTripKeepsParameterDictionaries()
        {
            var source = ReadAllPlacementsOrdered(_sourceLevelDirectory);
            var exported = ReadAllPlacementsOrdered(_exportDirectory);

            Assert.AreEqual(source.Count, exported.Count);
            for (var index = 0; index < source.Count; index++)
            {
                CollectionAssert.AreEquivalent(source[index].Parameters.Keys, exported[index].Parameters.Keys);
                foreach (var key in source[index].Parameters.Keys)
                {
                    Assert.AreEqual(source[index].Parameters[key], exported[index].Parameters[key]);
                }
            }
        }

        [Test]
        public void NormalizedJsonMatchesCharacterByCharacter()
        {
            AssertNormalizedLevelJsonMatches("关卡.json");
            AssertNormalizedChunkJsonMatches("区块_村口.json");
            AssertNormalizedChunkJsonMatches("区块_广场.json");
        }

        private static GameObject GetLevelRoot()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var levelRoot = roots.FirstOrDefault(root => root.name == LevelName);
            Assert.IsNotNull(levelRoot, $"活动场景里找不到名为 {LevelName} 的关卡根物体");
            return levelRoot;
        }

        private static Transform FindChunk(string chunkName)
        {
            return GetLevelRoot().transform.Find(chunkName);
        }

        private static GameObject FindEntity(string chunkName, string entityId)
        {
            var chunk = FindChunk(chunkName);
            if (chunk == null)
            {
                return null;
            }

            var entity = chunk.Find(entityId);
            return entity == null ? null : entity.gameObject;
        }

        private static List<LogicEntityPlacement> ReadAllPlacementsOrdered(string levelDirectory)
        {
            var repository = new LevelRepository(levelDirectory);
            var chunks = repository.LoadAllChunks();
            return chunks.Values
                .SelectMany(chunk => chunk.Placements)
                .OrderBy(placement => placement.EntityId, StringComparer.Ordinal)
                .ToList();
        }

        private void AssertNormalizedLevelJsonMatches(string fileName)
        {
            var source = NormalizeLevel(File.ReadAllText(Path.Combine(_sourceLevelDirectory, fileName)));
            var exported = NormalizeLevel(File.ReadAllText(Path.Combine(_exportDirectory, fileName)));
            Assert.AreEqual(source, exported, $"关卡元信息文件 {fileName} 往返后文本不一致");
        }

        private void AssertNormalizedChunkJsonMatches(string fileName)
        {
            var source = NormalizeChunk(File.ReadAllText(Path.Combine(_sourceLevelDirectory, fileName)));
            var exported = NormalizeChunk(File.ReadAllText(Path.Combine(_exportDirectory, fileName)));
            Assert.AreEqual(source, exported, $"区块文件 {fileName} 往返后文本不一致");
        }

        private static string NormalizeLevel(string json) => LevelSerializer.ToJson(LevelSerializer.LevelFromJson(json));

        private static string NormalizeChunk(string json) => LevelSerializer.ToJson(LevelSerializer.ChunkFromJson(json));
    }
}

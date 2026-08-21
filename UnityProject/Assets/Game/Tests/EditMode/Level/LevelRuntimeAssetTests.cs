using System.Collections.Generic;
using NUnit.Framework;
using Template.Level.Contracts;
using Template.Level.View;
using UnityEditor;
using UnityEngine;

namespace Template.Level.Tests.EditMode
{
    /// <summary>
    /// 关卡运行时资产的 EditMode 测试：映射资产、实体预制体、以及标记组件的只读视图。
    /// </summary>
    /// <remarks>
    /// 这一层原先完全没有自动化覆盖（账本 Q3）：秒级门禁看不见 Game.View，
    /// 于是「类别接不上资源」「预制体还是空壳」这类错只能靠人按 Play 才发现。
    /// 本组用例挑的都是不依赖播放模式的部分，跑在分钟级门禁的 EditMode 一档里。
    /// </remarks>
    public class LevelRuntimeAssetTests
    {
        private const string ResourceMapAssetPath = "Assets/Game/Settings/Level/EntityResourceMap.asset";
        private const string EntityPrefabDirectory = "Assets/Game/ResourceArt/Level";

        // 六个类别取自 Levels/Village/block-*.json 里「类别」字段的实际取值。
        private static readonly string[] ExpectedEntityKinds =
        {
            "NPC", "可交互物", "传送点", "刷怪点", "触发器", "任务物件",
        };

        [Test]
        public void ResourceMapCoversAllSixEntityKinds()
        {
            var asset = LoadResourceMapAsset();
            var map = asset.ToResourceMap();

            foreach (var entityKind in ExpectedEntityKinds)
            {
                Assert.IsTrue(
                    map.TryGetResourceAddress(entityKind, out _),
                    $"类别「{entityKind}」在 {ResourceMapAssetPath} 里没有登记资源地址");
            }
        }

        [Test]
        public void UnknownEntityKindResolvesToNothing()
        {
            var map = LoadResourceMapAsset().ToResourceMap();

            Assert.IsFalse(map.TryGetResourceAddress("这个类别不存在", out var address));
            Assert.IsNull(address);
        }

        [Test]
        public void EveryResourceAddressPointsAtARealPrefab()
        {
            var map = LoadResourceMapAsset().ToResourceMap();

            foreach (var entityKind in ExpectedEntityKinds)
            {
                Assert.IsTrue(map.TryGetResourceAddress(entityKind, out var address));

                // 收集器用的是 AddressByFileName，所以地址就是预制体文件名去掉扩展名。
                // 这一条把「映射填了地址」与「地址真有东西」两件事钉在一起——
                // 只填映射不建预制体，运行时表现是静默的空物体，光看配置看不出来。
                var prefabPath = $"{EntityPrefabDirectory}/{address}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefab, $"类别「{entityKind}」的地址 {address} 对不上任何预制体：{prefabPath}");
            }
        }

        [Test]
        public void EveryEntityPrefabCarriesAVisualBody()
        {
            var map = LoadResourceMapAsset().ToResourceMap();

            foreach (var entityKind in ExpectedEntityKinds)
            {
                Assert.IsTrue(map.TryGetResourceAddress(entityKind, out var address));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityPrefabDirectory}/{address}.prefab");
                Assert.IsNotNull(prefab);

                // 账本 L3 记的就是这一条：六个预制体全是空壳，没有 MeshRenderer，
                // 于是热更链路验的是空气，编辑器里也什么都看不见。
                var renderer = prefab.GetComponentInChildren<MeshRenderer>(includeInactive: true);
                Assert.IsNotNull(renderer, $"预制体 {address} 没有 MeshRenderer，仍是空壳");
                Assert.IsNotNull(renderer.sharedMaterial, $"预制体 {address} 的 MeshRenderer 没有材质");
            }
        }

        [Test]
        public void MarkerExposesIdKindPositionAndParametersThroughTheView()
        {
            var host = new GameObject("测试实体");
            try
            {
                host.transform.position = new Vector3(1f, 2f, 3f);
                var marker = host.AddComponent<LogicEntityMarker>();
                marker.EntityId = "村长";
                marker.EntityKind = "NPC";
                marker.SetParameters(new Dictionary<string, string> { { "对话", "开场白" } });

                ILevelEntityView view = marker;

                Assert.AreEqual("村长", view.EntityId);
                Assert.AreEqual("NPC", view.EntityKind);
                Assert.AreEqual(1f, view.Position.x, 0.0001f);
                Assert.AreEqual(3f, view.Position.z, 0.0001f);
                Assert.IsTrue(view.TryGetParameter("对话", out var value));
                Assert.AreEqual("开场白", value);
                Assert.IsFalse(view.TryGetParameter("不存在的键", out var missing));
                Assert.IsNull(missing);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CatalogRegistryStartsEmptyAndReturnsWhatWasPublished()
        {
            LevelEntityCatalogRegistry.Clear();
            Assert.IsNull(LevelEntityCatalogRegistry.Current);

            var catalog = new Template.Level.Data.LevelEntityCatalog(new ILevelEntityView[0]);
            LevelEntityCatalogRegistry.Publish(catalog);
            Assert.AreSame(catalog, LevelEntityCatalogRegistry.Current);

            LevelEntityCatalogRegistry.Clear();
            Assert.IsNull(LevelEntityCatalogRegistry.Current);
        }

        private static LevelEntityResourceMapAsset LoadResourceMapAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelEntityResourceMapAsset>(ResourceMapAssetPath);
            Assert.IsNotNull(
                asset,
                $"位置：{ResourceMapAssetPath}；原因：实体资源映射资产不存在；修复：跑 runtime.scaffold 命令生成运行时资产；参考：Tools/Cli/toolkit-cmd.ps1");
            return asset;
        }
    }
}

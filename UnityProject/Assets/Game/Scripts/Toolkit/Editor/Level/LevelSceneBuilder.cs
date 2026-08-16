using System;
using System.IO;
using Template.Level.Data;
using Template.Level.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Toolkit.Editor
{
    /// <summary>把关卡 JSON 构建成 Unity 场景：一区块一根物体，一实体一物体。</summary>
    public static class LevelSceneBuilder
    {
        /// <summary>构建一个关卡的场景并保存到指定路径，返回一行中文摘要。</summary>
        /// <param name="levelDirectory">关卡目录，里面放 关卡.json 与各区块 json。</param>
        /// <param name="scenePath">场景保存路径，相对 Unity 工程根写，例 Assets/Game/Scenes/World/村庄.unity。</param>
        public static string Build(string levelDirectory, string scenePath)
        {
            var repository = new LevelRepository(levelDirectory);

            var validationErrors = repository.Validate();
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException($"关卡校验未通过：{string.Join("；", validationErrors)}");
            }

            var level = repository.LoadLevel();
            // 走 Single 而不是 Additive：Additive 在编辑器里开着一个未保存的未命名场景时会直接抛
            // 「Cannot create a new scene additively with an untitled scene unsaved」，
            // 而构建命令要能在任意编辑器状态下跑。导出那一侧也是 OpenSceneMode.Single，两边一致。
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 关卡根与区块根都放在原点，实体只设局部坐标，于是局部坐标与 JSON 里的位置一一对应。
            var levelRoot = new GameObject(level.LevelName);

            // new GameObject 落在活动场景里，而「新建的场景就是活动场景」是 Single 模式才成立的前提。
            // 显式挪一次，构建结果就与模式无关，不会悄悄存出一个空场景。
            SceneManager.MoveGameObjectToScene(levelRoot, scene);

            var entityCount = 0;
            foreach (var chunkName in level.ChunkNames)
            {
                var chunk = repository.LoadChunk(chunkName);
                var chunkRoot = new GameObject(chunk.ChunkName);
                chunkRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

                foreach (var placement in chunk.Placements)
                {
                    var entity = new GameObject(placement.EntityId);
                    entity.transform.SetParent(chunkRoot.transform, worldPositionStays: false);
                    entity.transform.localPosition = new Vector3(placement.Position.X, placement.Position.Y, placement.Position.Z);
                    entity.transform.localEulerAngles = new Vector3(0f, placement.RotationAngle, 0f);

                    var marker = entity.AddComponent<LogicEntityMarker>();
                    marker.EntityId = placement.EntityId;
                    marker.EntityKind = placement.EntityKind;
                    marker.SetParameters(placement.Parameters);

                    entityCount++;
                }
            }

            // scenePath 相对工程根写，SaveScene 也按工程相对路径收；这里只负责在落盘前把父目录备好。
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var parentDirectory = Path.GetDirectoryName(Path.Combine(projectRoot, scenePath));
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException($"场景保存失败：{scenePath}");
            }

            AssetDatabase.Refresh();

            return $"关卡「{level.LevelName}」已构建成场景 {scenePath}：区块 {level.ChunkNames.Count} 块，实体 {entityCount} 个";
        }
    }
}

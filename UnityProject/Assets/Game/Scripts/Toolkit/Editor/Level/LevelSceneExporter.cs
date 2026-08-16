using System;
using System.IO;
using System.Text;
using Template.Level.Data;
using Template.Level.View;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Toolkit.Editor
{
    /// <summary>把 Unity 场景导出回关卡 JSON，与 LevelSceneBuilder 互为逆向。</summary>
    public static class LevelSceneExporter
    {
        /// <summary>导出一个场景到指定目录，产出 关卡.json 与各区块 json，返回一行中文摘要。</summary>
        /// <param name="scenePath">场景路径，相对 Unity 工程根写。</param>
        /// <param name="outputDirectory">导出目录，绝对路径。</param>
        /// <param name="environmentName">写进 关卡.json 的环境名。</param>
        public static string Export(string scenePath, string outputDirectory, string environmentName)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var levelRootObjects = scene.GetRootGameObjects();
            if (levelRootObjects.Length != 1)
            {
                // 报错按四要素写，格式与命令层、LevelRepository 保持一致。
                throw new InvalidOperationException(
                    $"位置：{scenePath}；原因：场景根物体数量是 {levelRootObjects.Length}，导出要求恰好 1 个关卡根物体；" +
                    "修复：把场景整理成「一个关卡根物体，区块挂在它下面」的结构；" +
                    "参考：Assets/Game/Scenes/World/村庄.unity");
            }

            var levelRoot = levelRootObjects[0];
            var level = new LevelDefinition
            {
                LevelName = levelRoot.name,
                EnvironmentName = environmentName,
            };

            Directory.CreateDirectory(outputDirectory);

            var entityCount = 0;
            var skippedCount = 0;

            foreach (Transform chunkTransform in levelRoot.transform)
            {
                var chunk = new LevelChunk { ChunkName = chunkTransform.name };
                level.ChunkNames.Add(chunk.ChunkName);

                foreach (Transform entityTransform in chunkTransform)
                {
                    var marker = entityTransform.GetComponent<LogicEntityMarker>();
                    if (marker == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var position = entityTransform.localPosition;
                    chunk.Placements.Add(new LogicEntityPlacement
                    {
                        EntityId = marker.EntityId,
                        EntityKind = marker.EntityKind,
                        Position = new LevelVector3(position.x, position.y, position.z),
                        // Transform 存的是四元数，欧拉角是换算回来的，末位带浮点噪声：写进去 45 度，
                        // 取回来是 45.0000038。收到三位小数，往返才谈得上等价——关卡角度的编辑精度
                        // 到千分之一度早已够用。另外 localEulerAngles 取回的值落在 0 到 360 之间，
                        // 源 JSON 里写成负数或超过 360 的角度会被归一化到这个区间。
                        RotationAngle = (float)Math.Round(entityTransform.localEulerAngles.y, 3),
                        Parameters = marker.ToParameterDictionary(),
                    });

                    entityCount++;
                }

                var chunkJson = LevelSerializer.ToJson(chunk);
                File.WriteAllText(Path.Combine(outputDirectory, chunk.ChunkName + ".json"), chunkJson, new UTF8Encoding(false));
            }

            var levelJson = LevelSerializer.ToJson(level);
            File.WriteAllText(Path.Combine(outputDirectory, "关卡.json"), levelJson, new UTF8Encoding(false));

            return $"场景 {scenePath} 已导出到 {outputDirectory}：区块 {level.ChunkNames.Count} 块，实体 {entityCount} 个，跳过 {skippedCount} 个";
        }
    }
}

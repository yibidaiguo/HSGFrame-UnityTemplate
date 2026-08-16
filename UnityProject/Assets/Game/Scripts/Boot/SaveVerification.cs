using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HSGFrame.Save;
using UnityEngine;

namespace Template.Boot
{
    /// <summary>
    /// 存档在 Unity 运行时的序列化验收：真写盘、真读回、真跑迁移链。
    /// 纯 .NET 侧的测试跑的是桌面 CoreCLR，而真包跑的是 IL2CPP——
    /// System.Text.Json 在 IL2CPP 下走的是反射解析器，泛型与反射一被裁剪就在运行时才炸，
    /// 所以这一条必须在真包里跑一遍才算数。
    /// </summary>
    public static class SaveVerification
    {
        private const string VerificationDirectoryName = "存档验收";
        private const string VerificationFileName = "存档.json";
        private const int CurrentVersion = 3;

        // 故意全用中文键与中文值：序列化选项里那个 UnsafeRelaxedJsonEscaping 一旦失效，
        // 中文会被转义成 \uXXXX，往返仍然相等，光比对象是看不出来的——要比落盘的原文。
        private const string SectionName = "背包";
        private const string SectionPayload = "{\"格子数\":24,\"备注\":\"第一格放着「青铜剑」\"}";

        /// <summary>跑一次存档往返与迁移链，返回一行中文结论。</summary>
        public static string ProbeRoundTrip()
        {
            var directory = Path.Combine(Application.persistentDataPath, VerificationDirectoryName);
            var filePath = Path.Combine(directory, VerificationFileName);

            try
            {
                Directory.CreateDirectory(directory);

                var original = new SaveDocument { Version = 1 };
                original.Sections[SectionName] = SectionPayload;

                var json = SaveSerializer.ToJson(original);
                File.WriteAllText(filePath, json, new UTF8Encoding(false));

                var readBack = File.ReadAllText(filePath, Encoding.UTF8);
                if (!string.Equals(readBack, json, StringComparison.Ordinal))
                {
                    return "未通过：落盘再读回的 JSON 原文与序列化结果不相等";
                }

                if (readBack.IndexOf(SectionName, StringComparison.Ordinal) < 0)
                {
                    return $"未通过：落盘的原文里找不到中文键「{SectionName}」，中文被转义了";
                }

                var restored = SaveSerializer.FromJson(readBack);
                if (restored == null)
                {
                    return "未通过：反序列化返回 null";
                }

                if (restored.Version != original.Version)
                {
                    return $"未通过：版本号往返后从 {original.Version} 变成 {restored.Version}";
                }

                if (!restored.Sections.TryGetValue(SectionName, out var restoredPayload)
                    || !string.Equals(restoredPayload, SectionPayload, StringComparison.Ordinal))
                {
                    return $"未通过：数据域「{SectionName}」往返后内容不相等";
                }

                return ProbeMigration(restored);
            }
            catch (Exception exception)
            {
                return $"未通过：存档验收抛了 {exception.GetType().Name}：{exception.Message}";
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        // 迁移链是存档这一块最容易在 IL2CPP 下出事的部分：它靠接口多态逐级驱动，
        // 实现类只被反射式地装进列表，裁剪器很容易判定它们没人用。
        private static string ProbeMigration(SaveDocument document)
        {
            var migrator = new SaveMigrator(
                CurrentVersion,
                new List<ISaveMigration> { new AddQuestSection(), new RenameBagKey() });

            var result = migrator.Migrate(document);
            if (!result.IsSuccess)
            {
                return $"未通过：迁移链失败，{result.Message}";
            }

            if (document.Version != CurrentVersion)
            {
                return $"未通过：迁移完版本号是 {document.Version}，期望 {CurrentVersion}";
            }

            if (!document.Sections.ContainsKey("任务"))
            {
                return "未通过：1→2 那一步该补出的「任务」数据域不在";
            }

            return $"通过：中文键值落盘往返逐字相等，迁移链 1→{CurrentVersion} 走了 {result.AppliedSteps.Count} 步";
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
                // 验收产物删不掉不影响结论，留着就留着。
            }
        }

        /// <summary>验收用的迁移第一步：补出「任务」数据域。</summary>
        private sealed class AddQuestSection : ISaveMigration
        {
            /// <summary>迁移起始版本号。</summary>
            public int FromVersion => 1;

            /// <summary>迁移目标版本号。</summary>
            public int ToVersion => 2;

            /// <summary>补出「任务」数据域。</summary>
            public void Apply(SaveDocument document)
            {
                document.Sections["任务"] = "{\"已完成\":[]}";
            }
        }

        /// <summary>验收用的迁移第二步：给背包域补一个字段，验证多步链条真的逐级走。</summary>
        private sealed class RenameBagKey : ISaveMigration
        {
            /// <summary>迁移起始版本号。</summary>
            public int FromVersion => 2;

            /// <summary>迁移目标版本号。</summary>
            public int ToVersion => 3;

            /// <summary>把背包域替换成带扩容字段的新形状。</summary>
            public void Apply(SaveDocument document)
            {
                document.Sections[SectionName] = "{\"格子数\":32,\"备注\":\"第一格放着「青铜剑」\"}";
            }
        }
    }
}

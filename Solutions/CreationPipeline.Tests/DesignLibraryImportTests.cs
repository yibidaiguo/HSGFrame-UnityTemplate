using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 效果图入库：人点拆图 = 选中这张原稿，顺手把设计库填上。
    ///
    /// 钉的重点是**原稿与重绘产物不能混**——拿重绘产物当风格锚点会世代退化：
    /// 模型参考自己的输出，下一轮再参考「参考自己输出的输出」，
    /// 几轮之后离原稿越来越远，而每一步看着都合理。
    /// </summary>
    public sealed class DesignLibraryImportTests
    {
        /// <summary>效果图收进模块的 refs/，并顺带带出定稿 v1。</summary>
        [Fact]
        public void ConfirmedImageIsImportedAndSeedsTheFirstFinal()
        {
            using var workspace = new Workspace();
            var source = MakeImage(workspace, "source.png");

            var result = DesignLibraryImporter.Import(workspace.Root, "Inventory", source, "ASSET-0000-01");

            Assert.True(result.Imported);
            Assert.True(File.Exists(result.ReferencePath));
            Assert.Contains("Inventory", result.ReferencePath);
            Assert.True(result.FinalCreated);

            Assert.True(ArtStyleFinal.TryRead(
                ArtStyleFinal.ModuleFilePath(workspace.Root, "Inventory"), "Inventory", out var final, out _));
            Assert.Equal(ArtStyleFinal.OriginSelection, final.Origin);
            Assert.NotEmpty(final.Palette);
        }

        /// <summary>
        /// 带出的定稿来源是「选片带出」，过得了「无假定稿」那道门禁。
        /// 这不算机器编——图是人挑的，主色是从那张图上算的，没有一处是现编的形容词。
        /// </summary>
        [Fact]
        public void SeededFinalPassesTheOriginGate()
        {
            using var workspace = new Workspace();
            DesignLibraryImporter.Import(workspace.Root, "Inventory", MakeImage(workspace, "s.png"), "ASSET-0000-01");

            ArtStyleFinal.TryRead(
                ArtStyleFinal.ModuleFilePath(workspace.Root, "Inventory"), "Inventory", out var final, out _);

            Assert.Empty(ArtStyleFinal.InspectOrigin(final));
        }

        /// <summary>
        /// 已经有定稿时一个字都不动：改风格是升版，要列出受影响的存量资产，
        /// 不能靠「又点了一次拆图」悄悄改掉。
        /// </summary>
        [Fact]
        public void ExistingFinalIsNeverOverwritten()
        {
            using var workspace = new Workspace();
            var finalPath = ArtStyleFinal.ModuleFilePath(workspace.Root, "Inventory");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            File.WriteAllText(finalPath, "{\"名称\":\"手写的\",\"来源\":\"人定\",\"版本\":3}", new UTF8Encoding(false));

            var result = DesignLibraryImporter.Import(
                workspace.Root, "Inventory", MakeImage(workspace, "s.png"), "ASSET-0000-02");

            Assert.True(result.Imported);
            Assert.False(result.FinalCreated);
            Assert.Contains("手写的", File.ReadAllText(finalPath));
        }

        /// <summary>
        /// 没有模块名时不收。**不许往「无模块」里堆**——
        /// 那等于把所有界面的原稿倒进一个筐，往后查同类全是不相干的东西。
        /// </summary>
        [Fact]
        public void ImageWithoutModuleIsNotImported()
        {
            using var workspace = new Workspace();

            var result = DesignLibraryImporter.Import(
                workspace.Root, "", MakeImage(workspace, "s.png"), "ASSET-0000-01");

            Assert.False(result.Imported);
            Assert.Contains(result.Notes, note => note.Contains("无模块"));
        }

        /// <summary>源图不在了就如实说，不静默跳过。</summary>
        [Fact]
        public void MissingSourceIsReported()
        {
            using var workspace = new Workspace();

            var result = DesignLibraryImporter.Import(
                workspace.Root, "Inventory", Path.Combine(workspace.Root, "没有这张.png"), "ASSET-0000-01");

            Assert.False(result.Imported);
            Assert.Contains(result.Notes, note => note.Contains("不在了"));
        }

        /// <summary>索引把效果图与元素图分开记，产出方式各是各的。</summary>
        [Fact]
        public void IndexDistinguishesReferenceFromElement()
        {
            using var workspace = new Workspace();
            DesignLibraryImporter.Import(workspace.Root, "Inventory", MakeImage(workspace, "s.png"), "ASSET-0000-01");
            WriteElement(workspace, "Inventory/T_SlotItem.png");

            var index = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false);

            Assert.Contains(index.Entries, entry => entry.Origin == DesignLibraryIndex.OriginReference);
            Assert.Contains(index.Entries, entry => entry.Origin == DesignLibraryIndex.OriginElement);
        }

        /// <summary>
        /// 查同类时**效果图排在前面**——那是人确认过的原稿。
        /// 只取 1 张时取到的必须是它，不能是重绘产物。
        /// </summary>
        [Fact]
        public void SimilarSearchPrefersTheConfirmedReference()
        {
            using var workspace = new Workspace();
            WriteElement(workspace, "Inventory/T_SlotItem.png");
            DesignLibraryImporter.Import(workspace.Root, "Inventory", MakeImage(workspace, "s.png"), "ASSET-0000-01");

            var index = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false);
            var found = index.FindSimilar("Inventory", "", 1);

            Assert.Single(found);
            Assert.Equal(DesignLibraryIndex.OriginReference, found[0].Origin);
        }

        /// <summary>重建两遍逐字节一样——加了新字段之后幂等仍要成立。</summary>
        [Fact]
        public void RebuildStaysDeterministicWithBothKinds()
        {
            using var workspace = new Workspace();
            DesignLibraryImporter.Import(workspace.Root, "Inventory", MakeImage(workspace, "s.png"), "ASSET-0000-01");
            WriteElement(workspace, "Inventory/T_SlotItem.png");

            Assert.Equal(
                DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false).Render(),
                DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false).Render());
        }

        /// <summary>造一张 2×2 的真图：主色聚类要有像素可算。</summary>
        private static string MakeImage(Workspace workspace, string fileName)
        {
            var pixels = new byte[2 * 2 * 4];
            for (var index = 0; index < 4; index++)
            {
                pixels[index * 4] = (byte)(index * 60);
                pixels[(index * 4) + 1] = 80;
                pixels[(index * 4) + 2] = 160;
                pixels[(index * 4) + 3] = 255;
            }

            var path = Path.Combine(workspace.Root, fileName);
            Directory.CreateDirectory(workspace.Root);
            Assert.True(PngEncoder.EncodeToFile(new PngImage(2, 2, pixels), path, out var reason), reason);
            return path;
        }

        /// <summary>在资产落点造一张元素图。</summary>
        private static void WriteElement(Workspace workspace, string relativePath)
        {
            var path = Path.Combine(
                DesignLibraryIndex.ScanRoot(workspace.Root), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "效果图入库测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}

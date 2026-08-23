using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 设计库视图：策划一份、美术一份，两份分开。
    ///
    /// 钉的重点是**空的时候要如实显示为空**——
    /// 拿别处的东西把它填满，只会让人以为这一层已经在用了。
    /// </summary>
    public sealed class DesignLibraryViewTests
    {
        /// <summary>两份都要写死「这是生成的视图」——免得有人在飞书上直接编辑然后奇怪为什么下次全没了。</summary>
        [Fact]
        public void BothViewsSayTheyAreGenerated()
        {
            using var workspace = new Workspace();

            Assert.Contains("生成的视图", DesignLibraryView.RenderGame(workspace.Root));
            Assert.Contains("生成的视图", DesignLibraryView.RenderArt(workspace.Root, Empty()));
        }

        /// <summary>什么都没有时如实说没有，不编内容填满。</summary>
        [Fact]
        public void EmptyLibraryRendersAsEmpty()
        {
            using var workspace = new Workspace();

            Assert.Contains("还没有定", DesignLibraryView.RenderGame(workspace.Root));
            Assert.Contains("还没有任何资产", DesignLibraryView.RenderArt(workspace.Root, Empty()));
        }

        /// <summary>有总设计层时，策划库把它原样摆出来。</summary>
        [Fact]
        public void GameViewCarriesTheDirection()
        {
            using var workspace = new Workspace();
            var path = DesignDirection.FilePathFor(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "低饱和冷色系，扁平。", new UTF8Encoding(false));

            Assert.Contains("低饱和冷色系", DesignLibraryView.RenderGame(workspace.Root));
        }

        /// <summary>美术库按模块分组，并把产出方式（效果图 / 元素图）摆出来。</summary>
        [Fact]
        public void ArtViewGroupsByModuleAndShowsOrigin()
        {
            using var workspace = new Workspace();
            var index = new DesignLibraryIndex(new[]
            {
                new DesignLibraryEntry("ASSET-0000-01", "PC界面底图", "Inventory", "Pools/Designs/Art/Inventory/refs/a.png",
                    new[] { "#2b3a4a" }, "", DesignLibraryIndex.OriginReference),
                new DesignLibraryEntry("T_SlotItem", "UI元素", "Inventory", "Assets/Game/Art/Texture/Ui/Inventory/T_SlotItem.png",
                    Array.Empty<string>(), "", DesignLibraryIndex.OriginElement)
            });

            var text = DesignLibraryView.RenderArt(workspace.Root, index);

            Assert.Contains("### Inventory", text);
            Assert.Contains(DesignLibraryIndex.OriginReference, text);
            Assert.Contains(DesignLibraryIndex.OriginElement, text);
        }

        /// <summary>有定稿时把色板与负面清单摆出来。</summary>
        [Fact]
        public void ArtViewShowsFinalPaletteAndNegativeList()
        {
            using var workspace = new Workspace();
            var path = ArtStyleFinal.ProjectFilePath(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "{\"名称\":\"项目风格@v1\",\"版本\":1,\"来源\":\"人定\","
                    + "\"色板\":[\"#2b3a4a\"],\"负面清单\":[\"不要赛博朋克霓虹\"]}",
                new UTF8Encoding(false));

            var text = DesignLibraryView.RenderArt(workspace.Root, Empty());

            Assert.Contains("#2b3a4a", text);
            Assert.Contains("不要赛博朋克霓虹", text);
        }

        /// <summary>两遍渲逐字节一样——否则「视图过期了」那道判据永远红。</summary>
        [Fact]
        public void RenderIsDeterministic()
        {
            using var workspace = new Workspace();
            var index = new DesignLibraryIndex(new[]
            {
                new DesignLibraryEntry("B", "UI元素", "Shop", "b.png", Array.Empty<string>(), "", DesignLibraryIndex.OriginElement),
                new DesignLibraryEntry("A", "UI元素", "Inventory", "a.png", Array.Empty<string>(), "", DesignLibraryIndex.OriginElement)
            });

            Assert.Equal(
                DesignLibraryView.RenderArt(workspace.Root, index),
                DesignLibraryView.RenderArt(workspace.Root, index));
        }

        /// <summary>写两遍之后第二遍是「无变化」——不动文件才谈得上幂等。</summary>
        [Fact]
        public void SecondWriteReportsNoChange()
        {
            using var workspace = new Workspace();
            var notes = new List<string>();

            Assert.True(DesignLibraryView.Write(workspace.Root, Empty(), notes));
            notes.Clear();
            Assert.True(DesignLibraryView.Write(workspace.Root, Empty(), notes));

            Assert.All(notes, note => Assert.Contains("无变化", note));
        }

        /// <summary>空索引。</summary>
        private static DesignLibraryIndex Empty()
        {
            return new DesignLibraryIndex(Array.Empty<DesignLibraryEntry>());
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "设计库视图测试-" + Guid.NewGuid().ToString("N"));
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

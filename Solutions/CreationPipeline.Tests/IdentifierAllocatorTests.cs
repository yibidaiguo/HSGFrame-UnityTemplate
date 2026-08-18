using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>IdentifierAllocator 取号行为的测试：目录缺失、空目录、取最大值与无关文件名。</summary>
    public class IdentifierAllocatorTests
    {
        /// <summary>目录不存在时返回前缀加最小编号 REQ-0001。</summary>
        [Fact]
        public void NextReturnsFirstIdWhenDirectoryMissing()
        {
            using var workspace = new PoolTestWorkspace();
            var missing = System.IO.Path.Combine(workspace.Root, "不存在");

            var next = IdentifierAllocator.Next(missing, "REQ-", 4);

            Assert.Equal("REQ-0001", next);
        }

        /// <summary>目录为空时返回 REQ-0001。</summary>
        [Fact]
        public void NextReturnsFirstIdWhenDirectoryEmpty()
        {
            using var workspace = new PoolTestWorkspace();

            var next = IdentifierAllocator.Next(PoolPaths.RequirementsDirectory(workspace.Root), "REQ-", 4);

            Assert.Equal("REQ-0001", next);
        }

        /// <summary>有 REQ-0007 时返回 REQ-0008；补进 REQ-0002 与 REQ-0011 后返回 REQ-0012（取最大值而非取个数）。</summary>
        [Fact]
        public void NextReturnsMaxNumberPlusOne()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = PoolPaths.RequirementsDirectory(workspace.Root);
            workspace.WriteRequirement("REQ-0007.json", "{}");

            Assert.Equal("REQ-0008", IdentifierAllocator.Next(directory, "REQ-", 4));

            workspace.WriteRequirement("REQ-0002.json", "{}");
            workspace.WriteRequirement("REQ-0011.json", "{}");

            Assert.Equal("REQ-0012", IdentifierAllocator.Next(directory, "REQ-", 4));
        }

        /// <summary>混入 README.md、REQ-abc.json、OTHER-0099.json 这类不匹配文件不影响取号结果。</summary>
        [Fact]
        public void NextIgnoresUnrelatedFileNames()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = PoolPaths.RequirementsDirectory(workspace.Root);
            workspace.WriteRequirement("README.md", "说明");
            workspace.WriteRequirement("REQ-abc.json", "{}");
            workspace.WriteRequirement("OTHER-0099.json", "{}");
            workspace.WriteRequirement("REQ-0003.json", "{}");

            var next = IdentifierAllocator.Next(directory, "REQ-", 4);

            Assert.Equal("REQ-0004", next);
        }
    }
}

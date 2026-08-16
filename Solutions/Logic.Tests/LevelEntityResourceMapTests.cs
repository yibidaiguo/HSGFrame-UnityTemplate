using System;
using System.Collections.Generic;
using Template.Level.Data;
using Xunit;

namespace Template.Tests
{
    /// <summary>实体类别到资源地址映射的查表与配置校验测试。</summary>
    public class LevelEntityResourceMapTests
    {
        [Fact]
        public void RegisteredKindResolvesToItsAddress()
        {
            var map = new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", "P_Npc"),
                new KeyValuePair<string, string>("传送点", "P_传送点"),
            });

            Assert.True(map.TryGetResourceAddress("NPC", out var address));
            Assert.Equal("P_Npc", address);
        }

        [Fact]
        public void UnknownKindResolvesToNothing()
        {
            var map = new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", "P_Npc"),
            });

            Assert.False(map.TryGetResourceAddress("没登记过的类别", out var address));
            Assert.Null(address);
        }

        // 关卡 JSON 里类别是自由字符串，空值与 null 完全可能出现；
        // 这两种输入必须返回「查不到」而不是抛异常，否则一条脏数据就能把整个关卡装配打断。
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void BlankKindResolvesToNothingInsteadOfThrowing(string entityKind)
        {
            var map = new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", "P_Npc"),
            });

            Assert.False(map.TryGetResourceAddress(entityKind, out var address));
            Assert.Null(address);
        }

        [Fact]
        public void KindLookupIsCaseSensitive()
        {
            var map = new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", "P_Npc"),
            });

            Assert.False(map.TryGetResourceAddress("npc", out _));
        }

        [Fact]
        public void EntityKindsAreSortedByOrdinal()
        {
            var map = new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("触发器", "P_触发器"),
                new KeyValuePair<string, string>("NPC", "P_Npc"),
                new KeyValuePair<string, string>("传送点", "P_传送点"),
            });

            Assert.Equal(new[] { "NPC", "传送点", "触发器" }, map.EntityKinds);
        }

        [Fact]
        public void EmptyMapResolvesNothing()
        {
            var map = new LevelEntityResourceMap(Array.Empty<KeyValuePair<string, string>>());

            Assert.Empty(map.EntityKinds);
            Assert.False(map.TryGetResourceAddress("NPC", out _));
        }

        [Fact]
        public void NullEntriesBuildAnEmptyMap()
        {
            var map = new LevelEntityResourceMap(null);

            Assert.Empty(map.EntityKinds);
        }

        [Fact]
        public void DuplicateKindIsRejectedInsteadOfSilentlyOverwriting()
        {
            var exception = Assert.Throws<ArgumentException>(() => new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", "P_Npc"),
                new KeyValuePair<string, string>("NPC", "P_另一个"),
            }));

            Assert.Contains("登记了两次", exception.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankKindInConfigurationIsRejected(string entityKind)
        {
            Assert.Throws<ArgumentException>(() => new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>(entityKind, "P_Npc"),
            }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankAddressInConfigurationIsRejected(string resourceAddress)
        {
            var exception = Assert.Throws<ArgumentException>(() => new LevelEntityResourceMap(new[]
            {
                new KeyValuePair<string, string>("NPC", resourceAddress),
            }));

            Assert.Contains("资源地址为空白", exception.Message);
        }
    }
}

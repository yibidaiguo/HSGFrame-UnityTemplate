using System.Collections.Generic;
using Template.Level.Contracts;
using Template.Level.Data;
using Unity.Mathematics;
using Xunit;

namespace Template.Tests
{
    /// <summary>关卡实体名录的检索测试：按编号、按类别、以及脏数据下的确定行为。</summary>
    public class LevelEntityCatalogTests
    {
        [Fact]
        public void FindsEntityById()
        {
            var catalog = new LevelEntityCatalog(new ILevelEntityView[]
            {
                new FakeEntity("村长", "NPC"),
                new FakeEntity("木箱", "可交互物"),
            });

            Assert.True(catalog.TryFind("木箱", out var entity));
            Assert.Equal("可交互物", entity.EntityKind);
        }

        [Fact]
        public void UnknownIdFindsNothing()
        {
            var catalog = new LevelEntityCatalog(new ILevelEntityView[]
            {
                new FakeEntity("村长", "NPC"),
            });

            Assert.False(catalog.TryFind("不存在的编号", out var entity));
            Assert.Null(entity);
        }

        [Fact]
        public void FindsAllEntitiesOfOneKind()
        {
            var catalog = new LevelEntityCatalog(new ILevelEntityView[]
            {
                new FakeEntity("村长", "NPC"),
                new FakeEntity("铁匠", "NPC"),
                new FakeEntity("木箱", "可交互物"),
            });

            Assert.Equal(2, catalog.FindByKind("NPC").Count);
            Assert.Empty(catalog.FindByKind("刷怪点"));
        }

        [Fact]
        public void NullEntriesAreSkipped()
        {
            var catalog = new LevelEntityCatalog(new ILevelEntityView[]
            {
                new FakeEntity("村长", "NPC"),
                null,
            });

            Assert.Single(catalog.Entities);
        }

        // 编号唯一性本该由关卡校验器挡住；真撞上时「按编号查到谁」必须是确定的，
        // 否则同一份关卡在两次运行里能查出不同的实体。
        [Fact]
        public void DuplicateIdKeepsTheFirstEntity()
        {
            var catalog = new LevelEntityCatalog(new ILevelEntityView[]
            {
                new FakeEntity("重复", "NPC"),
                new FakeEntity("重复", "可交互物"),
            });

            Assert.True(catalog.TryFind("重复", out var entity));
            Assert.Equal("NPC", entity.EntityKind);
            Assert.Equal(2, catalog.Entities.Count);
        }

        private sealed class FakeEntity : ILevelEntityView
        {
            public FakeEntity(string entityId, string entityKind)
            {
                EntityId = entityId;
                EntityKind = entityKind;
            }

            public string EntityId { get; }

            public string EntityKind { get; }

            public float3 Position => float3.zero;

            public IReadOnlyDictionary<string, string> Parameters => new Dictionary<string, string>();

            public bool TryGetParameter(string parameterKey, out string parameterValue)
            {
                parameterValue = null;
                return false;
            }
        }
    }
}

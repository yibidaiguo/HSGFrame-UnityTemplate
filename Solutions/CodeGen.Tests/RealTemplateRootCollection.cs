using Xunit;

namespace Template.Toolkit.CodeGen.Tests
{
    /// <summary>
    /// 「往真模板根写盘」的测试合用同一个 xUnit 集合，从而**不并行**。
    ///
    /// 为什么需要它：`ScribanTablePipelineTests` 与 `LubanTablePipelineTests` 都会拿
    /// **真的模板根**去跑 `GenerateAccessCode("Bag")`，两边写的是同一个真实文件
    /// （`UnityProject/.../BagTable.cs`）。xUnit 默认按「测试类」并行，两个类正好在
    /// 两个并行槽里，于是偶尔互相踩——表现是**随机一条红、单独重跑又绿**。
    ///
    /// 这种红最费人：它不指向任何真实缺陷，却会让人怀疑刚改过的东西。
    /// 用一个集合把它们串起来是最小的修法——**测的东西一个字没变**，
    /// 只是不再同时跑（真跑一次 Luban 才是这两个类的价值所在，不能改成写临时目录）。
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class RealTemplateRootCollection
    {
        /// <summary>集合名，两个测试类都挂这个名字。</summary>
        public const string Name = "往真模板根写盘的测试";
    }
}

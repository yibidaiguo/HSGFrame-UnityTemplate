using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 进度同步的测试：权威侧表、三值比对、回流账、进度文档渲染。
    /// 重点盯三件容易做错的事——首次同步不许判冲突、两侧都改过不许静默挑一边、
    /// 正文里不许出现时间戳（不然每轮都会重推一次全文）。
    /// </summary>
    public class ProgressSyncTests
    {
        /// <summary>权威侧表不存在时带原因返回空表——一格都同步不了要说出来，不许静默成功。</summary>
        [Fact]
        public void MissingSchemaReportsReason()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = ProgressSyncSchema.Load(workspace.RepositoryRoot);

            Assert.Empty(schema.Fields);
            Assert.Contains("不存在", schema.LoadFailureReason);
        }

        /// <summary>权威侧取值不认识的那一格被挡掉并报出来，其余格照常收。</summary>
        [Fact]
        public void UnknownAuthorityIsRejectedWithReason()
        {
            using var workspace = new PoolTestWorkspace();
            WriteSchema(workspace.RepositoryRoot, """
            {
              "字段": [
                {"名称": "阶段", "权威侧": "工程", "下游列": "引擎阶段"},
                {"名称": "进展", "权威侧": "随便谁", "下游列": "进展"}
              ]
            }
            """);

            var schema = ProgressSyncSchema.Load(workspace.RepositoryRoot);

            Assert.Single(schema.Fields);
            Assert.Equal("阶段", schema.Fields[0].Name);
            Assert.Contains("随便谁", schema.LoadFailureReason);
        }

        /// <summary>工程格与策划格分得开。</summary>
        [Fact]
        public void SchemaSplitsFieldsByAuthority()
        {
            using var workspace = new PoolTestWorkspace();
            WriteSchema(workspace.RepositoryRoot, DefaultSchemaJson);

            var schema = ProgressSyncSchema.Load(workspace.RepositoryRoot);

            Assert.Equal(new[] { "阶段" }, schema.EngineFields().Select(field => field.Name).ToArray());
            Assert.Equal(new[] { "进展" }, schema.PlannerFields().Select(field => field.Name).ToArray());
        }

        /// <summary>
        /// 第一次同步（没有基线）时两侧不一样一律按权威侧走，**不判冲突**。
        /// 判冲突的话第一次对账会把整张表变成一堆假冲突。
        /// </summary>
        [Fact]
        public void FirstRunNeverProducesConflict()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", ""));
            var downstream = Snapshot(("REQ-0001", "阶段", ""), ("REQ-0001", "进展", "进行中"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, null, hasBaseline: false, schema);

            Assert.Empty(plan.Conflicts());
            Assert.Single(plan.Outbound());
            Assert.Single(plan.Inbound());
            Assert.True(plan.FirstRun);
        }

        /// <summary>只有工程侧相对基线动过 → 出站；下游那一格被盖回去。</summary>
        [Fact]
        public void OnlyEngineMovedGoesOutbound()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "设计中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var downstream = Snapshot(("REQ-0001", "阶段", "设计中"), ("REQ-0001", "进展", "进行中"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);

            var outbound = Assert.Single(plan.Outbound());
            Assert.Equal("阶段", outbound.FieldName);
            Assert.Equal("实现中", outbound.EngineValue);
            Assert.Empty(plan.Conflicts());
        }

        /// <summary>只有下游相对基线动过一个**归下游**的格 → 入站。</summary>
        [Fact]
        public void OnlyDownstreamMovedPlannerFieldGoesInbound()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var downstream = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已完成"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);

            var inbound = Assert.Single(plan.Inbound());
            Assert.Equal("进展", inbound.FieldName);
            Assert.Equal("已完成", inbound.DownstreamValue);
            Assert.Empty(plan.Conflicts());
        }

        /// <summary>
        /// 下游擅自改了**归工程**的格 → 还是出站，工程侧的值盖回去。
        /// 判的是权威侧不是「谁动的」：非权威侧的改动正是要被盖掉的那种。
        /// </summary>
        [Fact]
        public void DownstreamTouchingEngineFieldIsOverwritten()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var downstream = Snapshot(("REQ-0001", "阶段", "我改了"), ("REQ-0001", "进展", "进行中"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);

            var outbound = Assert.Single(plan.Outbound());
            Assert.Equal("阶段", outbound.FieldName);
            Assert.Equal("实现中", outbound.SettledValue);
        }

        /// <summary>两侧相对基线都动过 → 冲突，一格都不覆盖。</summary>
        [Fact]
        public void BothMovedBecomesConflict()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已停滞"));
            var downstream = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已完成"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);

            var conflict = Assert.Single(plan.Conflicts());
            Assert.Equal("进展", conflict.FieldName);
            Assert.Empty(plan.Outbound());
            Assert.Empty(plan.Inbound());
        }

        /// <summary>下游还没有这一行时不判成「下游清空了」，而是排进待建行。</summary>
        [Fact]
        public void MissingDownstreamRowBecomesRowToCreate()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0007", "阶段", "实现中"), ("REQ-0007", "进展", ""));

            var plan = ProgressSyncPlanner.Plan(engine, new ProgressSnapshot(null, null), null, hasBaseline: false, schema);

            Assert.Equal(new[] { "REQ-0007" }, plan.RowsToCreate.ToArray());
            Assert.Empty(plan.Decisions);
        }

        /// <summary>
        /// 冲突那几格进基线时**保持上一次的基线值**。
        ///
        /// 这条断言原本写的是「取工程侧当前值」，理由是「基线记事实」——听着对，做出来是错的：
        /// 下一轮那一格就变成「只有下游改过」，于是按权威侧把下游盖掉，
        /// 冲突自己化解了。真跑飞书时抓到的（连跑两轮，第二轮出站 1 格把人手改的值冲了）。
        /// </summary>
        [Fact]
        public void SettledSnapshotFreezesBaselineForConflicts()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已停滞"));
            var downstream = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已完成"));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);
            var settled = plan.SettledSnapshot(engine);

            Assert.Equal("进行中", settled.Find("REQ-0001").Value("进展"));
        }

        /// <summary>基线文件不存在 = 没有基线，且不算失败。</summary>
        [Fact]
        public void MissingBaselineIsNotAFailure()
        {
            using var workspace = new PoolTestWorkspace();
            var baseline = ProgressSyncBaseline.Load(workspace.RepositoryRoot, out var hasBaseline, out var reason);

            Assert.False(hasBaseline);
            Assert.Equal("", reason);
            Assert.Empty(baseline.Entries);
        }

        /// <summary>基线坏掉时算「有基线」并带原因——当成没有基线会把真冲突静默覆盖掉。</summary>
        [Fact]
        public void BrokenBaselineIsReportedNotTreatedAsMissing()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = ProgressSyncBaseline.BaselineFile(workspace.RepositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, "{ 这不是 JSON");

            ProgressSyncBaseline.Load(workspace.RepositoryRoot, out var hasBaseline, out var reason);

            Assert.True(hasBaseline);
            Assert.Contains("读不了", reason);
        }

        /// <summary>基线写盘再读回来，值一字不差。</summary>
        [Fact]
        public void BaselineRoundTrips()
        {
            using var workspace = new PoolTestWorkspace();
            ProgressSyncBaseline.Save(workspace.RepositoryRoot, Snapshot(("REQ-0001", "阶段", "实现中")));

            var loaded = ProgressSyncBaseline.Load(workspace.RepositoryRoot, out var hasBaseline, out _);

            Assert.True(hasBaseline);
            Assert.Equal("实现中", loaded.Find("REQ-0001").Value("阶段"));
        }

        /// <summary>回流账只收归策划端的那几格，工程格一格都不进去。</summary>
        [Fact]
        public void InboundLedgerKeepsOnlyPlannerFields()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "进行中"));
            var engine = Snapshot(("REQ-0001", "阶段", "验收中"), ("REQ-0001", "进展", "进行中"));
            var downstream = Snapshot(("REQ-0001", "阶段", "实现中"), ("REQ-0001", "进展", "已完成"));
            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);

            var saved = ProgressInboundLedger.Save(workspace.RepositoryRoot, plan, schema, "2026-08-24T10:00:00+08:00");

            var entry = saved.Find("REQ-0001");
            Assert.Equal("已完成", entry.Value("进展"));
            Assert.False(entry.Fields.ContainsKey("阶段"));
            Assert.True(File.Exists(ProgressInboundLedger.LedgerFile(workspace.RepositoryRoot)));
        }

        /// <summary>下游行折成快照时，没有「需求id」那一列的行被丢掉，不当成一条新需求。</summary>
        [Fact]
        public void DownstreamRowWithoutIdentifierIsDropped()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var rows = new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string> { ["需求id"] = "REQ-0001", ["进展"] = "进行中" },
                new Dictionary<string, string> { ["进展"] = "已完成" }
            };

            var snapshot = ProgressSnapshot.FromDownstreamRows(rows, schema, "需求id");

            Assert.Single(snapshot.Entries);
            Assert.Equal("REQ-0001", snapshot.Entries[0].Identifier);
            Assert.Equal("进行中", snapshot.Entries[0].Value("进展"));
        }

        /// <summary>仓库侧快照：池子里每条需求一行，标题读得回来，没开跑的写「尚未开跑」。</summary>
        [Fact]
        public void RepositorySnapshotReadsTitleAndStage()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = PoolPaths.RequirementDirectory(workspace.Root, "REQ-0001");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                PoolPaths.RequirementFile(workspace.Root, "REQ-0001"),
                """{"id":"REQ-0001","标题":"背包系统","状态":"草稿"}""");

            var snapshot = ProgressSnapshot.CollectFromRepository(workspace.RepositoryRoot, workspace.Root);

            var entry = Assert.Single(snapshot.Entries);
            Assert.Equal("背包系统", entry.Value(ProgressSnapshot.TitleField));
            Assert.Equal("尚未开跑", entry.Value(ProgressSnapshot.StageField));
            Assert.Equal("1", snapshot.Global["需求数"]);
        }

        /// <summary>
        /// 有工作项失败时，「阶段」这一格要把失败的工作项说出来。
        ///
        /// **这一格就是飞书任务表里「引擎阶段」那一列**——人在飞书看到的就这一格。
        /// 只写「验收/等人」的话，一条卡在出图失败上的需求与一条正常等人验收的需求
        /// 长得一模一样，而这两件事要做的动作完全相反。
        /// </summary>
        [Fact]
        public void StageCellNamesFailedWorkItems()
        {
            using var workspace = new PoolTestWorkspace();
            Directory.CreateDirectory(PoolPaths.RequirementDirectory(workspace.Root, "REQ-0001"));
            File.WriteAllText(
                PoolPaths.RequirementFile(workspace.Root, "REQ-0001"),
                """{"id":"REQ-0001","标题":"背包系统","状态":"草稿"}""");

            var taskDirectory = Path.Combine(workspace.RepositoryRoot, "_Tasks", "REQ-0001");
            Directory.CreateDirectory(taskDirectory);
            File.WriteAllText(
                Path.Combine(taskDirectory, "state.json"),
                """{"阶段":"验收","子状态":"等人","当前工作项":"WI-0001-02","产物哈希":{},"预算":{}}""");

            var workItems = Path.Combine(taskDirectory, "20-work-items");
            Directory.CreateDirectory(workItems);
            File.WriteAllText(Path.Combine(workItems, "WI-0001-01.json"),
                """{"id":"WI-0001-01","标题":"拆需求","依赖":[],"状态":"已完成","引用需求字段":[]}""");
            File.WriteAllText(Path.Combine(workItems, "WI-0001-02.json"),
                """{"id":"WI-0001-02","标题":"出图","依赖":["WI-0001-01"],"状态":"失败","引用需求字段":[]}""");

            var snapshot = ProgressSnapshot.CollectFromRepository(workspace.RepositoryRoot, workspace.Root);

            var stage = Assert.Single(snapshot.Entries).Value(ProgressSnapshot.StageField);
            Assert.Contains("验收/等人", stage);
            Assert.Contains("WI-0001-02", stage);
            Assert.Contains("卡住", stage);
            // 没失败的那个不许出现——把「已完成」也列出来等于没说
            Assert.DoesNotContain("WI-0001-01", stage);
        }

        /// <summary>
        /// 进度文档的**正文**里一个时间戳都没有：换一个生成时间重渲，正文哈希必须一样。
        /// 这一条守的是「只推变了的」——正文带时间戳的话每轮都会往知识库刷一版空版本。
        /// </summary>
        [Fact]
        public void DocumentBodyHashIsStableAcrossMoments()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"));
            var inbound = Snapshot(("REQ-0001", "进展", "进行中"));
            var empty = new RequirementDocumentSyncState("", "", "", "");

            var first = ProgressDocumentRenderer.Render("RPG", engine, inbound, schema, "2026-08-24T10:00:00+08:00", empty);
            var second = ProgressDocumentRenderer.Render("RPG", engine, inbound, schema, "2026-08-25T23:59:00+08:00", empty);

            Assert.NotEqual(first, second);
            Assert.Equal(
                RequirementDocumentSyncState.HashBody(first),
                RequirementDocumentSyncState.HashBody(second));
        }

        /// <summary>进度文档里，归策划端的那一格取回流账的值而不是工程侧的空值。</summary>
        [Fact]
        public void DocumentTakesPlannerCellsFromInboundLedger()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0001", "阶段", "实现中"));
            var inbound = Snapshot(("REQ-0001", "进展", "已完成"));

            var text = ProgressDocumentRenderer.Render(
                "RPG", engine, inbound, schema, "2026-08-24T10:00:00+08:00",
                new RequirementDocumentSyncState("", "", "", ""));

            Assert.Contains("已完成", text);
            Assert.Contains("实现中", text);
        }

        /// <summary>「进度同步」是合法的冲突发现阶段——落账走的是既有那条裁决通道。</summary>
        [Fact]
        public void ProgressSyncIsAnAllowedConflictStage()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(
                workspace.Root, "REQ-0001.进展@下游", "REQ-0001.进展@工程", "进度同步");

            Assert.Equal("进度同步", entry.DiscoveryStage);
            Assert.Contains("进度同步", ConflictEntry.AllowedStages);
        }

        /// <summary>
        /// 冲突那一格的基线**保持不动**，不取工程侧当前值。
        /// 真跑出来的：取工程侧值的话，下一轮那格变成「只有下游改过」，
        /// 于是按权威侧把下游直接盖掉——一条刚落账的冲突自己化解了，人没来得及看见。
        /// </summary>
        [Fact]
        public void ConflictCellKeepsBaselineSoItStaysFrozen()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "尚未开跑"), ("REQ-0001", "进展", ""));
            var engine = Snapshot(("REQ-0001", "阶段", "实现/跑着"), ("REQ-0001", "进展", ""));
            var downstream = Snapshot(("REQ-0001", "阶段", "人手改的"), ("REQ-0001", "进展", ""));

            var plan = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);
            var settled = plan.SettledSnapshot(engine);

            Assert.Single(plan.Conflicts());
            Assert.Equal("尚未开跑", settled.Find("REQ-0001").Value("阶段"));
        }

        /// <summary>
        /// 拿上一轮的基线再比一次，**还是冲突**、还是一格都不出站——那一格冻到有人来对齐为止。
        /// </summary>
        [Fact]
        public void FrozenConflictStaysConflictOnTheNextRound()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var baseline = Snapshot(("REQ-0001", "阶段", "尚未开跑"), ("REQ-0001", "进展", ""));
            var engine = Snapshot(("REQ-0001", "阶段", "实现/跑着"), ("REQ-0001", "进展", ""));
            var downstream = Snapshot(("REQ-0001", "阶段", "人手改的"), ("REQ-0001", "进展", ""));

            var first = ProgressSyncPlanner.Plan(engine, downstream, baseline, hasBaseline: true, schema);
            var second = ProgressSyncPlanner.Plan(
                engine, downstream, first.SettledSnapshot(engine), hasBaseline: true, schema);

            Assert.Single(second.Conflicts());
            Assert.Empty(second.Outbound());
            Assert.Empty(second.Inbound());
        }

        /// <summary>
        /// 仓库侧对策划端那几格的值来自回流账，不是空串。
        /// 不并回流账的话每一轮都会判成「下游单边改过」，同样两格反复回流、永不收敛
        /// （真跑出来的：第 3 轮回流 2 格对，第 4 轮什么都没动却又回流同样 2 格）。
        /// </summary>
        [Fact]
        public void MergingInboundLedgerMakesPlannerCellsConverge()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0001", "阶段", "尚未开跑"), ("REQ-0001", "进展", ""));
            var inbound = Snapshot(("REQ-0001", "进展", "进行中"));
            var downstream = Snapshot(("REQ-0001", "阶段", "尚未开跑"), ("REQ-0001", "进展", "进行中"));
            var baseline = Snapshot(("REQ-0001", "阶段", "尚未开跑"), ("REQ-0001", "进展", "进行中"));

            var merged = engine.MergePlannerFields(inbound, schema.PlannerFields().Select(field => field.Name));
            var plan = ProgressSyncPlanner.Plan(merged, downstream, baseline, hasBaseline: true, schema);

            Assert.Equal("进行中", merged.Find("REQ-0001").Value("进展"));
            Assert.Empty(plan.Inbound());
            Assert.Empty(plan.Conflicts());
        }

        /// <summary>合并只动策划端那几格，工程格一格都不许被回流账盖掉。</summary>
        [Fact]
        public void MergingInboundLedgerNeverTouchesEngineCells()
        {
            var schema = LoadSchema(DefaultSchemaJson);
            var engine = Snapshot(("REQ-0001", "阶段", "实现/跑着"));
            var inbound = Snapshot(("REQ-0001", "阶段", "回流账里的脏值"), ("REQ-0001", "进展", "已完成"));

            var merged = engine.MergePlannerFields(inbound, schema.PlannerFields().Select(field => field.Name));

            Assert.Equal("实现/跑着", merged.Find("REQ-0001").Value("阶段"));
            Assert.Equal("已完成", merged.Find("REQ-0001").Value("进展"));
        }

        /// <summary>缺省的权威侧表 JSON：一格工程、一格策划端，够比对用。</summary>
        private const string DefaultSchemaJson = """
        {
          "字段": [
            {"名称": "阶段", "权威侧": "工程", "下游列": "引擎阶段"},
            {"名称": "进展", "权威侧": "策划端", "下游列": "进展"}
          ]
        }
        """;

        /// <summary>把权威侧表写进工作区并读回来。</summary>
        private static ProgressSyncSchema LoadSchema(string json)
        {
            var root = Path.Combine(Path.GetTempPath(), "进度同步测试-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteSchema(root, json);
                return ProgressSyncSchema.Load(root);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>把权威侧表写到 Tools/CreationPipeline/Config/progress-sync.json。</summary>
        private static void WriteSchema(string repositoryRoot, string json)
        {
            var filePath = ProgressSyncSchema.SchemaFile(repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }

        /// <summary>拼一份快照：三元组 (需求id, 字段, 值)。</summary>
        private static ProgressSnapshot Snapshot(params (string Identifier, string Field, string Value)[] cells)
        {
            var byIdentifier = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                if (!byIdentifier.TryGetValue(cell.Identifier, out var fields))
                {
                    fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    byIdentifier[cell.Identifier] = fields;
                }

                fields[cell.Field] = cell.Value;
            }

            var entries = byIdentifier
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ProgressEntry(pair.Key, pair.Value))
                .ToList();
            return new ProgressSnapshot(entries, null);
        }
    }
}

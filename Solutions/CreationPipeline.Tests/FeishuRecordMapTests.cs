using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Bridges.Feishu;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 字段值 ↔ 飞书记录字段的双向映射测试（纯函数，脱离网络可测）。
    /// 要点（与任务书红线一一对应）：
    /// - 单选值不在选项里 → 报错，不许自动加选项；
    /// - 复选框收到字符串 → 报错，不许硬转；
    /// - 文本收到非字符串 → 报错；
    /// - 记录 → 入站信封的字段对应正确（产出的 JSON 能被 InboxEnvelope.TryRead 读回）；
    /// - 同一条记录转两次文件名相同（幂等）。
    /// </summary>
    public class FeishuRecordMapTests
    {
        /// <summary>建表描述里的字段元数据（测试自造，只含本次用例需要的字段）。</summary>
        private static Dictionary<string, FieldSchema> BuildSchema()
        {
            var fields = new List<FieldSchema>
            {
                new FieldSchema("id", "文本", Array.Empty<string>()),
                new FieldSchema("类型", "单选", new[] { "系统", "修改", "缺陷" }),
                new FieldSchema("状态", "单选", new[] { "草稿", "已确认", "进行中", "待验收", "已完成", "已作废" }),
                new FieldSchema("标题", "文本", Array.Empty<string>()),
                new FieldSchema("验收标准", "多行文本", Array.Empty<string>()),
                new FieldSchema("锁定", "复选框", Array.Empty<string>()),
                new FieldSchema("schema版本", "文本", Array.Empty<string>())
            };

            var byName = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                byName[field.Name] = field;
            }

            return byName;
        }

        // —— 写方向：逻辑值 → 飞书写入值 ——

        /// <summary>单选值不在选项列表里必须报错，不许自动加选项。</summary>
        [Fact]
        public void SingleSelectValueOutsideOptionsFailsInsteadOfAutoAdding()
        {
            var schema = new FieldSchema("类型", "单选", new[] { "系统", "修改", "缺陷" });
            var value = JsonSerializer.SerializeToElement("不存在的选项");

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, value, out _, out var reason);

            Assert.False(succeeded);
            Assert.Contains("不在选项列表里", reason);
            Assert.Contains("不许自动加选项", reason); // 文案要明说「不许自动加」——自动加会让下游枚举悄悄漂移
        }

        /// <summary>单选值在选项列表里正常通过。</summary>
        [Theory]
        [InlineData("系统")]
        [InlineData("修改")]
        [InlineData("缺陷")]
        public void SingleSelectValueInsideOptionsPasses(string option)
        {
            var schema = new FieldSchema("类型", "单选", new[] { "系统", "修改", "缺陷" });

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, JsonSerializer.SerializeToElement(option), out var mapped, out var reason);

            Assert.True(succeeded);
            Assert.Equal("", reason);
            Assert.Equal(option, mapped.GetString());
        }

        /// <summary>复选框收到字符串必须报错，不许硬转。</summary>
        [Fact]
        public void CheckboxGivenStringFailsInsteadOfCoercing()
        {
            var schema = new FieldSchema("锁定", "复选框", Array.Empty<string>());
            var value = JsonSerializer.SerializeToElement("true");

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, value, out _, out var reason);

            Assert.False(succeeded);
            Assert.Contains("布尔", reason);
        }

        /// <summary>复选框收到数字必须报错。</summary>
        [Fact]
        public void CheckboxGivenNumberFailsToo()
        {
            var schema = new FieldSchema("锁定", "复选框", Array.Empty<string>());
            var value = JsonSerializer.SerializeToElement(1);

            Assert.False(FeishuRecordFieldMap.TryMapWrite(schema, value, out _, out var reason));
            Assert.Contains("布尔", reason);
        }

        /// <summary>复选框收布尔正常通过。</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CheckboxAcceptsBool(bool value)
        {
            var schema = new FieldSchema("锁定", "复选框", Array.Empty<string>());

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, JsonSerializer.SerializeToElement(value), out var mapped, out _);

            Assert.True(succeeded);
            Assert.Equal(value, mapped.GetBoolean());
        }

        /// <summary>文本字段收到数字必须报错，不许硬转成字符串。</summary>
        [Fact]
        public void TextGivenNumberFailsInsteadOfCoercing()
        {
            var schema = new FieldSchema("标题", "文本", Array.Empty<string>());
            var value = JsonSerializer.SerializeToElement(42);

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, value, out _, out var reason);

            Assert.False(succeeded);
            Assert.Contains("字符串", reason);
        }

        /// <summary>文本字段收字符串正常通过。</summary>
        [Fact]
        public void TextAcceptsString()
        {
            var schema = new FieldSchema("标题", "文本", Array.Empty<string>());

            var succeeded = FeishuRecordFieldMap.TryMapWrite(schema, JsonSerializer.SerializeToElement("七日签到"), out var mapped, out _);

            Assert.True(succeeded);
            Assert.Equal("七日签到", mapped.GetString());
        }

        // —— 读方向：飞书读回值 → 逻辑值 ——

        /// <summary>文本字段读回字符串，原样归一化成字符串。</summary>
        [Fact]
        public void ReadTextAsStringNormalizesToString()
        {
            var schema = new FieldSchema("标题", "文本", Array.Empty<string>());

            var succeeded = FeishuRecordFieldMap.TryMapRead(schema, JsonSerializer.SerializeToElement("七日签到"), out var mapped, out _);

            Assert.True(succeeded);
            Assert.Equal("七日签到", mapped.GetString());
        }

        /// <summary>文本字段读回 [{"text":…}] 富文本数组，归一化成字符串。</summary>
        [Fact]
        public void ReadTextAsRichTextArrayNormalizesToString()
        {
            var schema = new FieldSchema("标题", "文本", Array.Empty<string>());
            var array = JsonSerializer.SerializeToElement(new object[] { new { text = "七日" }, new { text = "签到" } });

            var succeeded = FeishuRecordFieldMap.TryMapRead(schema, array, out var mapped, out var reason);

            Assert.True(succeeded);
            Assert.Equal("", reason);
            Assert.Equal("七日签到", mapped.GetString());
        }

        /// <summary>单选字段读回选项名字符串。</summary>
        [Fact]
        public void ReadSingleSelectReturnsOptionName()
        {
            var schema = new FieldSchema("类型", "单选", new[] { "系统", "修改", "缺陷" });

            var succeeded = FeishuRecordFieldMap.TryMapRead(schema, JsonSerializer.SerializeToElement("缺陷"), out var mapped, out _);

            Assert.True(succeeded);
            Assert.Equal("缺陷", mapped.GetString());
        }

        /// <summary>复选框字段读回布尔。</summary>
        [Fact]
        public void ReadCheckboxReturnsBool()
        {
            var schema = new FieldSchema("锁定", "复选框", Array.Empty<string>());

            var succeeded = FeishuRecordFieldMap.TryMapRead(schema, JsonSerializer.SerializeToElement(true), out var mapped, out _);

            Assert.True(succeeded);
            Assert.True(mapped.GetBoolean());
        }

        // —— 记录 → 入站信封 ——

        /// <summary>一条飞书记录转成入站信封：字段对应正确，且产出的 JSON 能被 InboxEnvelope.TryRead 读回。</summary>
        [Fact]
        public void RecordMapsToInboxEnvelopeWithCorrectFields()
        {
            var record = JsonDocument.Parse(
                "{\"record_id\":\"recTEST0001\"," +
                "\"fields\":{\"id\":\"REQ-TEST-0001\",\"类型\":\"系统\",\"状态\":\"草稿\"," +
                "\"标题\":\"测试需求\",\"验收标准\":\"能跑通一次往返\",\"锁定\":true,\"schema版本\":\"1.0.0\"}," +
                "\"last_modified_time\":1789000000000}").RootElement;

            var succeeded = RecordReader.TryBuildEnvelope(record, BuildSchema(), out var envelope, out var reason);

            Assert.True(succeeded);
            Assert.Equal("", reason);
            Assert.Equal("REQ-TEST-0001.r1789000000.json", envelope.FileName);

            // 产出的 JSON 必须是引擎能读的入站信封：落临时文件，用 InboxEnvelope.TryRead（引擎侧契约）读回。
            var tempPath = Path.Combine(Path.GetTempPath(), envelope.FileName);
            try
            {
                File.WriteAllText(tempPath, envelope.ToJson());

                Assert.True(InboxEnvelope.TryRead(tempPath, out var parsed, out var parseReason), parseReason);
                Assert.Equal("feishu", parsed.Channel);
                Assert.Equal("REQ-TEST-0001", parsed.RecordIdentifier);
                Assert.Equal(1789000000, parsed.Revision);
                Assert.Equal("系统", parsed.Fields["类型"].GetString());
                Assert.Equal("草稿", parsed.Fields["状态"].GetString());
                Assert.Equal("测试需求", parsed.Fields["标题"].GetString());
                Assert.Equal("能跑通一次往返", parsed.Fields["验收标准"].GetString());
                Assert.True(parsed.Fields["锁定"].GetBoolean());
                Assert.Equal("1.0.0", parsed.Fields["schema版本"].GetString());
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        /// <summary>同一条记录转两次，文件名相同（幂等：重复拉不产出两个不同名的文件）。</summary>
        [Fact]
        public void SameRecordTwiceProducesSameFileName()
        {
            var recordJson =
                "{\"record_id\":\"recTEST0002\"," +
                "\"fields\":{\"id\":\"REQ-TEST-0002\",\"类型\":\"修改\",\"状态\":\"已确认\",\"标题\":\"幂等测试\",\"锁定\":false}," +
                "\"last_modified_time\":1789100000000}";
            var first = JsonDocument.Parse(recordJson).RootElement;
            var second = JsonDocument.Parse(recordJson).RootElement;

            Assert.True(RecordReader.TryBuildEnvelope(first, BuildSchema(), out var firstEnvelope, out _));
            Assert.True(RecordReader.TryBuildEnvelope(second, BuildSchema(), out var secondEnvelope, out _));

            Assert.Equal(firstEnvelope.FileName, secondEnvelope.FileName);
            Assert.Equal("REQ-TEST-0002.r1789100000.json", firstEnvelope.FileName);
        }

        /// <summary>缺 id 字段的记录转不成信封，给可读原因。</summary>
        [Fact]
        public void RecordWithoutIdFieldFailsWithReadableReason()
        {
            var record = JsonDocument.Parse(
                "{\"record_id\":\"recTEST0003\"," +
                "\"fields\":{\"标题\":\"没有 id\"}," +
                "\"last_modified_time\":1789200000000}").RootElement;

            Assert.False(RecordReader.TryBuildEnvelope(record, BuildSchema(), out _, out var reason));
            Assert.Contains("id", reason);
        }

        /// <summary>修订号从最后修改时刻推导，必须非负且确定。</summary>
        [Fact]
        public void RevisionDerivedFromLastModifiedIsDeterministicAndNonNegative()
        {
            var moment = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.FromHours(8));

            var revision = RecordReader.ToRevisionInt(moment);

            Assert.True(revision >= 0);
            Assert.Equal(revision, RecordReader.ToRevisionInt(moment));
        }

        /// <summary>文件名清理保持确定性，路径分隔符被替换。</summary>
        [Fact]
        public void SanitizeFileNameKeepsDeterministicAndReplacesInvalidChars()
        {
            Assert.Equal("REQ-TEST-0001", RecordReader.SanitizeFileName("REQ-TEST-0001"));
            var sanitized = RecordReader.SanitizeFileName("a/b\\c:d");
            Assert.Equal("a_b_c_d", sanitized);
        }

        /// <summary>
        /// 数组 → 多行文本 → 数组的往返必须闭合。
        /// 这一条是真跑撞出来的：schema 里「验收标准」是数组，下游只有文本列，
        /// 原来写方向直接判「不许硬转」，结果一条合法需求根本写不进下游表。
        /// 现在按建表描述里的「逻辑类型」序列化，一行一条，读回来按同一条规则切开。
        /// </summary>
        [Fact]
        public void ArrayFieldRoundTripsThroughTextColumn()
        {
            var schema = new FieldSchema("验收标准", "多行文本", Array.Empty<string>(), "数组");
            var logical = JsonSerializer.SerializeToElement(new[] { "第一条", "第二条" });

            Assert.True(FeishuRecordFieldMap.TryMapWrite(schema, logical, out var written, out var writeReason), writeReason);
            Assert.Equal("第一条\n第二条", written.GetString());

            Assert.True(FeishuRecordFieldMap.TryMapRead(schema, written, out var readBack, out var readReason), readReason);
            Assert.Equal(JsonValueKind.Array, readBack.ValueKind);
            Assert.Equal(new[] { "第一条", "第二条" }, readBack.EnumerateArray().Select(item => item.GetString()));
        }

        /// <summary>数组元素自带换行会被拒——一行一条是切回数组的唯一依据，混进换行往返就闭不上。</summary>
        [Fact]
        public void ArrayElementWithNewlineIsRejected()
        {
            var schema = new FieldSchema("验收标准", "多行文本", Array.Empty<string>(), "数组");
            var logical = JsonSerializer.SerializeToElement(new[] { "第一条\n偷偷换行" });

            Assert.False(FeishuRecordFieldMap.TryMapWrite(schema, logical, out _, out var reason));
            Assert.Contains("换行", reason);
        }

        /// <summary>对象 → 文本 → 对象同样闭合；读回来不是合法 JSON 要报错，不许当普通字符串塞回去。</summary>
        [Fact]
        public void ObjectFieldRoundTripsAndRejectsGarbage()
        {
            var schema = new FieldSchema("来源", "多行文本", Array.Empty<string>(), "对象");
            var logical = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["渠道"] = "助手会话" });

            Assert.True(FeishuRecordFieldMap.TryMapWrite(schema, logical, out var written, out var writeReason), writeReason);
            Assert.True(FeishuRecordFieldMap.TryMapRead(schema, written, out var readBack, out _));
            Assert.Equal("助手会话", readBack.GetProperty("渠道").GetString());

            var garbage = JsonSerializer.SerializeToElement("not json at all");
            Assert.False(FeishuRecordFieldMap.TryMapRead(schema, garbage, out _, out var reason));
            Assert.Contains("对象", reason);
        }

        /// <summary>没声明逻辑类型的文本列行为一个字不变：收字符串，给别的报错。</summary>
        [Fact]
        public void PlainTextFieldKeepsOldBehaviour()
        {
            var schema = new FieldSchema("标题", "文本", Array.Empty<string>());

            Assert.True(FeishuRecordFieldMap.TryMapWrite(schema, JsonSerializer.SerializeToElement("背包排序"), out _, out _));
            Assert.False(FeishuRecordFieldMap.TryMapWrite(schema, JsonSerializer.SerializeToElement(new[] { "a" }), out _, out var reason));
            Assert.Contains("不许硬转", reason);
        }
    }
}

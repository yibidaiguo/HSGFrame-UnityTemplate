using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 测试夹具：在系统临时目录下自造一个用完即删的池子目录，供测试写入基线 schema、
    /// 项目扩展 schema 与需求文件。每个测试各自建一个，互不共享磁盘状态。
    /// </summary>
    public sealed class PoolTestWorkspace : IDisposable
    {
        /// <summary>
        /// 在临时目录下创建「创作管线测试-&lt;Guid&gt;」目录，并建出 Schema/Baseline、Schema/Project、Requirements 三个子目录。
        /// </summary>
        public PoolTestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "创作管线测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(PoolPaths.SchemaBaselineDirectory(Root));
            Directory.CreateDirectory(PoolPaths.SchemaProjectDirectory(Root));
            Directory.CreateDirectory(PoolPaths.RequirementsDirectory(Root));
        }

        /// <summary>本工作区池子的根目录，用完由 Dispose 递归删除。</summary>
        public string Root { get; }

        /// <summary>收件箱目录：Inbox。构造时不预建，由写入方法按需创建。</summary>
        public string InboxDirectory
        {
            get { return PoolPaths.InboxDirectory(Root); }
        }

        /// <summary>仓库根目录：测试里与池根同一目录，让 _Tasks/_Generated 落在工作区里，Dispose 一并删除。</summary>
        public string RepositoryRoot
        {
            get { return Root; }
        }

        /// <summary>
        /// 把基线 schema JSON 写到 Schema/Baseline/&lt;实体&gt;.schema.json。
        /// </summary>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <param name="json">基线 schema 的 JSON 文本。</param>
        public void WriteBaselineSchema(string entityName, string json)
        {
            WriteFile(PoolPaths.BaselineSchemaFile(Root, entityName), json);
        }

        /// <summary>
        /// 把项目扩展 schema JSON 写到 Schema/Project/&lt;实体&gt;.扩展.json。
        /// </summary>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <param name="json">项目扩展 schema 的 JSON 文本。</param>
        public void WriteProjectSchema(string entityName, string json)
        {
            WriteFile(PoolPaths.ProjectSchemaFile(Root, entityName), json);
        }

        /// <summary>
        /// 把内容写成一条需求：Requirements/&lt;id&gt;/requirement.json。
        /// </summary>
        /// <param name="identifier">需求 id，如「REQ-0001」。目录名即 id，校验器按它判。</param>
        /// <param name="json">requirement.json 的内容。</param>
        public void WriteRequirement(string identifier, string json)
        {
            WriteFile(PoolPaths.RequirementFile(Root, identifier), json);
        }

        /// <summary>
        /// 把内容写到某条需求目录下的任意相对路径，如「index.md」「media/a.png」。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="relativePath">相对该需求目录的路径。</param>
        /// <param name="content">文件内容。</param>
        public void WriteRequirementFile(string identifier, string relativePath, string content)
        {
            WriteFile(Path.Combine(PoolPaths.RequirementDirectory(Root, identifier), relativePath), content);
        }

        /// <summary>
        /// 把一段 JSON 文本写入 Inbox 目录的指定文件；目录不存在时先创建。
        /// </summary>
        /// <param name="fileName">信封文件名，如「feishu-recABC123-3.json」。</param>
        /// <param name="json">信封 JSON 内容。</param>
        public void WriteInbox(string fileName, string json)
        {
            var directory = PoolPaths.InboxDirectory(Root);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), json);
        }

        /// <summary>读取某条需求的 requirement.json 全文。</summary>
        /// <param name="identifier">需求 id，如「REQ-0001」。</param>
        public string ReadRequirement(string identifier)
        {
            return File.ReadAllText(PoolPaths.RequirementFile(Root, identifier));
        }

        /// <summary>某条需求的 requirement.json 在不在。</summary>
        /// <param name="identifier">需求 id，如「REQ-0001」。</param>
        public bool RequirementExists(string identifier)
        {
            return File.Exists(PoolPaths.RequirementFile(Root, identifier));
        }

        /// <summary>
        /// 返回一份够用的「需求」基线 schema JSON：含 schema版本、实体、id模式，
        /// 字段里有 id/类型/状态/标题/验收标准，另有分类型必填三类与六条状态机转换。
        /// </summary>
        public static string MinimalRequirementSchema()
        {
            return """
            {
              "schema版本": "1.0.0",
              "实体": "需求",
              "id模式": "^REQ-\\d{4}$",
              "字段": [
                { "名称": "id", "类型": "string", "必填": true },
                { "名称": "类型", "类型": "enum", "枚举": ["系统", "修改", "缺陷"], "必填": true },
                { "名称": "状态", "类型": "enum", "枚举": ["草稿", "已确认", "进行中", "待验收", "已完成", "已作废"], "必填": true },
                { "名称": "标题", "类型": "string", "必填": true },
                { "名称": "验收标准", "类型": "数组", "元素类型": "string", "必填": true, "最少条数": 1 }
              ],
              "分类型必填": {
                "系统": ["目标", "玩法"],
                "修改": ["现状", "期望"],
                "缺陷": ["复现步骤", "期望", "实际"]
              },
              "状态机": {
                "初始状态": "草稿",
                "转换": [
                  { "从": "草稿", "到": "已确认", "谁": "确认人" },
                  { "从": "已确认", "到": "进行中", "谁": "引擎" },
                  { "从": "进行中", "到": "待验收", "谁": "引擎" },
                  { "从": "待验收", "到": "已完成", "谁": "引擎" },
                  { "从": "待验收", "到": "进行中", "谁": "引擎" },
                  { "从": "*", "到": "已作废", "谁": "确认人" }
                ]
              }
            }
            """;
        }

        /// <summary>
        /// 把成员目录 JSON 写到 &lt;Root&gt;/组织/成员.json，目录不存在先创建。
        /// </summary>
        /// <param name="json">成员目录的 JSON 文本。</param>
        public void WriteMemberDirectory(string json)
        {
            var directory = PoolPaths.OrganizationDirectory(Root);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "成员.json"), json);
        }

        /// <summary>
        /// 把卡片路由 JSON 写到 &lt;Root&gt;/组织/卡片路由.json，目录不存在先创建。
        /// </summary>
        /// <param name="json">卡片路由的 JSON 文本。</param>
        public void WriteCardRoute(string json)
        {
            var directory = PoolPaths.OrganizationDirectory(Root);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "卡片路由.json"), json);
        }

        /// <summary>
        /// 把专项 JSON 写到 &lt;Root&gt;/专项/&lt;fileName&gt;，目录不存在先创建。
        /// </summary>
        /// <param name="fileName">专项文件名，如「EP-0001.json」。</param>
        /// <param name="json">专项 JSON 文本。</param>
        public void WriteEpic(string fileName, string json)
        {
            var directory = PoolPaths.EpicsDirectory(Root);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), json);
        }

        /// <summary>
        /// 列 &lt;Root&gt;/_Generated/出站/ 下的全部文件全路径；目录不存在返回空列表，结果按序数序排序。
        /// </summary>
        public IReadOnlyList<string> ListOutboundFiles()
        {
            var directory = PipelinePaths.OutboundDirectory(Root);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var files = Directory.GetFiles(directory).ToList();
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        /// <summary>
        /// 把任务状态 JSON 写到 &lt;Root&gt;/_Tasks/&lt;需求id&gt;/状态.json，目录不存在先创建。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="json">任务状态 JSON 文本。</param>
        public void WriteTaskState(string requirementIdentifier, string json)
        {
            var directory = PipelinePaths.TaskDirectory(Root, requirementIdentifier);
            Directory.CreateDirectory(directory);
            WriteFile(PipelinePaths.TaskStateFile(Root, requirementIdentifier), json);
        }

        /// <summary>
        /// 把引擎配置 JSON 写到 &lt;Root&gt;/Tools/CreationPipeline/Config/engine.json，目录不存在先创建。
        /// </summary>
        /// <param name="json">引擎配置 JSON 文本。</param>
        public void WriteEngineSettings(string json)
        {
            var directory = Path.GetDirectoryName(EngineSettings.SettingsFile(Root));
            Directory.CreateDirectory(directory);
            WriteFile(EngineSettings.SettingsFile(Root), json);
        }

        /// <summary>&lt;Root&gt;/queue.json 是否存在。</summary>
        public bool QueueFileExists()
        {
            return File.Exists(PoolPaths.QueueFile(Root));
        }

        /// <summary>读取 &lt;Root&gt;/queue.json 全文；文件不存在返回空串。</summary>
        public string ReadQueueFile()
        {
            var path = PoolPaths.QueueFile(Root);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        /// <summary>
        /// 递归删除本工作区创建的临时目录；删除失败时吞掉异常，不让测试因清理失败而红。
        /// </summary>
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
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        private static void WriteFile(string path, string json)
        {
            // 需求现在是一个目录，落点比原来深一层，父目录不一定已经在了。
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }
}

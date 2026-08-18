using System;
using System.IO;
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
        /// 在临时目录下创建「创作管线测试-&lt;Guid&gt;」目录，并建出 Schema/基线、Schema/项目、Requirements 三个子目录。
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

        /// <summary>
        /// 把基线 schema JSON 写到 Schema/基线/&lt;实体&gt;.schema.json。
        /// </summary>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <param name="json">基线 schema 的 JSON 文本。</param>
        public void WriteBaselineSchema(string entityName, string json)
        {
            WriteFile(PoolPaths.BaselineSchemaFile(Root, entityName), json);
        }

        /// <summary>
        /// 把项目扩展 schema JSON 写到 Schema/项目/&lt;实体&gt;.扩展.json。
        /// </summary>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <param name="json">项目扩展 schema 的 JSON 文本。</param>
        public void WriteProjectSchema(string entityName, string json)
        {
            WriteFile(PoolPaths.ProjectSchemaFile(Root, entityName), json);
        }

        /// <summary>
        /// 把任意内容写到 Requirements/&lt;文件名&gt;。
        /// </summary>
        /// <param name="fileName">文件名，如「REQ-0001.json」。</param>
        /// <param name="json">文件内容。</param>
        public void WriteRequirement(string fileName, string json)
        {
            WriteFile(Path.Combine(PoolPaths.RequirementsDirectory(Root), fileName), json);
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
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }
}

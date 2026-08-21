using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条安装检查发现：级别（红=必须处理 / 黄=建议处理 / 绿=就绪）、检查项、说明与下一步。</summary>
    public sealed class SetupFinding
    {
        /// <summary>
        /// 构造一条发现。
        /// </summary>
        /// <param name="severity">级别：红 / 黄 / 绿。</param>
        /// <param name="item">检查项名称。</param>
        /// <param name="detail">现状说明。</param>
        /// <param name="nextStep">下一步动作；绿时为空串。</param>
        public SetupFinding(string severity, string item, string detail, string nextStep)
        {
            Severity = severity ?? "";
            Item = item ?? "";
            Detail = detail ?? "";
            NextStep = nextStep ?? "";
        }

        /// <summary>级别：红 / 黄 / 绿。</summary>
        public string Severity { get; }

        /// <summary>检查项名称。</summary>
        public string Item { get; }

        /// <summary>现状说明。</summary>
        public string Detail { get; }

        /// <summary>下一步动作；绿时为空串。</summary>
        public string NextStep { get; }

        /// <summary>渲染成一行清单文字。</summary>
        public string Render()
        {
            var line = $"[{Severity}] {Item}：{Detail}";
            return NextStep.Length == 0 ? line : line + $"　→ {NextStep}";
        }
    }

    /// <summary>
    /// 新项目安装检查：把「装到能用」要配的东西查一遍，逐条报级别与下一步。
    /// 密钥红线（决策 5、78）：只看**键在不在**，永不读值、不报长度前缀；
    /// 非密钥的路径类配置（可执行文件、地址）可以读值做存在性检查。
    /// </summary>
    public static class SetupInspector
    {
        /// <summary>
        /// 跑一遍安装检查。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（绝对路径）。</param>
        public static List<SetupFinding> Inspect(string repositoryRoot)
        {
            var findings = new List<SetupFinding>();
            var settingsFile = Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json");

            InspectSecretFileProtection(repositoryRoot, settingsFile, findings);

            var settings = LocalBridgeSettings.Load(repositoryRoot);
            if (!settings.Loaded)
            {
                findings.Add(new SetupFinding("红", "本机配置", settings.LoadFailureReason, "把 local.json 修成合法 JSON 再来"));
                return findings;
            }

            if (!File.Exists(settingsFile))
            {
                findings.Add(new SetupFinding("红", "本机配置", "local.json 不存在",
                    "跑 setup.init 生成骨架（不含密钥键），再按 Doc/creation-pipeline-user-setup.md 填密钥"));
            }

            foreach (var driverName in EnumerateDriverNames(repositoryRoot))
            {
                InspectDriver(repositoryRoot, driverName, settings, findings);
            }

            InspectUnityEditor(repositoryRoot, findings);
            return findings;
        }

        /// <summary>
        /// 生成 local.json 骨架：从 local.example.json 复制，**剥掉全部密钥键**——
        /// 密钥键的规矩是「键在 = 已配」（决策 78），带着空值的密钥键生出来就是假绿。
        /// 已存在时不动，返回说明。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（绝对路径）。</param>
        public static string InitializeLocalSettings(string repositoryRoot)
        {
            var configDirectory = Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config");
            var targetFile = Path.Combine(configDirectory, "local.json");
            if (File.Exists(targetFile))
            {
                return "local.json 已存在，没动它";
            }

            var exampleFile = Path.Combine(configDirectory, "local.example.json");
            if (!File.Exists(exampleFile))
            {
                return "local.example.json 不存在，生成不了骨架";
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(exampleFile)) as JsonObject;
            }
            catch (JsonException exception)
            {
                return "local.example.json 不是合法 JSON：" + exception.Message;
            }

            if (root == null)
            {
                return "local.example.json 顶层必须是对象";
            }

            var strippedKeys = new List<string>();
            foreach (var secretField in EnumerateDriverNames(repositoryRoot)
                .SelectMany(name => TryLoadDescriptor(repositoryRoot, name)?.SecretFieldNames ?? (IReadOnlyList<string>)Array.Empty<string>())
                .Distinct(StringComparer.Ordinal))
            {
                if (root.Remove(secretField))
                {
                    strippedKeys.Add(secretField);
                }
            }

            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(targetFile, root.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), new UTF8Encoding(false));

            return strippedKeys.Count == 0
                ? "已生成 local.json 骨架"
                : $"已生成 local.json 骨架，剥掉了密钥键：{string.Join("、", strippedKeys)}（拿到值再把键加回去）";
        }

        /// <summary>密钥文件保护：必须被 .gitignore 覆盖，且绝不能已被 git 跟踪。</summary>
        private static void InspectSecretFileProtection(string repositoryRoot, string settingsFile, List<SetupFinding> findings)
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, settingsFile).Replace('\\', '/');
            var tracked = RunGit(repositoryRoot, out var trackedOutput, "ls-files", "--", relativePath);
            if (tracked && trackedOutput.Trim().Length > 0)
            {
                findings.Add(new SetupFinding("红", "密钥文件保护", $"{relativePath} 已被 git 跟踪——密钥会进仓库",
                    "立刻 git rm --cached 它并确认 .gitignore 覆盖，再轮换泄露的密钥"));
                return;
            }

            var ignored = RunGit(repositoryRoot, out _, "check-ignore", "-q", "--", relativePath);
            if (!ignored)
            {
                findings.Add(new SetupFinding("红", "密钥文件保护", $"{relativePath} 不在 .gitignore 覆盖里",
                    "把这条路径加进 .gitignore，否则密钥迟早被提交"));
            }
            else
            {
                findings.Add(new SetupFinding("绿", "密钥文件保护", $"{relativePath} 被 .gitignore 覆盖且未被跟踪", ""));
            }
        }

        /// <summary>逐 driver 检查：密钥键在不在、配置节在不在、本地可执行文件在不在、供给过没有。</summary>
        private static void InspectDriver(string repositoryRoot, string driverName, LocalBridgeSettings settings, List<SetupFinding> findings)
        {
            var descriptor = TryLoadDescriptor(repositoryRoot, driverName);
            if (descriptor == null)
            {
                findings.Add(new SetupFinding("黄", $"下游 {driverName}", "driver.json 读不出来", "修好它或删掉这个 driver 目录"));
                return;
            }

            var missingSecrets = descriptor.SecretFieldNames
                .Where(field => !settings.TryGetSecret(field, out var value) || value.Length == 0)
                .ToList();
            if (missingSecrets.Count > 0)
            {
                findings.Add(new SetupFinding("红", $"下游 {driverName}", $"密钥键缺：{string.Join("、", missingSecrets)}",
                    "按 Doc/creation-pipeline-user-setup.md 第二节去拿，填进 local.json 顶层"));
            }

            if (!settings.TryGetDriverConfiguration(driverName, out var configuration))
            {
                findings.Add(new SetupFinding("红", $"下游 {driverName}", "local.json 没有它的「下游配置」节",
                    "照 local.example.json 里的同名节抄一份进来"));
            }
            else
            {
                // 非密钥的路径类配置读值做存在性检查（可执行文件路径不是密钥）。
                if (configuration.TryGetProperty("可执行文件", out var executable)
                    && executable.ValueKind == JsonValueKind.String
                    && !File.Exists(executable.GetString() ?? ""))
                {
                    findings.Add(new SetupFinding("红", $"下游 {driverName}", "「可执行文件」指向的路径不存在",
                        "装好工具后把真实路径填进「下游配置." + driverName + ".可执行文件」"));
                }
            }

            var fingerprintFile = Path.Combine(repositoryRoot, "_Generated", "Bridges", driverName, "fingerprint.json");
            if (!File.Exists(fingerprintFile))
            {
                findings.Add(new SetupFinding("黄", $"下游 {driverName}", "还没供给过（没有指纹文件）",
                    $"跑一次 bridge.provision --Driver {driverName}"));
            }

            if (missingSecrets.Count == 0 && settings.TryGetDriverConfiguration(driverName, out _) && File.Exists(fingerprintFile))
            {
                findings.Add(new SetupFinding("绿", $"下游 {driverName}", "密钥键齐、配置节在、已供给", ""));
            }
        }

        /// <summary>Unity 编辑器：按 ProjectVersion.txt 的版本探测常见装机路径。</summary>
        private static void InspectUnityEditor(string repositoryRoot, List<SetupFinding> findings)
        {
            var versionFile = Path.Combine(repositoryRoot, "UnityProject", "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(versionFile))
            {
                findings.Add(new SetupFinding("黄", "Unity 编辑器", "ProjectVersion.txt 不存在，判不了版本", ""));
                return;
            }

            var versionLine = File.ReadLines(versionFile).FirstOrDefault(line => line.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
            var version = versionLine?.Split(':', 2)[1].Trim() ?? "";
            if (version.Length == 0)
            {
                findings.Add(new SetupFinding("黄", "Unity 编辑器", "ProjectVersion.txt 里读不到版本号", ""));
                return;
            }

            var candidates = UnityEditorCandidates(version);
            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null)
            {
                findings.Add(new SetupFinding("绿", "Unity 编辑器", $"{version} 在 {found}", ""));
            }
            else
            {
                findings.Add(new SetupFinding("红", "Unity 编辑器", $"按版本 {version} 在常见装机路径都没找到编辑器",
                    "用 Unity Hub 装这个版本，或跑 unity-cmd.ps1 时用 -UnityExecutable 指路径"));
            }
        }

        /// <summary>按版本拼出常见装机路径（D 盘惯例 + Unity Hub 默认位置）。</summary>
        /// <param name="version">Unity 完整版本号，如 6000.3.11f1。</param>
        public static IReadOnlyList<string> UnityEditorCandidates(string version)
        {
            return new[]
            {
                $"D:/Unity/Editor/{version}/Unity.exe",
                $"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe",
                $"D:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe"
            };
        }

        /// <summary>枚举 Bridges 下带 driver.json 的目录名，序数序。</summary>
        private static IEnumerable<string> EnumerateDriverNames(string repositoryRoot)
        {
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            if (!Directory.Exists(bridgesDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(bridgesDirectory)
                .Where(directory => File.Exists(Path.Combine(directory, "driver.json")))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        private static BridgeDriverDescriptor TryLoadDescriptor(string repositoryRoot, string driverName)
        {
            try
            {
                return BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>跑一条 git 命令；退出码 0 返回 true。git 不在或超时按 false 处理。</summary>
        private static bool RunGit(string repositoryRoot, out string standardOutput, params string[] arguments)
        {
            standardOutput = "";
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8
                };
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                standardOutput = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(30_000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}

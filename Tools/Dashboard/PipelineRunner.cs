using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.Dashboard
{
    /// <summary>一条流水线跑完的结论。</summary>
    public sealed class PipelineRunResult
    {
        /// <summary>用成功与否、完成步骤数、失败步骤与消息构造结论。</summary>
        /// <param name="isSuccess">是否全部步骤都成功（或被跳过）。</param>
        /// <param name="completedStepCount">跑完的步骤数，含被跳过的步骤。</param>
        /// <param name="failedStepName">失败步骤的名称，全部成功时为空串。</param>
        /// <param name="message">结论的面向人消息。</param>
        public PipelineRunResult(bool isSuccess, int completedStepCount, string failedStepName, string message)
        {
            IsSuccess = isSuccess;
            CompletedStepCount = completedStepCount;
            FailedStepName = failedStepName;
            Message = message;
        }

        /// <summary>是否全部步骤都成功（或被跳过）。</summary>
        public bool IsSuccess { get; }

        /// <summary>跑完的步骤数（含被跳过的步骤）。</summary>
        public int CompletedStepCount { get; }

        /// <summary>失败步骤的名称，全部成功时为空串。</summary>
        public string FailedStepName { get; }

        /// <summary>结论的面向人消息。</summary>
        public string Message { get; }
    }

    /// <summary>流水线执行器：按顺序跑各步骤，逐行把输出交给回调，前一步失败就不做后一步。</summary>
    public sealed class PipelineRunner
    {
        // timeout 命令约定的超时退出码：进程被包装脚本超时必杀时，脚本会原样带出 124。
        private const int TimeoutExitCode = 124;

        private static readonly JsonSerializerOptions LogOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _workingDirectory;

        private readonly Action<string> _onLogLine;

        /// <summary>以工作目录与逐行回调构造。</summary>
        /// <param name="workingDirectory">各步骤的工作目录，通常是模板根。</param>
        /// <param name="onLogLine">每产出一行输出就被调用一次，传 null 时当作空操作。</param>
        public PipelineRunner(string workingDirectory, Action<string> onLogLine)
        {
            _workingDirectory = workingDirectory;
            _onLogLine = onLogLine;
        }

        /// <summary>跑一条流水线。skipStepsRequiringUnity 为 true 时跳过需要 Unity 的步骤。</summary>
        /// <param name="pipeline">要跑的流水线。</param>
        /// <param name="skipStepsRequiringUnity">为 true 时跳过「需要Unity」的步骤。</param>
        public PipelineRunResult Run(PipelineDefinition pipeline, bool skipStepsRequiringUnity)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            var steps = pipeline.Steps ?? Array.Empty<PipelineStep>();
            var completedStepCount = 0;

            foreach (var step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                if (step.RequiresUnity && skipStepsRequiringUnity)
                {
                    // 编辑器占着工程时 batchmode 打不开同一个工程，跳过才能让其余步骤照常验证。
                    Emit("信息", pipeline.Name, step.Name, "已跳过（需要 Unity，当前环境不启 Unity）");
                    completedStepCount++;
                    continue;
                }

                Emit("信息", pipeline.Name, step.Name, "开始");

                var exitCode = RunStep(pipeline.Name, step);

                if (exitCode == 0)
                {
                    Emit("信息", pipeline.Name, step.Name, $"结束，退出码 {exitCode}");
                    completedStepCount++;
                    continue;
                }

                Emit("错误", pipeline.Name, step.Name, $"结束，退出码 {exitCode}");
                var message = exitCode == TimeoutExitCode
                    ? $"流水线「{pipeline.Name}」在步骤「{step.Name}」超时（退出码 124）"
                    : $"流水线「{pipeline.Name}」在步骤「{step.Name}」失败，退出码 {exitCode}";
                return new PipelineRunResult(false, completedStepCount, step.Name, message);
            }

            return new PipelineRunResult(
                true,
                completedStepCount,
                string.Empty,
                $"流水线「{pipeline.Name}」完成，共 {completedStepCount} 步");
        }

        private int RunStep(string pipelineName, PipelineStep step)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = step.FileName,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8
            };

            if (step.Arguments != null)
            {
                foreach (var argument in step.Arguments)
                {
                    if (!string.IsNullOrEmpty(argument))
                    {
                        startInfo.ArgumentList.Add(argument);
                    }
                }
            }

            Process process;
            try
            {
                process = new Process { StartInfo = startInfo };
                process.Start();
            }
            catch (Win32Exception exception)
            {
                // 程序名不存在或无法启动时当失败处理，不把异常抛给调用方：流水线照常给出结论。
                Emit("错误", pipelineName, step.Name, $"无法启动程序 {step.FileName}：{exception.Message}");
                return -1;
            }

            using (process)
            {
                // 用异步事件逐行收集输出，避免先读尽标准输出再读错误流造成缓冲区死锁。
                process.OutputDataReceived += (sender, eventArgs) => EmitOutput(pipelineName, step.Name, eventArgs.Data);
                process.ErrorDataReceived += (sender, eventArgs) => EmitOutput(pipelineName, step.Name, eventArgs.Data);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return process.ExitCode;
            }
        }

        private void EmitOutput(string pipelineName, string stepName, string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Emit("输出", pipelineName, stepName, line);
        }

        private void Emit(string level, string pipelineName, string stepName, string content)
        {
            if (_onLogLine == null)
            {
                return;
            }

            var payload = new Dictionary<string, string>
            {
                ["级别"] = level,
                ["流水线"] = pipelineName,
                ["步骤"] = stepName,
                ["内容"] = content
            };

            _onLogLine(JsonSerializer.Serialize(payload, LogOptions));
        }
    }
}

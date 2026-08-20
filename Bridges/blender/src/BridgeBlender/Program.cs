using System;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Blender
{
    /// <summary>
    /// 加工站桥：stdin 收一份协议请求 JSON，stdout 出一份协议响应 JSON，退出码 0/非 0。
    /// 铁律：stdout 上只许有那一份 JSON，一个字节都不许多——日志、进度、警告一律走 stderr，
    /// 否则调用方拿到的是「JSON 解析失败」这种查不到根因的错。
    /// </summary>
    public static class Program
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>
        /// 入口：读 stdin 到 EOF → 解析请求 → 按动作分发 → 响应写 stdout → 按成功与否给退出码。
        /// 未知动作返回错误码「未知动作」的失败响应，不是崩溃；整个入口用 try/catch 兜住，
        /// 任何异常都转成失败响应（否则调用方拿到的是空 stdout）。
        /// </summary>
        /// <param name="args">命令行参数，本桥不消费。</param>
        public static int Main(string[] args)
        {
            try
            {
                var input = Console.In.ReadToEnd();
                if (!BridgeRequest.TryParse(input, out var request, out var reason))
                {
                    WriteResponse(BridgeResponse.Failure(ContractVersion, "请求不合协议", reason, retryable: false));
                    return 1;
                }

                BridgeResponse response;
                switch (request.Action)
                {
                    case "caps":
                        response = BlenderRunner.RunCaps(request);
                        break;
                    case "process":
                        response = BlenderRunner.RunProcess(request);
                        break;
                    default:
                        response = BridgeResponse.Failure(ContractVersion, "未知动作", $"不认识动作「{request.Action}」，本桥只支持 caps / process", retryable: false);
                        break;
                }

                WriteResponse(response);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BridgeBlender 内部错误：" + exception);
                WriteResponse(BridgeResponse.Failure(ContractVersion, "内部错误", exception.Message, retryable: false));
                return 1;
            }
        }

        /// <summary>把响应写 stdout（唯一允许出现在 stdout 上的内容），日志走 stderr。</summary>
        private static void WriteResponse(BridgeResponse response)
        {
            Console.Out.WriteLine(response.ToJson());
            Console.Error.WriteLine("BridgeBlender 处理完成，成功=" + response.Succeeded);
        }
    }
}

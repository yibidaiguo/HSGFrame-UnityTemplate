using System;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 飞书桥：stdin 收一份协议请求 JSON，stdout 出一份协议响应 JSON，退出码 0/非 0。
    /// 与 BridgeOaicompat 同构（线上形态 + HTTP + 密钥进 header/请求体）。
    /// 铁律：stdout 上只许有那一份 JSON，一个字节都不许多——日志、进度、警告一律走 stderr，
    /// 否则调用方拿到的是「JSON 解析失败」这种查不到根因的错。
    /// 动作：apply（幂等建表）、card（发一张选片卡）。
    /// </summary>
    public static class Program
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>
        /// 入口：读 stdin 到 EOF → 解析请求 → 按动作分发 → 响应写 stdout → 按成功与否给退出码。
        /// 未知动作返回错误码「未知动作」的失败响应，不是崩溃；整个入口用 try/catch 兜住，
        /// 任何异常都转成失败响应（否则调用方拿到的是空 stdout）。
        /// 密钥（飞书应用密钥、token）只在请求信封的「配置」与 HTTP 请求里，绝不出现在任何日志与文案。
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
                    case "apply":
                        response = TableProvisioner.RunApply(request);
                        break;
                    case "card":
                        response = CardSender.SendCard(request);
                        break;
                    default:
                        response = BridgeResponse.Failure(ContractVersion, "未知动作", $"不认识动作「{request.Action}」，本桥只支持 apply / card", retryable: false);
                        break;
                }

                WriteResponse(response);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BridgeFeishu 内部错误：" + exception.Message);
                WriteResponse(BridgeResponse.Failure(ContractVersion, "内部错误", exception.Message, retryable: false));
                return 1;
            }
        }

        /// <summary>把响应写 stdout（唯一允许出现在 stdout 上的内容），日志走 stderr。</summary>
        private static void WriteResponse(BridgeResponse response)
        {
            Console.Out.WriteLine(response.ToJson());
            Console.Error.WriteLine("BridgeFeishu 处理完成，成功=" + response.Succeeded);
        }
    }
}

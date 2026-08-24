using System;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Tripocli
{
    /// <summary>
    /// 模型生成桥（本地 CLI 形态）：stdin 收一份协议请求 JSON，stdout 出一份协议响应 JSON。
    ///
    /// 与 <c>Bridges/tripo</c>（线上 HTTP 形态）**说同一套动作、同一套载荷、同一套错误码**，
    /// 只是把「发 HTTP 请求 + 轮询 + 下载」换成「起一次 tripo 命令行」。
    /// 两个 driver 并列挂在「模型生成」这个 port 下，走哪一条由域路由的候选顺序说了算
    /// （面板「路由」页可以改，不用改代码）。
    ///
    /// **密钥不经这个桥**：CLI 自己 `tripo login` 过一次，钥匙在 <c>~/.tripo/config.json</c>。
    /// 所以 driver.json 的「密钥字段」是空的——把密钥又抄一份进 local.json 会有两个真相，
    /// 而人换了账号只会去 `tripo login`，不会想起还有一份抄件。
    ///
    /// 铁律：stdout 上只许有那一份 JSON，一个字节都不许多——日志、进度、警告一律走 stderr。
    /// </summary>
    public static class Program
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>
        /// 入口：读 stdin 到 EOF → 解析请求 → 按动作分发 → 响应写 stdout → 按成功与否给退出码。
        /// 未知动作返回错误码「未知动作」的失败响应，不是崩溃。
        /// </summary>
        /// <param name="args">命令行参数，本桥不消费。</param>
        public static int Main(string[] args)
        {
            // 三条流先钉成 UTF-8，再碰 stdin 一个字节——协议 JSON 的键是中文，
            // 编码没对上时收回来就是乱码，而报错完全指不到编码上。
            BridgeProtocolConsole.PinUtf8();

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
                    case "generate":
                        response = TripoCliRunner.RunGenerate(request);
                        break;
                    case "balance":
                        response = TripoCliRunner.RunBalance(request);
                        break;
                    case "caps":
                        response = TripoCliRunner.RunCaps(request);
                        break;
                    default:
                        response = BridgeResponse.Failure(
                            ContractVersion, "未知动作",
                            $"不认识动作「{request.Action}」，本桥支持 generate / balance / caps",
                            retryable: false);
                        break;
                }

                WriteResponse(response);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BridgeTripocli 内部错误：" + exception.Message);
                WriteResponse(BridgeResponse.Failure(ContractVersion, "内部错误", exception.Message, retryable: false));
                return 1;
            }
        }

        /// <summary>把响应写 stdout（唯一允许出现在 stdout 上的内容），日志走 stderr。</summary>
        private static void WriteResponse(BridgeResponse response)
        {
            Console.Out.WriteLine(response.ToJson());
            Console.Error.WriteLine("BridgeTripocli 处理完成，成功=" + response.Succeeded);
        }
    }
}

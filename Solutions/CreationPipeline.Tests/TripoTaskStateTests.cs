using Xunit;
using Template.Bridges.Tripo;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// tripo 任务状态机与错误码映射测试：都是纯函数，脱离网络可测。
    /// 要点：终态判定正确；未知状态必须当失败并把原样字符串带出来（决策 42——
    /// 不许默默 continue 轮询到超时）；错误码映射把「积分用完」说成额度不足而不是代码坏了。
    /// </summary>
    public class TripoTaskStateTests
    {
        /// <summary>queued / running 是非终态，要继续轮询。</summary>
        [Theory]
        [InlineData("queued")]
        [InlineData("running")]
        public void OngoingStatesAreNotFinal(string status)
        {
            var result = TripoTaskState.Classify(status);

            Assert.False(result.IsFinal);
            Assert.False(result.Succeeded);
            Assert.Equal(status, result.StatusText);
        }

        /// <summary>success 是终态且成功。</summary>
        [Fact]
        public void SuccessIsFinalAndSucceeded()
        {
            var result = TripoTaskState.Classify("success");

            Assert.True(result.IsFinal);
            Assert.True(result.Succeeded);
            Assert.Equal("success", result.StatusText);
        }

        /// <summary>failed / banned / expired / cancelled / unknown 都是终态失败。</summary>
        [Theory]
        [InlineData("failed")]
        [InlineData("banned")]
        [InlineData("expired")]
        [InlineData("cancelled")]
        [InlineData("unknown")]
        public void FinalizedFailureStatesAreFinalAndFailed(string status)
        {
            var result = TripoTaskState.Classify(status);

            Assert.True(result.IsFinal);
            Assert.False(result.Succeeded);
            Assert.Equal(status, result.StatusText);
        }

        /// <summary>不认识的字符串必须当失败，原样带出——不许默默当成还在跑。</summary>
        [Theory]
        [InlineData("SUCCESS")]
        [InlineData("processing")]
        [InlineData("done")]
        [InlineData("超时")]
        public void UnknownStatusIsFailureWithOriginalText(string status)
        {
            var result = TripoTaskState.Classify(status);

            Assert.True(result.IsFinal);
            Assert.False(result.Succeeded);
            Assert.Equal(status, result.StatusText);
            Assert.Contains(status, result.HumanText);
        }

        /// <summary>空状态字符串也当失败，不许继续轮询。</summary>
        [Fact]
        public void EmptyStatusIsFailure()
        {
            var result = TripoTaskState.Classify("");

            Assert.True(result.IsFinal);
            Assert.False(result.Succeeded);
            Assert.Equal("", result.StatusText);
        }

        /// <summary>null 状态按空串处理，当失败。</summary>
        [Fact]
        public void NullStatusIsFailure()
        {
            var result = TripoTaskState.Classify(null);

            Assert.True(result.IsFinal);
            Assert.False(result.Succeeded);
        }

        /// <summary>401/403 → 凭据无效，不可重试。</summary>
        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void UnauthorizedMapsToInvalidCredential(int statusCode)
        {
            var error = TripoHttpErrorMapper.Map(statusCode, "{\"message\":\"nope\"}");

            Assert.Equal("凭据无效", error.Code);
            Assert.False(error.Retryable);
        }

        /// <summary>tripo 的 403 + code 2010 是积分不足（错误码表），要映射成额度不足不是凭据无效。</summary>
        [Fact]
        public void ForbiddenWithCreditCodeMapsToOutOfQuota()
        {
            var error = TripoHttpErrorMapper.Map(403, "{\"code\":2010,\"message\":\"You don't have enough credit to create this task\"}");

            Assert.Equal("额度不足", error.Code);
            Assert.False(error.Retryable);
            Assert.Contains("积分", error.HumanText);
            Assert.Contains("不是代码坏了", error.HumanText);
        }

        /// <summary>tripo 的 403 + code 1005 是无权限，映射成凭据无效。</summary>
        [Fact]
        public void ForbiddenWithPermissionCodeMapsToInvalidCredential()
        {
            var error = TripoHttpErrorMapper.Map(403, "{\"code\":1005,\"message\":\"You are not allowed to access this resource\"}");

            Assert.Equal("凭据无效", error.Code);
            Assert.False(error.Retryable);
        }

        /// <summary>HTTP 402 → 额度不足，人话要写清是积分用完不是代码坏了。</summary>
        [Fact]
        public void PaymentRequiredMapsToOutOfQuota()
        {
            var error = TripoHttpErrorMapper.Map(402, "{\"message\":\"payment required\"}");

            Assert.Equal("额度不足", error.Code);
            Assert.False(error.Retryable);
            Assert.Contains("积分", error.HumanText);
            Assert.Contains("不是代码坏了", error.HumanText);
        }

        /// <summary>429 → 限流，可重试。</summary>
        [Fact]
        public void RateLimitedMapsToThrottledAndRetryable()
        {
            var error = TripoHttpErrorMapper.Map(429, "{\"message\":\"too many requests\"}");

            Assert.Equal("限流", error.Code);
            Assert.True(error.Retryable);
        }

        /// <summary>服务端 message 里带 credit/balance 这类词 → 额度不足，不管 HTTP 码。</summary>
        [Theory]
        [InlineData("{\"message\":\"insufficient credits\"}")]
        [InlineData("{\"message\":\"balance is not enough\"}")]
        [InlineData("{\"message\":\"exceeded your quota\"}")]
        [InlineData("{\"message\":\"积分不足\"}")]
        public void CreditLikeMessageMapsToOutOfQuota(string responseText)
        {
            var error = TripoHttpErrorMapper.Map(400, responseText);

            Assert.Equal("额度不足", error.Code);
            Assert.False(error.Retryable);
            Assert.Contains("积分", error.HumanText);
        }

        /// <summary>其余错误 → 下游报错，带服务端 message；5xx 可重试、4xx 不可重试。</summary>
        [Theory]
        [InlineData(400, false)]
        [InlineData(404, false)]
        [InlineData(500, true)]
        [InlineData(502, true)]
        public void OtherErrorsMapToDownstreamErrorWithMessage(int statusCode, bool retryable)
        {
            var error = TripoHttpErrorMapper.Map(statusCode, "{\"message\":\"server exploded\"}");

            Assert.Equal("下游报错", error.Code);
            Assert.Equal(retryable, error.Retryable);
            Assert.Contains("server exploded", error.HumanText);
        }

        /// <summary>响应体不是 JSON 也要能映射，给占位文案而不是崩。</summary>
        [Fact]
        public void NonJsonErrorBodyStillMaps()
        {
            var error = TripoHttpErrorMapper.Map(500, "<html>oops</html>");

            Assert.Equal("下游报错", error.Code);
            Assert.True(error.Retryable);
        }

        /// <summary>v3 的服务端 code 优先于 HTTP 状态码：同一个 403 底下 2010 与 1005 是两件事。</summary>
        [Theory]
        [InlineData(403, "{\"code\":2010,\"message\":\"You don't have enough credit to create this task\"}", "额度不足")]
        [InlineData(403, "{\"code\":1005,\"message\":\"no permission\"}", "凭据无效")]
        [InlineData(400, "{\"code\":1004,\"message\":\"invalid model\"}", "请求不合协议")]
        [InlineData(404, "{\"code\":4001,\"message\":\"No endpoint found: POST /v3/upload\"}", "下游报错")]
        [InlineData(404, "{\"code\":2001,\"message\":\"The task is not found\"}", "下游报错")]
        public void ServerCodeWinsOverHttpStatus(int statusCode, string responseText, string expectedCode)
        {
            var error = TripoHttpErrorMapper.Map(statusCode, responseText);

            Assert.Equal(expectedCode, error.Code);
            Assert.False(error.Retryable);
        }

        /// <summary>1004 的人话必须把矛头指向「我们发的形状不对」，不是账号问题——否则又要去换 key 查一天。</summary>
        [Fact]
        public void ParameterErrorSaysItIsOurRequestShape()
        {
            var error = TripoHttpErrorMapper.Map(400, "{\"code\":1004,\"message\":\"invalid model 'tripo-v3.1'\"}");

            Assert.Contains("形状不对", error.HumanText);
            Assert.Contains("不是账号问题", error.HumanText);
            Assert.Contains("tripo-v3.1", error.HumanText);
        }

        /// <summary>4001 的人话必须点名 base URL 或版本写错了——这正是待办 3 那次踩的坑。</summary>
        [Fact]
        public void EndpointNotFoundSaysBaseUrlIsWrong()
        {
            var error = TripoHttpErrorMapper.Map(404, "{\"code\":4001,\"message\":\"No endpoint found\"}");

            Assert.Contains("base URL", error.HumanText);
        }

        /// <summary>模型版本只认下游列出来的四个值；空串给缺省值。</summary>
        [Theory]
        [InlineData("P1-20260311")]
        [InlineData("v2.5-20250123")]
        [InlineData("v3.0-20250812")]
        [InlineData("v3.1-20260211")]
        public void AllowedModelVersionsPassThrough(string version)
        {
            Assert.Equal(version, TripoClient.NormalizeModelVersion(version));
        }

        /// <summary>空串给缺省模型版本。</summary>
        [Fact]
        public void EmptyModelVersionFallsBackToDefault()
        {
            Assert.Equal(TripoClient.DefaultModelVersion, TripoClient.NormalizeModelVersion(""));
        }

        /// <summary>
        /// 不在上次实证快照里的值**照发，不拦**：清单现在是探出来的，
        /// 本机那份快照随时会过期，拿它拦人只会把「下游新上的模型」挡在门外。
        /// 真不合法由服务端回 1004，报错里带着此刻的 allowed values。
        /// </summary>
        [Fact]
        public void UnknownModelVersionPassesThroughForServerToJudge()
        {
            Assert.Equal("tripo-v3.1", TripoClient.NormalizeModelVersion("tripo-v3.1"));
            Assert.Equal("下游明天才上的模型", TripoClient.NormalizeModelVersion("  下游明天才上的模型  "));
        }

        /// <summary>text-to-model 提交体是 v3 形状：有 model，没有 v2 的 type / model_version。</summary>
        [Fact]
        public void TextSubmitBodyUsesVersionThreeShape()
        {
            using var client = new TripoClient("https://openapi.tripo3d.ai/v3", "not-a-real-key", 60, "v3.0-20250812");

            var body = client.BuildSubmitBody("a small wooden crate");

            Assert.Contains("\"model\":\"v3.0-20250812\"", body);
            Assert.Contains("\"prompt\":\"a small wooden crate\"", body);
            Assert.DoesNotContain("model_version", body);
            Assert.DoesNotContain("\"type\":\"text_to_model\"", body);
        }

        /// <summary>image-to-model 提交体带 file={type,url}；类型空串按 png。</summary>
        [Theory]
        [InlineData("", "png")]
        [InlineData("JPG", "jpg")]
        [InlineData(".png", "png")]
        public void ImageSubmitBodyCarriesFileObject(string given, string expected)
        {
            using var client = new TripoClient("https://openapi.tripo3d.ai/v3", "not-a-real-key", 60, "v3.0-20250812");

            var body = client.BuildImageSubmitBody("https://example.invalid/a.png", given);

            Assert.Contains("\"file\":{\"type\":\"" + expected + "\"", body);
            Assert.Contains("\"url\":\"https://example.invalid/a.png\"", body);
        }
    }
}

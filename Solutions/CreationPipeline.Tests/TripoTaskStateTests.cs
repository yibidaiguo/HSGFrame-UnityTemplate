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
    }
}

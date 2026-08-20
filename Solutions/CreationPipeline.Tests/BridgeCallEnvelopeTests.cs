using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>下游调用协议信封（请求/响应/错误）的序列化与解析测试。</summary>
    public class BridgeCallEnvelopeTests
    {
        /// <summary>请求信封来回序列化：字段逐一还原，配置与载荷原样。</summary>
        [Fact]
        public void RequestRoundTrips()
        {
            var configuration = JsonDocument.Parse("{\"可执行文件\":\"D:/Tools/Blender/blender.exe\",\"超时秒\":900}").RootElement.Clone();
            var payload = JsonDocument.Parse("{\"输出路径\":\"D:/out/caps.json\"}").RootElement.Clone();

            var request = new BridgeRequest("1.0.0", "模型加工", "caps", configuration, payload);
            var json = request.ToJson();

            Assert.True(BridgeRequest.TryParse(json, out var parsed, out var reason), reason);
            Assert.Equal("1.0.0", parsed.ContractVersion);
            Assert.Equal("模型加工", parsed.Port);
            Assert.Equal("caps", parsed.Action);
            Assert.Equal("D:/Tools/Blender/blender.exe", parsed.Configuration.GetProperty("可执行文件").GetString());
            Assert.Equal(900, parsed.Configuration.GetProperty("超时秒").GetInt32());
            Assert.Equal("D:/out/caps.json", parsed.Payload.GetProperty("输出路径").GetString());
        }

        /// <summary>成功响应来回序列化：载荷原样。</summary>
        [Fact]
        public void SuccessResponseRoundTrips()
        {
            var payload = JsonDocument.Parse("{\"输出模型\":\"D:/out/prop_suzanne.gltf\"}").RootElement.Clone();
            var response = BridgeResponse.Success("1.0.0", payload);
            var json = response.ToJson();

            Assert.True(BridgeResponse.TryParse(json, out var parsed, out var reason), reason);
            Assert.True(parsed.Succeeded);
            Assert.Equal("1.0.0", parsed.ContractVersion);
            Assert.Equal("D:/out/prop_suzanne.gltf", parsed.Payload.GetProperty("输出模型").GetString());
            Assert.Null(parsed.Error);
        }

        /// <summary>失败响应来回序列化：错误三字段齐全。</summary>
        [Fact]
        public void FailureResponseRoundTrips()
        {
            var response = BridgeResponse.Failure("1.0.0", "下游不可达", "起 Blender 失败", retryable: true);
            var json = response.ToJson();

            Assert.True(BridgeResponse.TryParse(json, out var parsed, out var reason), reason);
            Assert.False(parsed.Succeeded);
            Assert.NotNull(parsed.Error);
            Assert.Equal("下游不可达", parsed.Error.Code);
            Assert.Equal("起 Blender 失败", parsed.Error.HumanText);
            Assert.True(parsed.Error.Retryable);
        }

        /// <summary>坏 JSON 给出行列可读的原因，不许只说「JSON 无效」。</summary>
        [Fact]
        public void BadJsonGivesReadableReason()
        {
            Assert.False(BridgeRequest.TryParse("{ 不是 JSON", out _, out var requestReason));
            Assert.Contains("不是合法 JSON", requestReason);

            Assert.False(BridgeResponse.TryParse("{\"契约版本\":\"1.0.0\",", out _, out var responseReason));
            Assert.Contains("不是合法 JSON", responseReason);
        }

        /// <summary>缺必填键时原因点出缺的是哪个键。</summary>
        [Fact]
        public void MissingFieldGivesReadableReason()
        {
            Assert.False(BridgeRequest.TryParse("{\"契约版本\":\"1.0.0\",\"port\":\"模型加工\",\"动作\":\"caps\"}", out _, out var reason));
            Assert.Contains("配置", reason);

            Assert.False(BridgeRequest.TryParse("{\"契约版本\":\"1.0.0\",\"port\":\"模型加工\",\"动作\":\"caps\",\"配置\":{}}", out _, out var missingPayloadReason));
            Assert.Contains("载荷", missingPayloadReason);
        }

        /// <summary>类型不对时原因点出字段与期望类型。</summary>
        [Fact]
        public void WrongTypeGivesReadableReason()
        {
            Assert.False(BridgeRequest.TryParse("{\"契约版本\":1.0,\"port\":\"模型加工\",\"动作\":\"caps\",\"配置\":{},\"载荷\":{}}", out _, out var reason));
            Assert.Contains("契约版本", reason);
            Assert.Contains("字符串", reason);
        }

        /// <summary>错误信封三字段：错误码 / 人话 / 可重试，缺一不可。</summary>
        [Fact]
        public void ErrorEnvelopeRequiresAllThreeFields()
        {
            Assert.True(BridgeError.TryParse("{\"错误码\":\"超时\",\"人话\":\"太慢\",\"可重试\":true}", out var error, out var reason), reason);
            Assert.Equal("超时", error.Code);
            Assert.Equal("太慢", error.HumanText);
            Assert.True(error.Retryable);

            Assert.False(BridgeError.TryParse("{\"错误码\":\"超时\",\"人话\":\"太慢\"}", out _, out var missingReason));
            Assert.Contains("可重试", missingReason);
        }

        /// <summary>成功响应缺「载荷」解析失败；失败响应缺「错误」解析失败。</summary>
        [Fact]
        public void ResponseShapeIsEnforced()
        {
            Assert.False(BridgeResponse.TryParse("{\"契约版本\":\"1.0.0\",\"成功\":true}", out _, out var successReason));
            Assert.Contains("载荷", successReason);

            Assert.False(BridgeResponse.TryParse("{\"契约版本\":\"1.0.0\",\"成功\":false}", out _, out var failureReason));
            Assert.Contains("错误", failureReason);
        }
    }
}

using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 采集服务器连接参数解析测试。
/// </summary>
public class ServerConnectionSettingsTests
{
    private static ServerConnectionSettings Create(params (string Key, string Value)[] config)
    {
        Dictionary<string, string> dictionary = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string value) in config)
        {
            dictionary[key] = value;
        }

        return new ServerConnectionSettings(
            Guid.NewGuid(), "HIST-01", "历史库服务器", "https://hist.example.com/", dictionary);
    }

    [Fact]
    public void ToHistorianOptions_Should_Carry_All_Configured_Values()
    {
        ServerConnectionSettings settings = Create(
            ("clientId", "client-001"),
            ("clientSecret", "secret-xyz"),
            ("useMockResponses", "true"),
            ("oauthTokenPath", "/oauth/token"),
            ("currentValuePath", "/api/value"),
            ("rawDataPathPrefix", "/api/raw"),
            ("timeoutSeconds", "45"),
            ("ignoreCertificateErrors", "false"));

        HistoryApiConnectionOptions options = settings.ToHistorianOptions();

        Assert.Equal("https://hist.example.com/", options.BaseAddress);
        Assert.Equal("client-001", options.ClientId);
        Assert.Equal("secret-xyz", options.ClientSecret);
        Assert.True(options.UseMockResponses);
        Assert.Equal("/oauth/token", options.OAuthTokenPath);
        Assert.Equal("/api/value", options.CurrentValuePath);
        Assert.Equal("/api/raw", options.RawDataPathPrefix);
        Assert.Equal(45, options.RequestTimeoutSeconds);
        Assert.False(options.IgnoreServerCertificateErrors);
    }

    [Fact]
    public void ToHistorianOptions_With_Missing_Config_Should_Yield_Empty_Strings_And_Nulls()
    {
        HistoryApiConnectionOptions options = Create().ToHistorianOptions();

        // 缺失字段退化为空串/null，而不是让调用方拿到 null 引用异常
        Assert.Equal(string.Empty, options.ClientId);
        Assert.Equal(string.Empty, options.ClientSecret);
        Assert.Null(options.UseMockResponses);
        Assert.Null(options.RequestTimeoutSeconds);
        Assert.Null(options.IgnoreServerCertificateErrors);
    }

    [Fact]
    public void ToHistorianOptions_Should_Trim_Whitespace_From_Values()
    {
        ServerConnectionSettings settings = Create(
            ("clientId", "  client-002  "),
            ("clientSecret", "\tsecret\t"));

        HistoryApiConnectionOptions options = settings.ToHistorianOptions();

        // 配置来自数据库与文本框，首尾空白必须被清理，否则会拼进 Authorization 头导致认证失败
        Assert.Equal("client-002", options.ClientId);
        Assert.Equal("secret", options.ClientSecret);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(" false ", false)]
    public void ToHistorianOptions_Should_Parse_Boolean_Case_Insensitively(string raw, bool expected)
    {
        HistoryApiConnectionOptions options = Create(("useMockResponses", raw)).ToHistorianOptions();

        Assert.Equal(expected, options.UseMockResponses);
    }

    [Theory]
    [InlineData("不是布尔值")]
    [InlineData("")]
    [InlineData("2")]
    public void ToHistorianOptions_With_Invalid_Boolean_Should_Fall_Back_To_Null(string raw)
    {
        // 非法值不能默默变成 false——那会让生产环境意外切到 mock 响应
        HistoryApiConnectionOptions options = Create(("useMockResponses", raw)).ToHistorianOptions();

        Assert.Null(options.UseMockResponses);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("不是数字")]
    [InlineData("")]
    public void ToHistorianOptions_With_Invalid_Timeout_Should_Fall_Back_To_Null(string raw)
    {
        HistoryApiConnectionOptions options = Create(("timeoutSeconds", raw)).ToHistorianOptions();

        // 0 或负数不是合法超时，回退 null 让调用方使用默认超时，而不是立即超时
        Assert.Null(options.RequestTimeoutSeconds);
    }

    [Fact]
    public void ToHistorianOptions_Should_Look_Up_Keys_Case_Insensitively()
    {
        ServerConnectionSettings settings = Create(("ClientId", "upper-001"), ("TIMEOUTSECONDS", "30"));

        HistoryApiConnectionOptions options = settings.ToHistorianOptions();

        // 数据库里的键名大小写可能不一致，不能因此解析失败
        Assert.Equal("upper-001", options.ClientId);
        Assert.Equal(30, options.RequestTimeoutSeconds);
    }
}

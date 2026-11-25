using System.Net;
using System.Text;
using DAP.Clients;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证平台接口统一错误解析：平台自定义 <c>message</c> 格式与 RFC 7807
/// <c>ValidationProblem</c>（<c>errors</c> 字典）都必须透出中文原因，不得只留状态码。
/// </summary>
public sealed class PlatformHttpResponseTests
{
    [Fact]
    public async Task ReadErrorMessageAsync_ReturnsPlatformMessage()
    {
        using HttpResponseMessage response = CreateResponse(
            HttpStatusCode.BadRequest, """{"message":"名称不能为空。"}""");

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("名称不能为空。", actual);
    }

    [Fact]
    public async Task ReadErrorMessageAsync_JoinsValidationProblemErrors()
    {
        // Results.ValidationProblem 的实际响应结构：errors 是字段到消息数组的字典。
        using HttpResponseMessage response = CreateResponse(
            HttpStatusCode.BadRequest,
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"ManagedDataDefinition":["名称不能为空。","工序不能为空。"]}}""");

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("名称不能为空。 工序不能为空。", actual);
    }

    [Fact]
    public async Task ReadErrorMessageAsync_IgnoresEnglishProblemTitle()
    {
        // errors 缺失时不得回显英文 title，应回落状态码描述。
        using HttpResponseMessage response = CreateResponse(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400}""");

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("请求失败（400）。", actual);
    }

    [Fact]
    public async Task ReadErrorMessageAsync_ReturnsProblemDetail()
    {
        using HttpResponseMessage response = CreateResponse(
            HttpStatusCode.Conflict, """{"title":"Conflict","status":409,"detail":"数据编码已被占用。"}""");

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("数据编码已被占用。", actual);
    }

    [Fact]
    public async Task ReadErrorMessageAsync_FallsBackForEmptyPayload()
    {
        using HttpResponseMessage response = CreateResponse(HttpStatusCode.BadRequest, string.Empty);

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("请求失败（400）。", actual);
    }

    [Fact]
    public async Task ReadErrorMessageAsync_FallsBackForNonJsonPayload()
    {
        // 反向代理返回的 HTML 错误页不原样回显。
        using HttpResponseMessage response = CreateResponse(
            HttpStatusCode.BadGateway, "<html><body>Bad Gateway</body></html>");

        string actual = await PlatformHttpResponse.ReadErrorMessageAsync(response, CancellationToken.None);

        Assert.Equal("服务端处理失败，请查看服务端日志。", actual);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}

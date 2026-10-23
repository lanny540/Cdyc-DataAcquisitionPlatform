using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证 History API 代理服务行为。
/// 服务器地址与凭据全部来自数据库中的采集服务器配置。
/// </summary>
public class HistoryApiProxyServiceTests
{
    [Fact]
    public async Task GetCurrentValueAsync_ShouldUseRequestedTagInMockResponse()
    {
        HistoryApiProxyService service = CreateService();

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("tag900"),
            new HistoryApiConnectionOptions("http://10.9.3.54:8080", UseMockResponses: true));

        Assert.True(response.Success);
        Assert.Contains("\"tagName\":\"tag900\"", response.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_ShouldFail_WhenServerAddressMissing()
    {
        HistoryApiProxyService service = CreateService();

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("tag001"),
            new HistoryApiConnectionOptions(null, "admin", "Pa55word"));

        Assert.False(response.Success);
        Assert.Contains("未配置基础地址", response.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_ShouldFail_WhenCredentialsMissing()
    {
        HistoryApiProxyService service = CreateService();

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("tag001"),
            new HistoryApiConnectionOptions("http://10.9.3.54:8080", string.Empty, "   "));

        Assert.False(response.Success);
        Assert.Contains("ClientId", response.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_ShouldSplitMultipleTagNames_InMockResponse()
    {
        HistoryApiProxyService service = CreateService();

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("tag001, tag002 ,tag003"),
            new HistoryApiConnectionOptions("http://10.9.3.54:8080", UseMockResponses: true));

        Assert.True(response.Success);
        Assert.Contains("\"tagName\":\"tag001\"", response.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"tagName\":\"tag002\"", response.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"tagName\":\"tag003\"", response.PayloadJson, StringComparison.Ordinal);
    }

    private static HistoryApiProxyService CreateService(
        HistoryApiConnectionOptions? connectionOptions = null)
    {
        _ = connectionOptions;

        return new HistoryApiProxyService(
            new TestHttpClientFactory(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AcquisitionServerRuntimeOptions {UseMockResponses = false}),
            NullLogger<HistoryApiProxyService>.Instance);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}

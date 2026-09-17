using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证 History API 代理服务行为。
/// </summary>
public class HistoryApiProxyServiceTests
{
    [Fact]
    public async Task GetCurrentValueAsync_ShouldUseRequestedTagInMockResponse()
    {
        var service = new HistoryApiProxyService(
            new TestHttpClientFactory(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new HistoryApiOptions
            {
                BaseAddress = "http://10.9.3.54:8080",
                UseMockResponses = true
            }),
            NullLogger<HistoryApiProxyService>.Instance);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("tag900"));

        Assert.True(response.Success);
        Assert.Contains("\"tagName\":\"tag900\"", response.PayloadJson, StringComparison.Ordinal);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}

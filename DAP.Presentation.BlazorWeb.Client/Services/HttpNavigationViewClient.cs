using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

/// <summary>
/// WASM 端导航加载器：调 /api/navigation，由服务端按会话 Cookie 过滤。
/// </summary>
public sealed class HttpNavigationViewClient(HttpClient httpClient) : INavigationViewClient
{
    /// <inheritdoc />
    public async Task<NavigationDto?> LoadAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await httpClient.GetAsync("/api/navigation", cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<NavigationDto>(cancellationToken);
        }
        catch (HttpRequestException)
        {
            // 网络异常时渲染空导航，认证闸门与路由守卫会兜底。
            return null;
        }
    }
}

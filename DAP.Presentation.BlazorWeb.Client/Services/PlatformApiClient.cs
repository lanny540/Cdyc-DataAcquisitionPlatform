using System.Net;
using System.Net.Http.Json;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

public sealed class PlatformApiClient(HttpClient httpClient) : IPlatformApiClient
{
    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        var overview = await httpClient.GetFromJsonAsync<DashboardOverviewDto>("/api/dashboard/overview", cancellationToken);
        return overview ?? throw new InvalidOperationException("服务端未返回平台概览数据。");
    }

    public async Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default)
    {
        var points = await httpClient.GetFromJsonAsync<IReadOnlyList<CollectionPointDto>>("/api/collection-points", cancellationToken);
        return points ?? [];
    }

    public async Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/collection-points", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CollectionPointDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("服务端未返回保存后的采集点数据。");
    }

    public async Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/collection-points/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}

using System.Net;
using System.Net.Http.Json;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

public sealed class PlatformApiClient(HttpClient httpClient) : IPlatformApiClient
{
    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        var overview =
            await httpClient.GetFromJsonAsync<DashboardOverviewDto>("/api/dashboard/overview", cancellationToken);
        return overview ?? throw new InvalidOperationException("服务端未返回平台概览数据。");
    }

    public async Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        var points =
            await httpClient.GetFromJsonAsync<IReadOnlyList<CollectionPointDto>>("/api/collection-points",
                cancellationToken);
        return points ?? [];
    }

    public async Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/collection-points", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CollectionPointDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("服务端未返回保存后的采集点数据。");
    }

    public async Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync($"/api/collection-points/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ManagedDataDefinitionDto>>("/api/managed-data-definitions",
                cancellationToken);
        return definitions ?? [];
    }

    public async Task<ManagedDataDefinitionDetailsDto> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        ManagedDataDefinitionDetailsDto? details =
            await httpClient.GetFromJsonAsync<ManagedDataDefinitionDetailsDto>(
                $"/api/managed-data-definitions/{id}?historyLimit={Math.Max(1, historyLimit)}",
                cancellationToken);
        return details ?? throw new InvalidOperationException("服务端未返回后台数据节点详情。");
    }

    public async Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/managed-data-definitions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ManagedDataDefinitionDto? result =
            await response.Content.ReadFromJsonAsync<ManagedDataDefinitionDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回保存后的后台数据定义。");
    }

    public async Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync($"/api/managed-data-definitions/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<ManagedDataExecutionResultDto> DebugReadManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync($"/api/managed-data-definitions/{id}/debug-read", null, cancellationToken);
        response.EnsureSuccessStatusCode();

        ManagedDataExecutionResultDto? result =
            await response.Content.ReadFromJsonAsync<ManagedDataExecutionResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回后台数据调试读取结果。");
    }

    public async Task<ManagedDataExecutionResultDto> CollectManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync($"/api/managed-data-definitions/{id}/collect", null, cancellationToken);
        response.EnsureSuccessStatusCode();

        ManagedDataExecutionResultDto? result =
            await response.Content.ReadFromJsonAsync<ManagedDataExecutionResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回后台数据采集结果。");
    }

    public async Task<IReadOnlyList<ServerConnectionStatusDto>> GetServerConnectionStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var statuses =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ServerConnectionStatusDto>>("/api/server-connections/status",
                cancellationToken);
        return statuses ?? [];
    }

    public async Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/managed-data-definitions/test-connection", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ManagedDataConnectionTestResultDto? result =
            await response.Content.ReadFromJsonAsync<ManagedDataConnectionTestResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回连接测试结果。");
    }

    public async Task<HistoryApiQueryResponse> GetHistoryCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/history-api/current-value", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        HistoryApiQueryResponse? result =
            await response.Content.ReadFromJsonAsync<HistoryApiQueryResponse>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回 History 最新值测试结果。");
    }

    public async Task<HistoryApiQueryResponse> GetHistoryRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/history-api/raw-data", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        HistoryApiQueryResponse? result =
            await response.Content.ReadFromJsonAsync<HistoryApiQueryResponse>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回 History 时间段数据测试结果。");
    }
}

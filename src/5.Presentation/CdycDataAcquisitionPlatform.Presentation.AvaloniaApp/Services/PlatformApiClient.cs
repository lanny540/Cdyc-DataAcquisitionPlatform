using System.Net.Http.Json;
using CdycDataAcquisitionPlatform.Core.Shared.Contracts;

namespace CdycDataAcquisitionPlatform.Presentation.AvaloniaApp.Services;

/// <summary>
/// 提供桌面客户端访问服务端 API 的封装。
/// </summary>
public sealed class PlatformApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化一个新的 <see cref="PlatformApiClient"/> 实例。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端。</param>
    public PlatformApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 获取平台概览。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>平台概览。</returns>
    public async Task<DashboardOverviewDto?> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<DashboardOverviewDto>("api/dashboard/overview", cancellationToken);
    }

    /// <summary>
    /// 获取服务端采集点配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集点集合。</returns>
    public async Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyCollection<CollectionPointDto>>("api/collection-points", cancellationToken)
            ?? Array.Empty<CollectionPointDto>();
    }

    /// <summary>
    /// 获取最新采集数据。
    /// </summary>
    /// <param name="limit">返回记录数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集数据集合。</returns>
    public async Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyCollection<CollectionDataRecordDto>>(
                   $"api/collection-data?limit={limit}",
                   cancellationToken)
               ?? Array.Empty<CollectionDataRecordDto>();
    }

    /// <summary>
    /// 同步本地采集点到服务端。
    /// </summary>
    /// <param name="points">本地采集点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    public async Task<SyncCollectionPointsResponse?> SyncLocalCollectionPointsAsync(
        IReadOnlyCollection<LocalCollectionPointDto> points,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/collection-points/sync",
            new SyncCollectionPointsRequest(points),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncCollectionPointsResponse>(cancellationToken);
    }
}

using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Queries;
using Dapper;
using Npgsql;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 提供基于 Dapper 的平台只读查询仓储。
/// </summary>
public sealed class PostgreSqlPlatformReadRepository : IPlatformReadRepository
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlPlatformReadRepository"/> 实例。
    /// </summary>
    /// <param name="dataSource">PostgreSQL 数据源。</param>
    public PostgreSqlPlatformReadRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var items = await connection.QueryAsync<CollectionPointReadModel>(new CommandDefinition(
            PlatformReadSql.CollectionPoints,
            cancellationToken: cancellationToken));

        return items.Select(MapCollectionPoint).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var items = await connection.QueryAsync<CollectionDataRecordReadModel>(new CommandDefinition(
            PlatformReadSql.CollectionData,
            new { Limit = Math.Max(1, limit) },
            cancellationToken: cancellationToken));

        return items.Select(MapCollectionDataRecord).ToArray();
    }

    /// <inheritdoc />
    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var summary = await connection.QuerySingleAsync<DashboardSummaryModel>(new CommandDefinition(
            PlatformReadSql.DashboardSummary,
            cancellationToken: cancellationToken));

        var latestRecords = await GetCollectionDataAsync(10, cancellationToken);
        var collectionPoints = await GetCollectionPointsAsync(cancellationToken);

        return new DashboardOverviewDto(
            summary.TotalCollectionPoints,
            summary.OnlineCollectionPoints,
            summary.OfflineCollectionPoints,
            summary.LocalSourcedCollectionPoints,
            latestRecords,
            collectionPoints);
    }

    private sealed record DashboardSummaryModel(
        int TotalCollectionPoints,
        int OnlineCollectionPoints,
        int OfflineCollectionPoints,
        int LocalSourcedCollectionPoints);

    private sealed record CollectionPointReadModel(
        Guid Id,
        string Code,
        string Name,
        string Protocol,
        string Endpoint,
        bool IsEnabled,
        string CommunicationStatus,
        string Source,
        string? LastError,
        DateTime UpdatedAt);

    private sealed record CollectionDataRecordReadModel(
        Guid Id,
        Guid CollectionPointId,
        string CollectionPointCode,
        string CollectionPointName,
        string MetricName,
        decimal Value,
        string Unit,
        DateTime CollectedAt);

    private static CollectionPointDto MapCollectionPoint(CollectionPointReadModel item)
    {
        return new CollectionPointDto(
            item.Id,
            item.Code,
            item.Name,
            item.Protocol,
            item.Endpoint,
            item.IsEnabled,
            item.CommunicationStatus,
            item.Source,
            item.LastError,
            new DateTimeOffset(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc)));
    }

    private static CollectionDataRecordDto MapCollectionDataRecord(CollectionDataRecordReadModel item)
    {
        return new CollectionDataRecordDto(
            item.Id,
            item.CollectionPointId,
            item.CollectionPointCode,
            item.CollectionPointName,
            item.MetricName,
            item.Value,
            item.Unit,
            new DateTimeOffset(DateTime.SpecifyKind(item.CollectedAt, DateTimeKind.Utc)));
    }
}

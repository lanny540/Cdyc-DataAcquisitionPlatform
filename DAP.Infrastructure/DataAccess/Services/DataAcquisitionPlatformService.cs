using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DAP.Infrastructure.DataAccess.Services;

/// <summary>
/// 提供采集平台业务编排服务。
/// </summary>
public sealed class DataAcquisitionPlatformService : IDataAcquisitionPlatformService
{
    private readonly DataAcquisitionPlatformDbContext _dbContext;
    private readonly ICollectionPointRepository _collectionPointRepository;
    private readonly ICollectionDataRecordRepository _collectionDataRecordRepository;
    private readonly IPlatformReadRepository _platformReadRepository;

    /// <summary>
    /// 初始化一个新的 <see cref="DataAcquisitionPlatformService"/> 实例。
    /// </summary>
    public DataAcquisitionPlatformService(
        DataAcquisitionPlatformDbContext dbContext,
        ICollectionPointRepository collectionPointRepository,
        ICollectionDataRecordRepository collectionDataRecordRepository,
        IPlatformReadRepository platformReadRepository)
    {
        _dbContext = dbContext;
        _collectionPointRepository = collectionPointRepository;
        _collectionDataRecordRepository = collectionDataRecordRepository;
        _platformReadRepository = platformReadRepository;
    }

    /// <inheritdoc />
    public Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetDashboardOverviewAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetCollectionPointsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CollectionPointDto> UpsertCollectionPointAsync(
        CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCollectionPointRequest(request);

        var normalizedCode = NormalizeCode(request.Code);
        var existingPoint = request.Id.HasValue
            ? await _collectionPointRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        existingPoint ??= await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);

        var source = NormalizeSource(request.Source);
        var updatedAt = DateTimeOffset.UtcNow;

        if (existingPoint is null)
        {
            existingPoint = new CollectionPoint
            {
                Id = request.Id ?? Guid.NewGuid(),
                Code = normalizedCode,
                Name = request.Name.Trim(),
                Protocol = request.Protocol.Trim(),
                Endpoint = request.Endpoint.Trim(),
                IsEnabled = request.IsEnabled,
                CommunicationStatus = ResolveCommunicationStatus(null, request.IsEnabled),
                Source = source,
                UpdatedAt = updatedAt
            };

            await _collectionPointRepository.AddAsync(existingPoint, cancellationToken);
        }
        else
        {
            existingPoint.Code = normalizedCode;
            existingPoint.Name = request.Name.Trim();
            existingPoint.Protocol = request.Protocol.Trim();
            existingPoint.Endpoint = request.Endpoint.Trim();
            existingPoint.IsEnabled = request.IsEnabled;
            existingPoint.Source = source;
            existingPoint.CommunicationStatus = ResolveCommunicationStatus(existingPoint.CommunicationStatus, request.IsEnabled);
            existingPoint.UpdatedAt = updatedAt;
        }

        await SaveChangesAsync(normalizedCode, cancellationToken);
        return MapCollectionPoint(existingPoint);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collectionPoint = await _collectionPointRepository.GetByIdAsync(id, cancellationToken);
        if (collectionPoint is null)
        {
            return false;
        }

        _collectionPointRepository.Remove(collectionPoint);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetCollectionDataAsync(limit, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CollectionDataRecordDto> IngestCollectionDataAsync(
        IngestCollectionDataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CollectionPointCode) ||
            string.IsNullOrWhiteSpace(request.MetricName))
        {
            throw new InvalidOperationException("采集点编码和指标名称不能为空。");
        }

        var normalizedCode = NormalizeCode(request.CollectionPointCode);
        var collectionPoint = await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);
        if (collectionPoint is null)
        {
            collectionPoint = new CollectionPoint
            {
                Id = Guid.NewGuid(),
                Code = normalizedCode,
                Name = $"{normalizedCode} 自动发现点位",
                Protocol = "Unknown",
                Endpoint = "AutoDiscovered",
                IsEnabled = true,
                CommunicationStatus = "在线",
                Source = "Server",
                UpdatedAt = request.CollectedAt
            };

            await _collectionPointRepository.AddAsync(collectionPoint, cancellationToken);
        }
        else
        {
            collectionPoint.IsEnabled = true;
            collectionPoint.CommunicationStatus = "在线";
            collectionPoint.LastError = null;
            collectionPoint.UpdatedAt = request.CollectedAt;
        }

        var record = new CollectionDataRecord
        {
            Id = Guid.NewGuid(),
            CollectionPointId = collectionPoint.Id,
            MetricName = request.MetricName.Trim(),
            Value = request.Value,
            Unit = request.Unit.Trim(),
            CollectedAt = request.CollectedAt
        };

        await _collectionDataRecordRepository.AddAsync(record, cancellationToken);
        await SaveChangesAsync(normalizedCode, cancellationToken);

        return new CollectionDataRecordDto(
            record.Id,
            collectionPoint.Id,
            collectionPoint.Code,
            collectionPoint.Name,
            record.MetricName,
            record.Value,
            record.Unit,
            record.CollectedAt);
    }

    /// <inheritdoc />
    public async Task<SyncCollectionPointsResponse> SyncLocalCollectionPointsAsync(
        SyncCollectionPointsRequest request,
        CancellationToken cancellationToken = default)
    {
        var createdCount = 0;
        var updatedCount = 0;
        var syncedIds = new List<Guid>();

        foreach (var localPoint in request.Points)
        {
            var normalizedCode = NormalizeCode(localPoint.Code);
            var collectionPoint = await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);

            if (collectionPoint is null)
            {
                collectionPoint = new CollectionPoint
                {
                    Id = Guid.NewGuid(),
                    Code = normalizedCode,
                    Name = localPoint.Name.Trim(),
                    Protocol = localPoint.Protocol.Trim(),
                    Endpoint = localPoint.Endpoint.Trim(),
                    IsEnabled = localPoint.IsEnabled,
                    CommunicationStatus = ResolveCommunicationStatus(null, localPoint.IsEnabled),
                    Source = "Local",
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                await _collectionPointRepository.AddAsync(collectionPoint, cancellationToken);
                createdCount++;
            }
            else
            {
                collectionPoint.Name = localPoint.Name.Trim();
                collectionPoint.Protocol = localPoint.Protocol.Trim();
                collectionPoint.Endpoint = localPoint.Endpoint.Trim();
                collectionPoint.IsEnabled = localPoint.IsEnabled;
                collectionPoint.CommunicationStatus = ResolveCommunicationStatus(collectionPoint.CommunicationStatus, localPoint.IsEnabled);
                collectionPoint.Source = "Local";
                collectionPoint.UpdatedAt = DateTimeOffset.UtcNow;
                updatedCount++;
            }

            syncedIds.Add(localPoint.LocalId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SyncCollectionPointsResponse(
            createdCount,
            updatedCount,
            DateTimeOffset.UtcNow,
            syncedIds);
    }

    private static void ValidateCollectionPointRequest(CollectionPointUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Protocol) ||
            string.IsNullOrWhiteSpace(request.Endpoint))
        {
            throw new InvalidOperationException("编码、名称、协议和端点不能为空。");
        }
    }

    private static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    private static string NormalizeSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? "Server" : source.Trim();
    }

    private static string ResolveCommunicationStatus(string? existingStatus, bool isEnabled)
    {
        if (!isEnabled)
        {
            return "停用";
        }

        return existingStatus is "在线" or "离线" ? existingStatus : "在线";
    }

    private static CollectionPointDto MapCollectionPoint(CollectionPoint item)
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
            item.UpdatedAt);
    }

    private async Task SaveChangesAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgresException &&
                                           postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"采集点编码 {code} 已存在。", ex);
        }
    }
}

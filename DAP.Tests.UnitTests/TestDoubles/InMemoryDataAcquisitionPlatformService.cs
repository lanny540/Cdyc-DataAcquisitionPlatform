using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests.TestDoubles;

/// <summary>
/// 为单元测试提供内存版平台服务替身。
/// </summary>
internal sealed class InMemoryDataAcquisitionPlatformService : IDataAcquisitionPlatformService
{
    private readonly Lock _syncRoot = new();

    private readonly List<CollectionPointDto> _collectionPoints =
    [
        new(
            Guid.NewGuid(),
            "MB-01",
            "锅炉一号温度点",
            "Modbus TCP",
            "192.168.10.21:502",
            true,
            "在线",
            "Server",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-12))
    ];

    private readonly List<CollectionDataRecordDto> _records = [];

    public Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(new DashboardOverviewDto(
                _collectionPoints.Count,
                _collectionPoints.Count(item => item.CommunicationStatus == "在线"),
                _collectionPoints.Count(item => item.CommunicationStatus != "在线"),
                _collectionPoints.Count(item => item.Source == "Local"),
                _records.ToArray(),
                _collectionPoints.ToArray()));
        }
    }

    public Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyCollection<CollectionPointDto>>(_collectionPoints.OrderBy(item => item.Code)
                .ToArray());
        }
    }

    public Task<CollectionPointDto> UpsertCollectionPointAsync(
        CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var normalizedCode = request.Code.Trim().ToUpperInvariant();
            CollectionPointDto? existingPoint = request.Id.HasValue
                ? _collectionPoints.FirstOrDefault(item => item.Id == request.Id.Value)
                : _collectionPoints.FirstOrDefault(item =>
                    item.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));

            var savedPoint = new CollectionPointDto(
                existingPoint?.Id ?? request.Id ?? Guid.NewGuid(),
                normalizedCode,
                request.Name.Trim(),
                request.Protocol.Trim(),
                request.Endpoint.Trim(),
                request.IsEnabled,
                request.IsEnabled
                    ? existingPoint?.CommunicationStatus is "在线" or "离线" ? existingPoint.CommunicationStatus : "在线"
                    : "停用",
                string.IsNullOrWhiteSpace(request.Source) ? "Server" : request.Source.Trim(),
                existingPoint?.LastError,
                DateTimeOffset.UtcNow);

            if (existingPoint is null)
            {
                _collectionPoints.Add(savedPoint);
            }
            else
            {
                var index = _collectionPoints.IndexOf(existingPoint);
                _collectionPoints[index] = savedPoint;
            }

            return Task.FromResult(savedPoint);
        }
    }

    public Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            CollectionPointDto? existingPoint = _collectionPoints.FirstOrDefault(item => item.Id == id);
            if (existingPoint is null)
            {
                return Task.FromResult(false);
            }

            _collectionPoints.Remove(existingPoint);
            _records.RemoveAll(item => item.CollectionPointId == id);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyCollection<CollectionDataRecordDto>>(
                _records.OrderByDescending(item => item.CollectedAt).Take(Math.Max(1, limit)).ToArray());
        }
    }

    public Task<CollectionDataRecordDto> IngestCollectionDataAsync(
        IngestCollectionDataRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            CollectionPointDto? point = _collectionPoints.FirstOrDefault(item =>
                item.Code.Equals(request.CollectionPointCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (point is null)
            {
                point = new CollectionPointDto(
                    Guid.NewGuid(),
                    request.CollectionPointCode.Trim().ToUpperInvariant(),
                    $"{request.CollectionPointCode.Trim().ToUpperInvariant()} 自动发现点位",
                    "Unknown",
                    "AutoDiscovered",
                    true,
                    "在线",
                    "Server",
                    null,
                    request.CollectedAt);

                _collectionPoints.Add(point);
            }

            var record = new CollectionDataRecordDto(
                Guid.NewGuid(),
                point.Id,
                point.Code,
                point.Name,
                request.MetricName.Trim(),
                request.Value,
                request.Unit.Trim(),
                request.CollectedAt);

            _records.Add(record);
            return Task.FromResult(record);
        }
    }

    public Task<SyncCollectionPointsResponse> SyncLocalCollectionPointsAsync(
        SyncCollectionPointsRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var createdCount = 0;
            var updatedCount = 0;
            var syncedIds = new List<Guid>();

            foreach (LocalCollectionPointDto localPoint in request.Points)
            {
                var normalizedCode = localPoint.Code.Trim().ToUpperInvariant();
                CollectionPointDto? existingPoint = _collectionPoints.FirstOrDefault(item =>
                    item.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));

                var savedPoint = new CollectionPointDto(
                    existingPoint?.Id ?? Guid.NewGuid(),
                    normalizedCode,
                    localPoint.Name.Trim(),
                    localPoint.Protocol.Trim(),
                    localPoint.Endpoint.Trim(),
                    localPoint.IsEnabled,
                    localPoint.IsEnabled
                        ? existingPoint?.CommunicationStatus is "在线" or "离线" ? existingPoint.CommunicationStatus : "在线"
                        : "停用",
                    "Local",
                    existingPoint?.LastError,
                    DateTimeOffset.UtcNow);

                if (existingPoint is null)
                {
                    _collectionPoints.Add(savedPoint);
                    createdCount++;
                }
                else
                {
                    var index = _collectionPoints.IndexOf(existingPoint);
                    _collectionPoints[index] = savedPoint;
                    updatedCount++;
                }

                syncedIds.Add(localPoint.LocalId);
            }

            return Task.FromResult(new SyncCollectionPointsResponse(
                createdCount,
                updatedCount,
                DateTimeOffset.UtcNow,
                syncedIds));
        }
    }
}

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
    private readonly List<ManagedDataDefinitionDto> _managedDataDefinitions = [];

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

    public Task<IReadOnlyCollection<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyCollection<ManagedDataDefinitionDto>>(
                _managedDataDefinitions.OrderBy(item => item.AcquisitionType).ThenBy(item => item.Code).ToArray());
        }
    }

    public Task<ManagedDataDefinitionDetailsDto?> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            ManagedDataDefinitionDto? definition = _managedDataDefinitions.FirstOrDefault(item => item.Id == id);
            if (definition is null)
            {
                return Task.FromResult<ManagedDataDefinitionDetailsDto?>(null);
            }

            CollectionPointDto? point = _collectionPoints.FirstOrDefault(item =>
                item.Code.Equals(definition.Code, StringComparison.OrdinalIgnoreCase));

            CollectionDataRecordDto[] records = _records
                .Where(item => item.CollectionPointCode.Equals(definition.Code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CollectedAt)
                .Take(Math.Max(1, historyLimit))
                .ToArray();

            return Task.FromResult<ManagedDataDefinitionDetailsDto?>(
                new ManagedDataDefinitionDetailsDto(definition, point, records.FirstOrDefault(), records));
        }
    }

    public Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var normalizedCode = request.Code.Trim().ToUpperInvariant();
            ManagedDataDefinitionDto? existingDefinition = request.Id.HasValue
                ? _managedDataDefinitions.FirstOrDefault(item => item.Id == request.Id.Value)
                : _managedDataDefinitions.FirstOrDefault(item =>
                    item.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
            var originalCode = existingDefinition?.Code;

            var savedDefinition = new ManagedDataDefinitionDto(
                existingDefinition?.Id ?? request.Id ?? Guid.NewGuid(),
                normalizedCode,
                request.Name.Trim(),
                request.AcquisitionType.Trim(),
                request.ConnectionAddress.Trim(),
                request.Identifier.Trim(),
                request.Department.Trim(),
                request.ProcessCode.Trim(),
                request.DataCategory.Trim(),
                request.Unit.Trim(),
                request.Description.Trim(),
                string.IsNullOrWhiteSpace(request.ConfigurationJson) ? "{}" : request.ConfigurationJson.Trim(),
                string.IsNullOrWhiteSpace(request.BusinessTagsJson) ? "{}" : request.BusinessTagsJson.Trim(),
                request.CollectionIntervalSeconds,
                request.IsEnabled,
                DateTimeOffset.UtcNow);

            if (existingDefinition is null)
            {
                _managedDataDefinitions.Add(savedDefinition);
            }
            else
            {
                var index = _managedDataDefinitions.IndexOf(existingDefinition);
                _managedDataDefinitions[index] = savedDefinition;
            }

            SyncCollectionPointForManagedDefinition(savedDefinition, originalCode);
            return Task.FromResult(savedDefinition);
        }
    }

    public Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            ManagedDataDefinitionDto? existingDefinition =
                _managedDataDefinitions.FirstOrDefault(item => item.Id == id);
            if (existingDefinition is null)
            {
                return Task.FromResult(false);
            }

            _managedDataDefinitions.Remove(existingDefinition);
            CollectionPointDto? existingPoint = _collectionPoints.FirstOrDefault(item =>
                item.Code.Equals(existingDefinition.Code, StringComparison.OrdinalIgnoreCase));

            if (existingPoint is not null)
            {
                _collectionPoints.Remove(existingPoint);
                _records.RemoveAll(item => item.CollectionPointId == existingPoint.Id);
            }

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

    private void SyncCollectionPointForManagedDefinition(ManagedDataDefinitionDto definition, string? originalCode)
    {
        CollectionPointDto? existingPoint = null;
        if (!string.IsNullOrWhiteSpace(originalCode) &&
            !originalCode.Equals(definition.Code, StringComparison.OrdinalIgnoreCase))
        {
            existingPoint = _collectionPoints.FirstOrDefault(item =>
                item.Code.Equals(originalCode, StringComparison.OrdinalIgnoreCase));
        }

        existingPoint ??= _collectionPoints.FirstOrDefault(item =>
            item.Code.Equals(definition.Code, StringComparison.OrdinalIgnoreCase));

        var savedPoint = new CollectionPointDto(
            existingPoint?.Id ?? Guid.NewGuid(),
            definition.Code,
            definition.Name,
            definition.AcquisitionType,
            definition.ConnectionAddress,
            definition.IsEnabled,
            definition.IsEnabled
                ? existingPoint?.CommunicationStatus is "在线" or "离线" ? existingPoint.CommunicationStatus : "在线"
                : "停用",
            "Server",
            existingPoint?.LastError,
            definition.UpdatedAt);

        if (existingPoint is null)
        {
            _collectionPoints.Add(savedPoint);
        }
        else
        {
            var index = _collectionPoints.IndexOf(existingPoint);
            _collectionPoints[index] = savedPoint;
        }

        for (var index = 0; index < _records.Count; index++)
        {
            if (_records[index].CollectionPointId != savedPoint.Id)
            {
                continue;
            }

            CollectionDataRecordDto record = _records[index];
            _records[index] = record with
            {
                CollectionPointCode = savedPoint.Code,
                CollectionPointName = savedPoint.Name
            };
        }
    }
}

using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace DAP.Infrastructure.DataAccess.Services;

/// <summary>
/// 提供采集平台业务编排服务。
/// </summary>
public sealed class DataAcquisitionPlatformService : IDataAcquisitionPlatformService
{
    private readonly DataAcquisitionPlatformDbContext _dbContext;
    private readonly ICollectionPointRepository _collectionPointRepository;
    private readonly IManagedDataDefinitionRepository _managedDataDefinitionRepository;
    private readonly ICollectionDataRecordRepository _collectionDataRecordRepository;
    private readonly IPlatformReadRepository _platformReadRepository;

    /// <summary>
    /// 初始化一个新的 <see cref="DataAcquisitionPlatformService"/> 实例。
    /// </summary>
    public DataAcquisitionPlatformService(
        DataAcquisitionPlatformDbContext dbContext,
        ICollectionPointRepository collectionPointRepository,
        IManagedDataDefinitionRepository managedDataDefinitionRepository,
        ICollectionDataRecordRepository collectionDataRecordRepository,
        IPlatformReadRepository platformReadRepository)
    {
        _dbContext = dbContext;
        _collectionPointRepository = collectionPointRepository;
        _managedDataDefinitionRepository = managedDataDefinitionRepository;
        _collectionDataRecordRepository = collectionDataRecordRepository;
        _platformReadRepository = platformReadRepository;
    }

    /// <inheritdoc />
    public Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetDashboardOverviewAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
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
        CollectionPoint? existingPoint = request.Id.HasValue
            ? await _collectionPointRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        existingPoint ??= await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);

        var source = NormalizeSource(request.Source);
        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;

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
            existingPoint.CommunicationStatus =
                ResolveCommunicationStatus(existingPoint.CommunicationStatus, request.IsEnabled);
            existingPoint.UpdatedAt = updatedAt;
        }

        await SaveChangesAsync(normalizedCode, "采集点编码", cancellationToken);
        return MapCollectionPoint(existingPoint);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        CollectionPoint? collectionPoint = await _collectionPointRepository.GetByIdAsync(id, cancellationToken);
        if (collectionPoint is null)
        {
            return false;
        }

        _collectionPointRepository.Remove(collectionPoint);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetManagedDataDefinitionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagedDataDefinitionDetailsDto?> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        return _platformReadRepository.GetManagedDataDefinitionDetailsAsync(id, historyLimit, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateManagedDataDefinitionRequest(request);

        var normalizedCode = NormalizeCode(request.Code);
        ManagedDataDefinition? existingDefinition = request.Id.HasValue
            ? await _managedDataDefinitionRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        existingDefinition ??= await _managedDataDefinitionRepository.GetByCodeAsync(normalizedCode, cancellationToken);

        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;

        if (existingDefinition is null)
        {
            existingDefinition = new ManagedDataDefinition
            {
                Id = request.Id ?? Guid.NewGuid(),
                Code = normalizedCode,
                Name = request.Name.Trim(),
                AcquisitionType = request.AcquisitionType.Trim(),
                ConnectionAddress = request.ConnectionAddress.Trim(),
                Identifier = request.Identifier.Trim(),
                Department = request.Department.Trim(),
                ProcessCode = request.ProcessCode.Trim(),
                DataCategory = request.DataCategory.Trim(),
                Unit = request.Unit.Trim(),
                Description = request.Description.Trim(),
                ConfigurationJson = NormalizeJsonOrEmptyObject(request.ConfigurationJson),
                BusinessTagsJson = NormalizeJsonOrEmptyObject(request.BusinessTagsJson),
                CollectionIntervalSeconds = request.CollectionIntervalSeconds,
                IsEnabled = request.IsEnabled,
                UpdatedAt = updatedAt
            };

            await _managedDataDefinitionRepository.AddAsync(existingDefinition, cancellationToken);
        }
        else
        {
            existingDefinition.Code = normalizedCode;
            existingDefinition.Name = request.Name.Trim();
            existingDefinition.AcquisitionType = request.AcquisitionType.Trim();
            existingDefinition.ConnectionAddress = request.ConnectionAddress.Trim();
            existingDefinition.Identifier = request.Identifier.Trim();
            existingDefinition.Department = request.Department.Trim();
            existingDefinition.ProcessCode = request.ProcessCode.Trim();
            existingDefinition.DataCategory = request.DataCategory.Trim();
            existingDefinition.Unit = request.Unit.Trim();
            existingDefinition.Description = request.Description.Trim();
            existingDefinition.ConfigurationJson = NormalizeJsonOrEmptyObject(request.ConfigurationJson);
            existingDefinition.BusinessTagsJson = NormalizeJsonOrEmptyObject(request.BusinessTagsJson);
            existingDefinition.CollectionIntervalSeconds = request.CollectionIntervalSeconds;
            existingDefinition.IsEnabled = request.IsEnabled;
            existingDefinition.UpdatedAt = updatedAt;
        }

        await UpsertCollectionPointForManagedDefinitionAsync(existingDefinition, cancellationToken);
        await SaveChangesAsync(normalizedCode, "后台数据编码", cancellationToken);
        return MapManagedDataDefinition(existingDefinition);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ManagedDataDefinition? definition = await _managedDataDefinitionRepository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return false;
        }

        _managedDataDefinitionRepository.Remove(definition);
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
        CollectionPoint? collectionPoint =
            await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);
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
        await SaveChangesAsync(normalizedCode, "采集点编码", cancellationToken);

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

        foreach (LocalCollectionPointDto localPoint in request.Points)
        {
            var normalizedCode = NormalizeCode(localPoint.Code);
            CollectionPoint? collectionPoint =
                await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);

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
                collectionPoint.CommunicationStatus =
                    ResolveCommunicationStatus(collectionPoint.CommunicationStatus, localPoint.IsEnabled);
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

    private static void ValidateManagedDataDefinitionRequest(ManagedDataDefinitionUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.AcquisitionType) ||
            string.IsNullOrWhiteSpace(request.ConnectionAddress) ||
            string.IsNullOrWhiteSpace(request.Identifier) ||
            string.IsNullOrWhiteSpace(request.Department) ||
            string.IsNullOrWhiteSpace(request.ProcessCode) ||
            string.IsNullOrWhiteSpace(request.DataCategory) ||
            request.CollectionIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("编码、名称、采集方式、连接地址、数据标识、部门、工序、数据类别不能为空，且采集频次必须大于 0 秒。");
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

    private static ManagedDataDefinitionDto MapManagedDataDefinition(ManagedDataDefinition item)
    {
        return new ManagedDataDefinitionDto(
            item.Id,
            item.Code,
            item.Name,
            item.AcquisitionType,
            item.ConnectionAddress,
            item.Identifier,
            item.Department,
            item.ProcessCode,
            item.DataCategory,
            item.Unit,
            item.Description,
            item.ConfigurationJson,
            item.BusinessTagsJson,
            item.CollectionIntervalSeconds,
            item.IsEnabled,
            item.UpdatedAt);
    }

    private async Task UpsertCollectionPointForManagedDefinitionAsync(
        ManagedDataDefinition definition,
        CancellationToken cancellationToken)
    {
        CollectionPoint? collectionPoint =
            await _collectionPointRepository.GetByCodeAsync(definition.Code, cancellationToken);

        if (collectionPoint is null)
        {
            collectionPoint = new CollectionPoint
            {
                Id = Guid.NewGuid(),
                Code = definition.Code,
                Name = definition.Name,
                Protocol = definition.AcquisitionType,
                Endpoint = definition.ConnectionAddress,
                IsEnabled = definition.IsEnabled,
                CommunicationStatus = ResolveCommunicationStatus(null, definition.IsEnabled),
                Source = "Server",
                UpdatedAt = definition.UpdatedAt
            };

            await _collectionPointRepository.AddAsync(collectionPoint, cancellationToken);
            return;
        }

        collectionPoint.Name = definition.Name;
        collectionPoint.Protocol = definition.AcquisitionType;
        collectionPoint.Endpoint = definition.ConnectionAddress;
        collectionPoint.IsEnabled = definition.IsEnabled;
        collectionPoint.CommunicationStatus =
            ResolveCommunicationStatus(collectionPoint.CommunicationStatus, definition.IsEnabled);
        collectionPoint.Source = "Server";
        collectionPoint.UpdatedAt = definition.UpdatedAt;
    }

    private static string NormalizeJsonOrEmptyObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.GetRawText();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("配置 JSON 或业务标识 JSON 格式不正确。", ex);
        }
    }

    private async Task SaveChangesAsync(string code, string codeDisplayName, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException {SqlState: PostgresErrorCodes.UniqueViolation})
        {
            throw new InvalidOperationException($"{codeDisplayName} {code} 已存在。", ex);
        }
    }
}

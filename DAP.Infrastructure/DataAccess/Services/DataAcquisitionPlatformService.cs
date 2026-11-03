using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
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
    /// <summary>
    /// 客户端一次性同步采集点的数量上限。
    /// </summary>
    private const int MaxLocalSyncPoints = 2000;

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
        var normalizedCode = NormalizeCode(request.Code);
        ManagedDataDefinition? existingDefinition = request.Id.HasValue
            ? await _managedDataDefinitionRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        existingDefinition ??= await _managedDataDefinitionRepository.GetByCodeAsync(normalizedCode, cancellationToken);
        var originalCode = existingDefinition?.Code;

        // 请求来自 JSON：非必填字符串缺失时是 null 而不是空串，
        // 直接 .Trim() 会在校验之前抛 NullReferenceException（表现为 500 而不是中文校验提示）。
        var name = request.Name?.Trim() ?? string.Empty;
        var identifier = request.Identifier?.Trim() ?? string.Empty;
        var department = request.Department?.Trim() ?? string.Empty;
        var processCode = request.ProcessCode?.Trim() ?? string.Empty;
        var dataCategory = request.DataCategory?.Trim() ?? string.Empty;
        var unit = request.Unit?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;

        // 选择采集服务器后，采集方式与连接地址由服务器决定，节点只需补充自身采集信息。
        ServerConnection? boundServer = await ResolveBoundServerAsync(
            request.ServerConnectionId ?? existingDefinition?.ServerConnectionId, cancellationToken);
        var (acquisitionType, connectionAddress) = ResolveAcquisitionTarget(request, boundServer);

        ValidateManagedDataDefinitionRequest(request, acquisitionType, connectionAddress);

        Dictionary<string, string> nodeConfiguration = ParseJsonDictionary(request.ConfigurationJson);
        IReadOnlyCollection<string> nodeErrors = AcquisitionServerCatalog.ValidateNode(
            acquisitionType, identifier, nodeConfiguration);
        if (nodeErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", nodeErrors));
        }

        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        Guid? serverConnectionId = boundServer?.Id ?? request.ServerConnectionId;

        if (existingDefinition is null)
        {
            existingDefinition = new ManagedDataDefinition
            {
                Id = request.Id ?? Guid.NewGuid(),
                Code = normalizedCode,
                Name = name,
                AcquisitionType = acquisitionType,
                ConnectionAddress = connectionAddress,
                ServerConnectionId = serverConnectionId,
                Identifier = identifier,
                Department = department,
                ProcessCode = processCode,
                DataCategory = dataCategory,
                Unit = unit,
                Description = description,
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
            existingDefinition.Name = name;
            existingDefinition.AcquisitionType = acquisitionType;
            existingDefinition.ConnectionAddress = connectionAddress;
            existingDefinition.ServerConnectionId = serverConnectionId;
            existingDefinition.Identifier = identifier;
            existingDefinition.Department = department;
            existingDefinition.ProcessCode = processCode;
            existingDefinition.DataCategory = dataCategory;
            existingDefinition.Unit = unit;
            existingDefinition.Description = description;
            existingDefinition.ConfigurationJson = NormalizeJsonOrEmptyObject(request.ConfigurationJson);
            existingDefinition.BusinessTagsJson = NormalizeJsonOrEmptyObject(request.BusinessTagsJson);
            existingDefinition.CollectionIntervalSeconds = request.CollectionIntervalSeconds;
            existingDefinition.IsEnabled = request.IsEnabled;
            existingDefinition.UpdatedAt = updatedAt;
        }

        await UpsertCollectionPointForManagedDefinitionAsync(existingDefinition, originalCode, cancellationToken);
        if (boundServer is null && serverConnectionId is null)
        {
            ServerConnection ensuredServer =
                await EnsureServerConnectionForManagedDefinitionAsync(existingDefinition, cancellationToken);
            existingDefinition.ServerConnectionId = ensuredServer.Id;
        }

        await SaveChangesAsync(normalizedCode, "后台数据编码", cancellationToken);
        return MapManagedDataDefinition(existingDefinition, boundServer?.DisplayName);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ManagedDataDefinition? definition = await _managedDataDefinitionRepository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return false;
        }

        await RemoveCollectionPointForManagedDefinitionAsync(definition.Code, cancellationToken);
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

        // 单位在库上有「不得为空」的 check 约束：这里先给出可读的中文提示，
        // 而不是让一次登录式上报变成 500。
        var unit = request.Unit?.Trim() ?? string.Empty;

        if (unit.Length == 0)
        {
            throw new InvalidOperationException("指标单位不能为空。");
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
            Unit = unit,
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

        // 上限防止一次请求塞进几十万条：这是客户端配置同步，正常规模是几十到几百。
        if (request.Points.Count > MaxLocalSyncPoints)
        {
            throw new InvalidOperationException(
                $"单次最多同步 {MaxLocalSyncPoints} 个采集点，本次提交 {request.Points.Count} 个。");
        }

        foreach (LocalCollectionPointDto localPoint in request.Points)
        {
            var normalizedCode = NormalizeCode(localPoint.Code);

            if (normalizedCode.Length == 0)
            {
                throw new InvalidOperationException("采集点编码不能为空。");
            }

            var name = localPoint.Name?.Trim() ?? string.Empty;
            var protocol = localPoint.Protocol?.Trim() ?? string.Empty;
            var endpoint = localPoint.Endpoint?.Trim() ?? string.Empty;

            if (name.Length == 0 || protocol.Length == 0 || endpoint.Length == 0)
            {
                throw new InvalidOperationException($"采集点 {normalizedCode} 的名称、协议和端点不能为空。");
            }

            CollectionPoint? collectionPoint =
                await _collectionPointRepository.GetByCodeAsync(normalizedCode, cancellationToken);

            if (collectionPoint is null)
            {
                collectionPoint = new CollectionPoint
                {
                    Id = Guid.NewGuid(),
                    Code = normalizedCode,
                    Name = name,
                    Protocol = protocol,
                    Endpoint = endpoint,
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
                collectionPoint.Name = name;
                collectionPoint.Protocol = protocol;
                collectionPoint.Endpoint = endpoint;
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

    private static void ValidateManagedDataDefinitionRequest(
        ManagedDataDefinitionUpsertRequest request,
        string acquisitionType,
        string connectionAddress)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add("编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(acquisitionType))
        {
            errors.Add("采集方式不能为空。");
        }

        if (string.IsNullOrWhiteSpace(connectionAddress))
        {
            errors.Add("连接地址不能为空，请选择已配置地址的采集服务器。");
        }

        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            errors.Add("数据标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Department))
        {
            errors.Add("部门不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.ProcessCode))
        {
            errors.Add("工序不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.DataCategory))
        {
            errors.Add("数据类别不能为空。");
        }

        if (request.CollectionIntervalSeconds <= 0)
        {
            errors.Add("采集频次必须大于 0 秒。");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private async Task<ServerConnection?> ResolveBoundServerAsync(
        Guid? serverConnectionId,
        CancellationToken cancellationToken)
    {
        if (!serverConnectionId.HasValue)
        {
            return null;
        }

        return await _dbContext.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == serverConnectionId.Value, cancellationToken);
    }

    private static (string AcquisitionType, string ConnectionAddress) ResolveAcquisitionTarget(
        ManagedDataDefinitionUpsertRequest request,
        ServerConnection? boundServer)
    {
        if (boundServer is not null)
        {
            return (boundServer.AcquisitionType.Trim(), boundServer.Address.Trim());
        }

        return (request.AcquisitionType?.Trim() ?? string.Empty, request.ConnectionAddress?.Trim() ?? string.Empty);
    }

    private static string NormalizeCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpperInvariant();
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

    private static ManagedDataDefinitionDto MapManagedDataDefinition(
        ManagedDataDefinition item,
        string? serverName = null)
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
            item.UpdatedAt,
            item.ServerConnectionId,
            serverName ?? item.ServerConnection?.DisplayName);
    }

    private async Task UpsertCollectionPointForManagedDefinitionAsync(
        ManagedDataDefinition definition,
        string? originalCode,
        CancellationToken cancellationToken)
    {
        CollectionPoint? collectionPoint = null;
        if (!string.IsNullOrWhiteSpace(originalCode) &&
            !string.Equals(originalCode, definition.Code, StringComparison.OrdinalIgnoreCase))
        {
            collectionPoint = await _collectionPointRepository.GetByCodeAsync(originalCode, cancellationToken);
        }

        collectionPoint ??= await _collectionPointRepository.GetByCodeAsync(definition.Code, cancellationToken);

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

        collectionPoint.Code = definition.Code;
        collectionPoint.Name = definition.Name;
        collectionPoint.Protocol = definition.AcquisitionType;
        collectionPoint.Endpoint = definition.ConnectionAddress;
        collectionPoint.IsEnabled = definition.IsEnabled;
        collectionPoint.CommunicationStatus =
            ResolveCommunicationStatus(collectionPoint.CommunicationStatus, definition.IsEnabled);
        collectionPoint.Source = "Server";
        collectionPoint.UpdatedAt = definition.UpdatedAt;
    }

    private async Task RemoveCollectionPointForManagedDefinitionAsync(string code, CancellationToken cancellationToken)
    {
        CollectionPoint? collectionPoint = await _collectionPointRepository.GetByCodeAsync(code, cancellationToken);
        if (collectionPoint is null)
        {
            return;
        }

        _collectionPointRepository.Remove(collectionPoint);
    }

    private async Task<ServerConnection> EnsureServerConnectionForManagedDefinitionAsync(
        ManagedDataDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.ServerConnectionId.HasValue)
        {
            ServerConnection? boundConnection = await _dbContext.ServerConnections
                .FirstOrDefaultAsync(item => item.Id == definition.ServerConnectionId.Value, cancellationToken);
            if (boundConnection is not null)
            {
                return boundConnection;
            }
        }

        var acquisitionType = definition.AcquisitionType.Trim();
        var address = NormalizeServerAddress(definition.ConnectionAddress);

        ServerConnection? existingConnection = await _dbContext.ServerConnections
            .FirstOrDefaultAsync(
                item => item.AcquisitionType == acquisitionType && item.Address == address,
                cancellationToken);
        if (existingConnection is not null)
        {
            return existingConnection;
        }

        var serverConnection = new ServerConnection
        {
            Id = Guid.NewGuid(),
            Code = $"SRV_{Guid.NewGuid():N}".ToUpperInvariant(),
            DisplayName = $"{acquisitionType} 服务器",
            AcquisitionType = acquisitionType,
            Address = address,
            ConfigurationJson = definition.ConfigurationJson,
            IsEnabled = definition.IsEnabled,
            UpdatedAt = definition.UpdatedAt
        };
        _dbContext.ServerConnections.Add(serverConnection);
        return serverConnection;
    }

    private static string NormalizeServerAddress(string address)
    {
        return address.Trim().TrimEnd('/');
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

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return parsed?.ToDictionary(
                       item => item.Key,
                       item => item.Value.ValueKind == JsonValueKind.String
                           ? item.Value.GetString() ?? string.Empty
                           : item.Value.ToString(),
                       StringComparer.OrdinalIgnoreCase) ??
                   new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveChangesAsync(string code, string codeDisplayName, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation
                                           })
        {
            throw new InvalidOperationException($"{codeDisplayName} {code} 已存在。", ex);
        }
    }
}

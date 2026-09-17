using System.Collections.Concurrent;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 提供后台数据自动定时采集调度能力。
/// </summary>
public sealed class ManagedDataCollectionScheduler(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ManagedDataSchedulerOptions> options,
    ILogger<ManagedDataCollectionScheduler> logger) : BackgroundService
{
    private readonly ManagedDataSchedulerOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAttemptTimes = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("后台数据自动采集调度器已禁用。");
            return;
        }

        var startupDelaySeconds = Math.Max(0, _options.StartupDelaySeconds);
        var scanIntervalSeconds = Math.Max(1, _options.ScanIntervalSeconds);

        logger.LogInformation(
            "后台数据自动采集调度器已启动，启动延迟 {StartupDelaySeconds} 秒，扫描周期 {ScanIntervalSeconds} 秒。",
            startupDelaySeconds,
            scanIntervalSeconds);

        if (startupDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDispatchCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "后台数据自动采集调度周期执行失败。");
            }

            await Task.Delay(TimeSpan.FromSeconds(scanIntervalSeconds), stoppingToken);
        }
    }

    private async Task RunDispatchCycleAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        var platformService =
            scope.ServiceProvider.GetRequiredService<IDataAcquisitionPlatformService>();
        var executionService =
            scope.ServiceProvider.GetRequiredService<IManagedDataExecutionService>();
        var collectionPointRepository =
            scope.ServiceProvider.GetRequiredService<ICollectionPointRepository>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

        IReadOnlyCollection<ManagedDataDefinitionDto> definitions =
            await platformService.GetManagedDataDefinitionsAsync(cancellationToken);

        foreach (ManagedDataDefinitionDto definition in GetDefinitionsToDispatch(definitions))
        {
            if (!await ShouldDispatchDefinitionAsync(platformService, definition, cancellationToken))
            {
                continue;
            }

            await CollectDefinitionAsync(
                definition,
                executionService,
                collectionPointRepository,
                dbContext,
                cancellationToken);
        }
    }

    private static IOrderedEnumerable<ManagedDataDefinitionDto> GetDefinitionsToDispatch(
        IReadOnlyCollection<ManagedDataDefinitionDto> definitions)
    {
        return definitions
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.CollectionIntervalSeconds)
            .ThenBy(item => item.Code);
    }

    private async Task<bool> ShouldDispatchDefinitionAsync(
        IDataAcquisitionPlatformService platformService,
        ManagedDataDefinitionDto definition,
        CancellationToken cancellationToken)
    {
        ManagedDataDefinitionDetailsDto? details =
            await platformService.GetManagedDataDefinitionDetailsAsync(definition.Id, 1, cancellationToken);

        return details is not null && IsDue(definition, details.CurrentRecord);
    }

    private async Task CollectDefinitionAsync(
        ManagedDataDefinitionDto definition,
        IManagedDataExecutionService executionService,
        ICollectionPointRepository collectionPointRepository,
        DataAcquisitionPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        _lastAttemptTimes[definition.Id] = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "开始按计划采集后台数据 {Code}，采集频次 {IntervalSeconds} 秒。",
            definition.Code,
            definition.CollectionIntervalSeconds);

        ManagedDataExecutionResultDto result =
            await executionService.CollectAsync(definition.Id, cancellationToken);

        await SyncCollectionPointStateAsync(
            definition,
            result,
            collectionPointRepository,
            dbContext,
            cancellationToken);

        LogCollectionResult(definition, result);
    }

    private void LogCollectionResult(ManagedDataDefinitionDto definition, ManagedDataExecutionResultDto result)
    {
        if (result.Success)
        {
            logger.LogInformation(
                "后台数据 {Code} 自动采集成功，值 {Value}。",
                definition.Code,
                result.ParsedValue);
            return;
        }

        logger.LogWarning(
            "后台数据 {Code} 自动采集失败：{ErrorMessage}",
            definition.Code,
            result.ErrorMessage ?? result.StatusMessage);
    }

    private bool IsDue(ManagedDataDefinitionDto definition, CollectionDataRecordDto? currentRecord)
    {
        DateTimeOffset? latestRecordAt = currentRecord?.CollectedAt;
        DateTimeOffset? latestAttemptAt = _lastAttemptTimes.TryGetValue(definition.Id, out DateTimeOffset attemptAt)
            ? attemptAt
            : null;

        DateTimeOffset referenceTime = latestRecordAt.HasValue && latestAttemptAt.HasValue
            ? latestRecordAt.Value > latestAttemptAt.Value ? latestRecordAt.Value : latestAttemptAt.Value
            : latestRecordAt ?? latestAttemptAt ?? DateTimeOffset.MinValue;

        return DateTimeOffset.UtcNow - referenceTime >=
               TimeSpan.FromSeconds(Math.Max(1, definition.CollectionIntervalSeconds));
    }

    private static async Task SyncCollectionPointStateAsync(
        ManagedDataDefinitionDto definition,
        ManagedDataExecutionResultDto result,
        ICollectionPointRepository collectionPointRepository,
        DataAcquisitionPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        CollectionPoint? collectionPoint =
            await collectionPointRepository.GetByCodeAsync(definition.Code, cancellationToken);

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
                Source = "Server",
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await collectionPointRepository.AddAsync(collectionPoint, cancellationToken);
        }

        collectionPoint.Name = definition.Name;
        collectionPoint.Protocol = definition.AcquisitionType;
        collectionPoint.Endpoint = definition.ConnectionAddress;
        collectionPoint.IsEnabled = definition.IsEnabled;
        collectionPoint.Source = "Server";
        collectionPoint.CommunicationStatus = result.Success ? "在线" : "离线";
        collectionPoint.LastError = result.Success ? null : result.ErrorMessage ?? result.StatusMessage;
        collectionPoint.UpdatedAt = result.ExecutedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// 表示后台数据自动采集调度配置。
/// </summary>
public sealed class ManagedDataSchedulerOptions
{
    public const string SectionName = "ManagedDataScheduler";

    public bool Enabled { get; set; } = true;

    public int StartupDelaySeconds { get; set; } = 5;

    public int ScanIntervalSeconds { get; set; } = 5;
}

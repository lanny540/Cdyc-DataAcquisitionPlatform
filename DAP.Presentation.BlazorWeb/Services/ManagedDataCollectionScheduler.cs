using System.Collections.Concurrent;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 提供后台数据自动定时采集调度能力。
/// </summary>
public sealed class ManagedDataCollectionScheduler(
    IServiceScopeFactory serviceScopeFactory,
    PlatformAdvisoryLock advisoryLock,
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
        // 两台实例同分钟各跑一轮时，IsDue 会基于同一条「最新记录」同时成立，
        // 结果是同一时刻采两份数据入库。用会话级咨询锁保证每轮只有一个执行者；
        // 抢不到锁的实例直接跳过本轮，下一个周期再来。
        await using IAsyncDisposable? lockHandle =
            await advisoryLock.TryAcquireAsync("managed-data-collection", cancellationToken);

        if (lockHandle is null)
        {
            logger.LogDebug("后台数据自动采集：另一实例正在执行本轮，跳过。");
            return;
        }

        await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        var platformService =
            scope.ServiceProvider.GetRequiredService<IDataAcquisitionPlatformService>();
        var executionService =
            scope.ServiceProvider.GetRequiredService<IManagedDataExecutionService>();
        var collectionPoints =
            scope.ServiceProvider.GetRequiredService<CollectionPointStateSynchronizer>();

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
                collectionPoints,
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
        CollectionPointStateSynchronizer collectionPoints,
        CancellationToken cancellationToken)
    {
        _lastAttemptTimes[definition.Id] = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "开始按计划采集后台数据 {Code}，采集频次 {IntervalSeconds} 秒。",
            definition.Code,
            definition.CollectionIntervalSeconds);

        ManagedDataExecutionResultDto result =
            await executionService.CollectAsync(definition.Id, cancellationToken);

        await collectionPoints.SyncAsync(
            definition.Code,
            definition.Name,
            definition.AcquisitionType,
            definition.ConnectionAddress,
            definition.IsEnabled,
            result.Success ? "在线" : "离线",
            result.Success ? null : result.ErrorMessage ?? result.StatusMessage,
            result.ExecutedAt,
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

using System.Text.Json;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示后台数据节点详情页的交互逻辑。
/// </summary>
public partial class ManagedDataDefinitionDetails : IAsyncDisposable
{
    /// <summary>
    /// 折线图统一呈现配置：数据点标记、加粗线条、坐标轴线与悬停提示。
    /// 纵轴标题取最近一条记录的单位，让量纲在图上一目了然。
    /// 无记录时不设标题（该属性仅在图表分支被求值，此处防御式判空避免越界）。
    /// </summary>
    private LineChartOptions ChartOptions =>
        PlatformChartDefaults.CreateLineOptions(
            HistoryTrendRecords.Length > 0 ? ResolveUnit(HistoryTrendRecords[^1].Unit) : null,
            "采集时间");

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() {WriteIndented = true};

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    private ManagedDataDefinitionDetailsDto? _details;
    private ManagedDataExecutionResultDto? _lastExecutionResult;
    private ManagedDataExecutionResultDto? _liveCurrentSnapshot;
    private CancellationTokenSource? _autoRefreshCts;
    private Task? _autoRefreshTask;
    private bool _isLoading = true;
    private bool _isExecuting;
    private bool _isAutoRefreshing;
    private bool _isInfoDrawerOpen;
    private bool _isEditorOpen;
    private int _historyLimit = 50;
    private int _historyRowsPerPage = 10;
    private int _secondsUntilNextRefresh;
    private string? _message;
    private string? _autoRefreshError;
    private Severity _messageSeverity = Severity.Info;
    private static readonly int[] HistoryPageSizeOptions = [10, 20, 50];

    private IReadOnlyDictionary<string, string> ConfigurationItems =>
        _details is null
            ? []
            : ParseJsonDictionary(_details.Definition.ConfigurationJson);

    private IReadOnlyDictionary<string, string> BusinessTagItems =>
        _details is null
            ? []
            : ParseJsonDictionary(_details.Definition.BusinessTagsJson);

    private CollectionDataRecordDto[] HistoryTrendRecords =>
        _details?.RecentRecords.OrderBy(item => item.CollectedAt).ToArray() ?? [];

    private List<ChartSeries<double>> HistoryChartSeries =>
        HistoryTrendRecords.Length == 0 || _details is null
            ? []
            :
            [
                new ChartSeries<double>
                {
                    Name = _details.Definition.Name,
                    Data = HistoryTrendRecords.Select(item => decimal.ToDouble(item.Value)).ToArray()
                }
            ];

    private string[] HistoryChartLabels => BuildHistoryChartLabels();

    private bool HasLiveCurrentSnapshot =>
        _liveCurrentSnapshot is {Success: true, ParsedValue: not null};

    private string CurrentValueText =>
        HasLiveCurrentSnapshot
            ? $"{_liveCurrentSnapshot!.ParsedValue!.Value:0.###} {ResolveUnit(_liveCurrentSnapshot.Unit)}".Trim()
            : FormatRecordValue(_details?.CurrentRecord);

    private string CurrentValueSourceText => HasLiveCurrentSnapshot ? "实时读取快照" : "数据库最近一次入库值";

    private string CurrentValueTimestampText =>
        HasLiveCurrentSnapshot
            ? FormatDateTime(_liveCurrentSnapshot!.SampledAt ?? _liveCurrentSnapshot.ExecutedAt)
            : FormatDateTime(_details?.CurrentRecord?.CollectedAt);

    private string CurrentValueFootnote =>
        HasLiveCurrentSnapshot
            ? $"自动刷新于 {FormatDateTime(_liveCurrentSnapshot!.ExecutedAt)}"
            : "当前还没有实时读取结果，展示的是已入库历史数据。";

    private string LatestTrendDeltaText
    {
        get
        {
            if (HistoryTrendRecords.Length < 2)
            {
                return "历史点位不足，暂无法计算波动趋势。";
            }

            CollectionDataRecordDto latest = HistoryTrendRecords[^1];
            CollectionDataRecordDto previous = HistoryTrendRecords[^2];
            var delta = latest.Value - previous.Value;
            var unit = ResolveUnit(latest.Unit);

            return delta switch
            {
                > 0 => $"较上一点上升 +{delta:0.###} {unit}".Trim(),
                < 0 => $"较上一点下降 {delta:0.###} {unit}".Trim(),
                _ => "较上一点无变化"
            };
        }
    }

    private string HistoryRangeText
    {
        get
        {
            if (HistoryTrendRecords.Length == 0)
            {
                return "--";
            }

            var min = HistoryTrendRecords.Min(item => item.Value);
            var max = HistoryTrendRecords.Max(item => item.Value);
            var unit = ResolveUnit(HistoryTrendRecords[^1].Unit);
            return $"{min:0.###} - {max:0.###} {unit}".Trim();
        }
    }

    private string AutoRefreshSummary =>
        _details is null
            ? "--"
            : _details.Definition.IsEnabled
                ? $"当前节点已开启自动轮询，系统会每 {FormatInterval(_details.Definition.CollectionIntervalSeconds)} 执行一次实时读取。"
                : "当前节点已停用，自动刷新已暂停。";

    private string NextRefreshDisplay =>
        _details is null || !_details.Definition.IsEnabled
            ? "已暂停"
            : _isAutoRefreshing
                ? "刷新中..."
                : _secondsUntilNextRefresh <= 0
                    ? "即将刷新"
                    : $"{_secondsUntilNextRefresh} 秒后";

    private double AutoRefreshProgress
    {
        get
        {
            if (_details is null || !_details.Definition.IsEnabled)
            {
                return 0;
            }

            var interval = GetRefreshIntervalSeconds();
            if (_isAutoRefreshing)
            {
                return 100;
            }

            return Math.Clamp((interval - _secondsUntilNextRefresh) * 100d / interval, 0, 100);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        _liveCurrentSnapshot = null;
        _lastExecutionResult = null;
        _autoRefreshError = null;
        await LoadAsync();
    }

    /// <summary>
    /// 载入节点详情。实时读取属于「锦上添花」的补充数据，且采集服务器不可达时会等到超时，
    /// 因此先渲染详情再在后台刷新当前值，避免首屏被上游超时阻塞。
    /// </summary>
    private async Task LoadAsync(bool skipLiveRefresh = false)
    {
        _isLoading = true;
        _message = null;

        try
        {
            _details = await PlatformApiClient.GetManagedDataDefinitionDetailsAsync(Id, _historyLimit);
            RestartAutoRefreshLoop();

            if (!skipLiveRefresh && _details.Definition.IsEnabled && !HasLiveCurrentSnapshot)
            {
                _ = RefreshCurrentValueInBackgroundAsync();
            }
        }
        catch (Exception ex)
        {
            _details = null;
            StopAutoRefreshLoop();
            _message = $"节点详情加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task ExecuteAsync(bool isCollect)
    {
        if (_details is null)
        {
            return;
        }

        _isExecuting = true;
        _message = null;

        try
        {
            _lastExecutionResult = isCollect
                ? await PlatformApiClient.CollectManagedDataDefinitionAsync(_details.Definition.Id)
                : await PlatformApiClient.DebugReadManagedDataDefinitionAsync(_details.Definition.Id);

            if (_lastExecutionResult.Success && _lastExecutionResult.ParsedValue.HasValue)
            {
                _liveCurrentSnapshot = _lastExecutionResult;
                _autoRefreshError = null;
                ResetCountdown();
            }

            _message = _lastExecutionResult.Success
                ? _lastExecutionResult.StatusMessage
                : $"执行失败：{_lastExecutionResult.ErrorMessage ?? _lastExecutionResult.StatusMessage}";
            _messageSeverity = _lastExecutionResult.Success ? Severity.Success : Severity.Error;

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"节点执行失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private void GoBack()
    {
        NavigationManager.NavigateTo("/managed-data-definitions");
    }

    private void OpenInfoDrawer()
    {
        _isInfoDrawerOpen = true;
    }

    private void CloseInfoDrawer()
    {
        _isInfoDrawerOpen = false;
    }

    /// <summary>打开共享编辑抽屉，节点标识由路由参数 Id 提供。</summary>
    private void OpenEditor()
    {
        _isEditorOpen = true;
    }

    /// <summary>编辑保存成功后刷新详情，让当前值、历史趋势与配置摘要立即反映最新配置。</summary>
    private async Task OnEditorSaved(ManagedDataDefinitionDto saved)
    {
        _message = $"采集节点 {saved.Code} 已保存。";
        _messageSeverity = Severity.Success;
        await LoadAsync(true);
    }

    private async Task OnHistoryLimitChanged(int value)
    {
        _historyLimit = value;
        await LoadAsync();
    }

    /// <summary>
    /// 在后台补一次实时读取。失败只记录到自动刷新提示，不影响详情主体渲染。
    /// </summary>
    private async Task RefreshCurrentValueInBackgroundAsync()
    {
        try
        {
            await RefreshCurrentValueAsync(false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _autoRefreshError = $"实时读取失败：{UserFacingErrors.Describe(ex)}";
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshCurrentValueAsync(bool setStatusMessage, CancellationToken cancellationToken)
    {
        if (_details is null || !_details.Definition.IsEnabled)
        {
            return;
        }

        _isAutoRefreshing = true;

        try
        {
            ManagedDataExecutionResultDto result =
                await PlatformApiClient.DebugReadManagedDataDefinitionAsync(_details.Definition.Id, cancellationToken);

            if (result is {Success: true, ParsedValue: not null})
            {
                _liveCurrentSnapshot = result;
                _lastExecutionResult = result;
                _autoRefreshError = null;
            }
            else
            {
                _autoRefreshError = result.ErrorMessage ?? result.StatusMessage;
            }

            if (setStatusMessage)
            {
                _message = result.Success
                    ? result.StatusMessage
                    : $"实时刷新失败：{result.ErrorMessage ?? result.StatusMessage}";
                _messageSeverity = result.Success ? Severity.Success : Severity.Warning;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 页面离开或节点被停用时的正常取消，不作为错误提示。
        }
        catch (OperationCanceledException)
        {
            // 上游 HTTP 超时会以 TaskCanceledException 抛出，转成可读的连接提示。
            _autoRefreshError = "实时读取超时，请检查采集服务器是否可达。";
        }
        catch (Exception ex)
        {
            _autoRefreshError = $"实时读取失败：{UserFacingErrors.Describe(ex)}";

            if (setStatusMessage)
            {
                _message = _autoRefreshError;
                _messageSeverity = Severity.Warning;
            }
        }
        finally
        {
            _isAutoRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void RestartAutoRefreshLoop()
    {
        StopAutoRefreshLoop();

        if (_details is null || !_details.Definition.IsEnabled)
        {
            _secondsUntilNextRefresh = 0;
            return;
        }

        ResetCountdown();
        _autoRefreshCts = new CancellationTokenSource();
        _autoRefreshTask = RunAutoRefreshLoopAsync(_autoRefreshCts.Token);
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                await ProcessAutoRefreshTickAsync(cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAutoRefreshTickAsync(CancellationToken cancellationToken)
    {
        if (!CanAutoRefresh())
        {
            _secondsUntilNextRefresh = 0;
            return;
        }

        DecrementRefreshCountdown();

        if (!ShouldRefreshCurrentValue())
        {
            return;
        }

        await RefreshCurrentValueAsync(false, cancellationToken);
        ResetCountdown();
    }

    private bool CanAutoRefresh()
    {
        return _details is not null && _details.Definition.IsEnabled;
    }

    private void DecrementRefreshCountdown()
    {
        if (_secondsUntilNextRefresh > 0)
        {
            _secondsUntilNextRefresh--;
        }
    }

    private bool ShouldRefreshCurrentValue()
    {
        return _secondsUntilNextRefresh == 0 &&
               !_isLoading &&
               !_isExecuting &&
               !_isAutoRefreshing;
    }

    private void StopAutoRefreshLoop()
    {
        if (_autoRefreshCts is null)
        {
            return;
        }

        _autoRefreshCts.Cancel();
        _autoRefreshCts.Dispose();
        _autoRefreshCts = null;
        _autoRefreshTask = null;
    }

    private int GetRefreshIntervalSeconds()
    {
        return Math.Max(1, _details?.Definition.CollectionIntervalSeconds ?? 60);
    }

    private void ResetCountdown()
    {
        _secondsUntilNextRefresh = GetRefreshIntervalSeconds();
    }

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return rawDictionary?.ToDictionary(
                item => item.Key,
                item => item.Value.ValueKind == JsonValueKind.String
                    ? item.Value.GetString() ?? string.Empty
                    : item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string[] BuildHistoryChartLabels()
    {
        if (HistoryTrendRecords.Length == 0)
        {
            return [];
        }

        if (HistoryTrendRecords.Length <= 8)
        {
            return HistoryTrendRecords
                .Select(item => item.CollectedAt.ToLocalTime().ToString("HH:mm:ss"))
                .ToArray();
        }

        var step = Math.Max(1, (int) Math.Ceiling((HistoryTrendRecords.Length - 1) / 9d));

        return HistoryTrendRecords
            .Select((item, index) => index == 0 || index == HistoryTrendRecords.Length - 1 || index % step == 0
                ? item.CollectedAt.ToLocalTime().ToString("HH:mm")
                : string.Empty)
            .ToArray();
    }

    private static string FormatInterval(int intervalSeconds)
    {
        if (intervalSeconds >= 3600 && intervalSeconds % 3600 == 0)
        {
            return $"{intervalSeconds / 3600} 小时";
        }

        return intervalSeconds >= 60 && intervalSeconds % 60 == 0
            ? $"{intervalSeconds / 60} 分钟"
            : $"{intervalSeconds} 秒";
    }

    private static string ResolveUnit(string? unit)
    {
        return string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim();
    }

    private static string FormatRecordValue(CollectionDataRecordDto? record)
    {
        return record is null ? "--" : $"{record.Value:0.###} {ResolveUnit(record.Unit)}".Trim();
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
    }

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, JsonSerializerOptions);
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// 节点运行状态取色：停用为琥珀，在线为绿，启用但未连上为警示。
    /// 按设计规范不用灰色表达业务状态。
    /// </summary>
    private static Color GetStatusColor(bool isEnabled, CollectionPointDto? collectionPoint)
    {
        if (!isEnabled)
        {
            return Color.Warning;
        }

        return collectionPoint?.CommunicationStatus == "在线" ? Color.Success : Color.Warning;
    }

    public async ValueTask DisposeAsync()
    {
        StopAutoRefreshLoop();

        if (_autoRefreshTask is not null)
        {
            try
            {
                await _autoRefreshTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}

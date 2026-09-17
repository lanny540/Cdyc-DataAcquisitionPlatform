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
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

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
    private int _historyLimit = 50;
    private int _secondsUntilNextRefresh;
    private string? _message;
    private string? _autoRefreshError;
    private Severity _messageSeverity = Severity.Info;

    private IReadOnlyDictionary<string, string> ConfigurationItems =>
        _details is null
            ? new Dictionary<string, string>()
            : ParseJsonDictionary(_details.Definition.ConfigurationJson);

    private IReadOnlyDictionary<string, string> BusinessTagItems =>
        _details is null
            ? new Dictionary<string, string>()
            : ParseJsonDictionary(_details.Definition.BusinessTagsJson);

    private IReadOnlyList<CollectionDataRecordDto> HistoryTrendRecords =>
        _details?.RecentRecords.OrderBy(item => item.CollectedAt).ToArray() ?? [];

    private List<ChartSeries<double>> HistoryChartSeries =>
        HistoryTrendRecords.Count == 0 || _details is null
            ? []
            :
            [
                new ChartSeries<double>
                {
                    Name = _details.Definition.Name,
                    Data = HistoryTrendRecords.Select(item => decimal.ToDouble(item.Value)).ToArray()
                }
            ];

    private string[] HistoryChartLabels =>
        HistoryTrendRecords.Select(item => item.CollectedAt.ToLocalTime().ToString("HH:mm:ss")).ToArray();

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
            if (HistoryTrendRecords.Count < 2)
            {
                return "历史点位不足，暂无法计算波动趋势。";
            }

            CollectionDataRecordDto latest = HistoryTrendRecords[^1];
            CollectionDataRecordDto previous = HistoryTrendRecords[^2];
            decimal delta = latest.Value - previous.Value;
            string unit = ResolveUnit(latest.Unit);

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
            if (HistoryTrendRecords.Count == 0)
            {
                return "--";
            }

            decimal min = HistoryTrendRecords.Min(item => item.Value);
            decimal max = HistoryTrendRecords.Max(item => item.Value);
            string unit = ResolveUnit(HistoryTrendRecords[^1].Unit);
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

            int interval = GetRefreshIntervalSeconds();
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

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

        try
        {
            _details = await PlatformApiClient.GetManagedDataDefinitionDetailsAsync(Id, _historyLimit);
            RestartAutoRefreshLoop();

            if (_details.Definition.IsEnabled && !HasLiveCurrentSnapshot)
            {
                await RefreshCurrentValueAsync(setStatusMessage: false, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _details = null;
            StopAutoRefreshLoop();
            _message = $"节点详情加载失败：{ex.Message}";
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
            _message = $"节点执行失败：{ex.Message}";
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

    private async Task OnHistoryLimitChanged(int value)
    {
        _historyLimit = value;
        await LoadAsync();
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
                _message = result.Success ? result.StatusMessage : $"实时刷新失败：{result.ErrorMessage ?? result.StatusMessage}";
                _messageSeverity = result.Success ? Severity.Success : Severity.Warning;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _autoRefreshError = ex.Message;

            if (setStatusMessage)
            {
                _message = $"实时刷新失败：{ex.Message}";
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

                if (_details is null || !_details.Definition.IsEnabled)
                {
                    _secondsUntilNextRefresh = 0;
                    continue;
                }

                if (_secondsUntilNextRefresh > 0)
                {
                    _secondsUntilNextRefresh--;
                }

                if (_secondsUntilNextRefresh == 0)
                {
                    if (_isLoading || _isExecuting || _isAutoRefreshing)
                    {
                        continue;
                    }

                    await RefreshCurrentValueAsync(setStatusMessage: false, cancellationToken);
                    ResetCountdown();
                }

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
            Dictionary<string, JsonElement>? rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return rawDictionary?.ToDictionary(
                item => item.Key,
                item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
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

    private static Color GetStatusColor(bool isEnabled, CollectionPointDto? collectionPoint)
    {
        if (!isEnabled)
        {
            return Color.Default;
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
    }
}

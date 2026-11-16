using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 实时采集数据页的交互逻辑。
///
/// 与桌面端同名页面同源：数据来自同一个 <c>/api/collection-data</c> 接口，
/// 条数档位（50/100/200/500）与刷新频率（30 秒 / 1 / 3 / 5 分钟）保持一致，
/// 现场在两端看到的读数口径不会出现差异。
/// </summary>
public partial class CollectionData
{
    /// <summary>
    /// 可选返回条数。上限 500 与服务端 <c>ingest/query</c> 的夹取范围一致。
    /// </summary>
    private static readonly int[] LimitOptions = [50, 100, 200, 500];

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private CollectionDataRecordDto[] _records = [];

    /// <summary>
    /// 当前返回条数。
    /// </summary>
    private int _limit = 100;

    /// <summary>
    /// 首载遮罩。
    /// </summary>
    private bool _isLoading = true;

    /// <summary>
    /// 静默刷新中：不换遮罩，只显示控制条上的一枚小型进度圈。
    /// </summary>
    private bool _isRefreshing;

    /// <summary>
    /// 自动刷新开关。实时页默认开启：这页的存在意义就是盯现场读数，
    /// 与能耗页「默认关闭」的取舍不同。
    /// </summary>
    private bool _autoRefreshEnabled = true;

    /// <summary>
    /// 自动刷新间隔（秒），默认 1 分钟。
    /// </summary>
    private int _refreshIntervalSeconds = 60;

    /// <summary>
    /// 最近一次成功加载时间，用于「数据截至 HH:mm:ss」。
    /// </summary>
    private DateTimeOffset? _lastUpdatedAt;

    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    /// <summary>
    /// DTO 集合声明为 IReadOnlyList，表格与计数统一用数组。
    /// </summary>
    private CollectionDataRecordDto[] Records => _records;

    private string SummaryText => Records.Length == 0
        ? "暂无采集记录"
        : $"共 {Records.Length} 条记录";

    private string LatestCollectedText => Records.Length == 0
        ? "--"
        : FormatCollectedAt(Records[0].CollectedAt);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// 首载与手动刷新：整页遮罩，失败时清空表格。
    /// </summary>
    private Task LoadAsync()
    {
        return LoadInternalAsync(silent: false);
    }

    /// <summary>
    /// 定时刷新：不换遮罩、不清表格，失败只出一条警告提示。
    /// </summary>
    private Task RefreshSilentlyAsync()
    {
        return LoadInternalAsync(silent: true);
    }

    /// <param name="silent">true 表示定时触发的静默刷新。</param>
    private async Task LoadInternalAsync(bool silent)
    {
        // 首载未结束时定时器可能已经跑到第一拍：两个请求并行会互相覆盖结果。
        if (silent && (_isLoading || _isRefreshing))
        {
            return;
        }

        if (silent)
        {
            _isRefreshing = true;
        }
        else
        {
            _isLoading = true;
            _message = null;
        }

        try
        {
            IReadOnlyList<CollectionDataRecordDto> records =
                await PlatformApiClient.GetCollectionDataAsync(_limit);

            _records = records
                .OrderByDescending(item => item.CollectedAt)
                .ToArray();

            _lastUpdatedAt = DateTimeOffset.Now;
            _message = null;
        }
        catch (Exception ex)
        {
            // 静默刷新失败保留上一批记录：定时器跑在用户没操作的时刻，
            // 一次瞬时抖动把现场读数清空比展示略旧的数据糟糕得多。
            _message = silent
                ? $"自动刷新未成功，表格仍显示上次加载的记录：{UserFacingErrors.Describe(ex)}"
                : $"采集记录加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = silent ? Severity.Warning : Severity.Error;

            if (!silent)
            {
                _records = [];
            }
        }
        finally
        {
            if (silent)
            {
                _isRefreshing = false;
            }
            else
            {
                _isLoading = false;
            }
        }
    }

    private async Task OnLimitChangedAsync(int value)
    {
        _limit = value;
        await LoadAsync();
    }

    /// <summary>
    /// 采集时间统一按本地时区展示：服务端存的是带偏移的时间戳，
    /// 现场核对读数时不希望看到 UTC。
    /// </summary>
    private static string FormatCollectedAt(DateTimeOffset collectedAt)
    {
        return collectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 与桌面端一致：超过一千用千分位整数，否则保留两位小数。
    /// </summary>
    private static string FormatValue(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0")
            : value.ToString("0.##");
    }
}

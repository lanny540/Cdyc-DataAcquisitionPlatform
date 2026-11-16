using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 实时采集数据：最新入库的采集记录，只读。
///
/// 进入页面后按所选刷新频率轮询（30 秒 / 1 分钟 / 3 分钟 / 5 分钟，默认 1 分钟），
/// 切离页面立即停表；切回时若数据已超出 <see cref="ReactivationFreshnessWindow"/> 才重拉，
/// 手动刷新或改条数会重新开始计时，避免刚点完刷新又被定时器打一次。
/// </summary>
public sealed partial class CollectionDataPageViewModel : PageViewModelBase
{
    /// <summary>
    /// 默认刷新频率（1 分钟）。兼顾现场变化的可察觉性与 500 条明细的查询成本。
    /// </summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 切回页面时的数据新鲜度窗口。窗口内的数据视为仍然有效，直接复用。
    /// 用户快速来回切换（点错、找页面）时，不会把每次切页都变成一次请求；
    /// 需要绝对「切回即拉」时把它设为 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    public static readonly TimeSpan ReactivationFreshnessWindow = TimeSpan.FromSeconds(5);

    private readonly DesktopSession _session;
    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isActive;

    /// <summary>
    /// 初始化页面。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public CollectionDataPageViewModel(DesktopSession session)
    {
        _session = session;
        LimitOptions = new ObservableCollection<int>([50, 100, 200, 500]);
        RefreshIntervalOptions = new ObservableCollection<RefreshIntervalOption>(
        [
            new(30, "30 秒"),
            new(60, "1 分钟"),
            new(180, "3 分钟"),
            new(300, "5 分钟")
        ]);

        _autoRefreshTimer = new DispatcherTimer { Interval = DefaultRefreshInterval };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;

        // 在下拉项与定时器都就绪后再落默认值，避免变更回调里访问未初始化的字段。
        SelectedRefreshInterval =
            RefreshIntervalOptions.First(option => option.Interval == DefaultRefreshInterval);
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.CollectionData;

    /// <inheritdoc />
    public override string Title => "实时采集数据";

    /// <inheritdoc />
    public override string Description => "按采集时间倒序展示最新入库记录，用于核对现场读数。";

    /// <inheritdoc />
    public override string Accent => "info";

    /// <summary>
    /// 可选返回条数。
    /// </summary>
    public ObservableCollection<int> LimitOptions { get; }

    /// <summary>
    /// 可选刷新频率。
    /// </summary>
    public ObservableCollection<RefreshIntervalOption> RefreshIntervalOptions { get; }

    /// <summary>
    /// 当前刷新频率。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoRefreshHint))]
    private RefreshIntervalOption? _selectedRefreshInterval;

    /// <summary>
    /// 返回条数。
    /// </summary>
    [ObservableProperty]
    private int _limit = 100;

    /// <summary>
    /// 是否自动刷新。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoRefreshHint))]
    private bool _autoRefreshEnabled = true;

    /// <summary>
    /// 自动刷新状态说明。
    /// </summary>
    public string AutoRefreshHint => AutoRefreshEnabled
        ? $"每 {SelectedRefreshInterval?.Label ?? "1 分钟"}自动刷新"
        : "自动刷新已暂停，仅手动刷新";

    /// <summary>
    /// 是否有数据。
    /// </summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>
    /// 汇总文案。
    /// </summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>
    /// 记录行。
    /// </summary>
    public ObservableCollection<CollectionRecordRowModel> Rows { get; } = [];

    /// <inheritdoc />
    public override async Task OnActivatedAsync()
    {
        _isActive = true;

        if (!IsLoaded)
        {
            await EnsureLoadedAsync();
        }
        else if (!IsFreshWithin(ReactivationFreshnessWindow))
        {
            // 超出新鲜窗口才重拉：快速来回切页时复用现有数据，避免把切页变成请求风暴。
            await ReloadAsync();
        }

        UpdateTimerState();
    }

    /// <inheritdoc />
    public override void OnDeactivated()
    {
        _isActive = false;
        UpdateTimerState();
    }

    /// <summary>
    /// 条数变化后重新查询。
    /// </summary>
    partial void OnLimitChanged(int value)
    {
        _ = ReloadAsync();
    }

    /// <summary>
    /// 开关变化后启停定时器。
    /// </summary>
    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        UpdateTimerState();
    }

    /// <summary>
    /// 切换刷新频率：换挡立即生效，并按新间隔重新计时。
    /// </summary>
    partial void OnSelectedRefreshIntervalChanged(RefreshIntervalOption? value)
    {
        if (value is null)
        {
            return;
        }

        _autoRefreshTimer.Interval = value.Interval;
        RestartCountdown();
    }

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        IReadOnlyList<CollectionDataRecordDto> records = await _session.Api.GetCollectionDataAsync(Limit);

        List<CollectionRecordRowModel> rows = records
            .OrderByDescending(item => item.CollectedAt)
            .Select(item => new CollectionRecordRowModel(
                item.CollectionPointCode,
                item.CollectionPointName,
                item.MetricName,
                MetricFormat.Value(item.Value),
                item.Unit,
                item.CollectedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")))
            .ToList();

        // 增量同步而不是清空重填：自动刷新时表格不会闪、滚动位置也不会被打回顶部。
        RowSync.Apply(Rows, rows);

        HasData = rows.Count > 0;
        SummaryText = rows.Count == 0
            ? "暂无采集记录"
            : $"共 {rows.Count} 条记录，最新一条采集于 {rows[0].CollectedAt}";

        RestartCountdown();
    }

    /// <summary>
    /// 按当前状态启停定时器。页面不可见时绝不运行，否则会在后台持续请求接口。
    /// </summary>
    private void UpdateTimerState()
    {
        if (_isActive && AutoRefreshEnabled)
        {
            _autoRefreshTimer.Start();
            return;
        }

        _autoRefreshTimer.Stop();
    }

    /// <summary>
    /// 重新开始倒计时（只对已启动的定时器有效）。
    /// </summary>
    private void RestartCountdown()
    {
        if (!_autoRefreshTimer.IsEnabled)
        {
            return;
        }

        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    private void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (!_isActive || IsLoading)
        {
            return;
        }

        _ = ReloadAsync();
    }
}

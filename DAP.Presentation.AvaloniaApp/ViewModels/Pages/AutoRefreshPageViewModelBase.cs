using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Presentation.AvaloniaApp.Common;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 带定时刷新的页面基类：刷新开关、频率档位与「数据截至」时间。
///
/// 四个能耗页共用这一套逻辑，避免各写一遍定时器而互相漂移；
/// 排班日历不继承本类——它一天只变一次，自动刷新只是白白打接口。
///
/// 生命周期：页面切离时停表（<see cref="OnDeactivated"/>），切回且开关打开时再起表。
/// 这是「不在后台持续请求」的唯一保证。
/// </summary>
public abstract partial class AutoRefreshPageViewModelBase : PageViewModelBase
{
    /// <summary>
    /// 默认刷新频率（1 分钟）。与 Web 端能耗页、桌面端实时采集页同一档默认值。
    /// </summary>
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isActive;

    /// <summary>
    /// 初始化基类。
    /// </summary>
    protected AutoRefreshPageViewModelBase()
    {
        RefreshIntervalOptions = new ObservableCollection<RefreshIntervalOption>(
        [
            new RefreshIntervalOption(30, "30 秒"),
            new RefreshIntervalOption(60, "1 分钟"),
            new RefreshIntervalOption(180, "3 分钟"),
            new RefreshIntervalOption(300, "5 分钟")
        ]);

        _autoRefreshTimer = new DispatcherTimer {Interval = DefaultRefreshInterval};
        _autoRefreshTimer.Tick += OnAutoRefreshTick;

        // 在下拉项与定时器都就绪后再落默认值，避免变更回调里访问未初始化的字段。
        SelectedRefreshInterval =
            RefreshIntervalOptions.First(option => option.Interval == DefaultRefreshInterval);
    }

    /// <summary>
    /// 可选刷新频率。
    /// </summary>
    public ObservableCollection<RefreshIntervalOption> RefreshIntervalOptions { get; }

    /// <summary>
    /// 当前刷新频率。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AutoRefreshHint))]
    private RefreshIntervalOption? _selectedRefreshInterval;

    /// <summary>
    /// 是否自动刷新。默认关闭：能耗页常驻时不该无条件持续打接口，
    /// 需要看板常驻的用户自己打开（与 Web 端能耗页同一取舍）。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AutoRefreshHint))]
    private bool _autoRefreshEnabled;

    /// <summary>
    /// 数据新鲜度文案。首次加载前显示占位符，而不是给一个看似有效的时间。
    /// </summary>
    public string LastUpdatedText => LastLoadedAt is { } loadedAt
        ? $"数据截至 {loadedAt:HH:mm:ss}"
        : "数据截至 --:--:--";

    /// <summary>
    /// 自动刷新状态说明。
    /// </summary>
    public string AutoRefreshHint => AutoRefreshEnabled
        ? $"每 {SelectedRefreshInterval?.Label ?? "1 分钟"}自动刷新"
        : "自动刷新已暂停";

    /// <inheritdoc />
    public override async Task OnActivatedAsync()
    {
        _isActive = true;
        await base.OnActivatedAsync();
        UpdateTimerState();
    }

    /// <inheritdoc />
    public override void OnDeactivated()
    {
        _isActive = false;
        UpdateTimerState();
    }

    /// <inheritdoc />
    protected override void OnLoaded()
    {
        base.OnLoaded();
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(AutoRefreshHint));

        // 任何一次成功加载都重新计时：手动刷新之后不该立刻又被定时器打一次。
        RestartCountdown();
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
        if (value is null) return;

        _autoRefreshTimer.Interval = value.Interval;
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
    /// 重新开始计时（只对已启动的定时器有效）。
    /// </summary>
    private void RestartCountdown()
    {
        if (!_autoRefreshTimer.IsEnabled) return;

        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    private void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (!_isActive || IsLoading) return;

        _ = ReloadAsync();
    }
}
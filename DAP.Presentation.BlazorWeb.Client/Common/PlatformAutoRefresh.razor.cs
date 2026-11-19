using Microsoft.AspNetCore.Components;

namespace DAP.Presentation.BlazorWeb.Client.Common;

/// <summary>
/// 数据新鲜度与自动刷新控制条。
///
/// 只负责「什么时候该拉一次」，不负责怎么拉：到点后回调 <see cref="Refresh"/>，
/// 由页面决定是静默刷新（保留旧数据）还是走首载遮罩。这样四个能耗页与实时采集页
/// 共享同一套计时逻辑，不会各自写一份 <c>Task.Delay</c> 循环而互相漂移。
///
/// 生命周期：循环在 <c>OnInitialized</c> 启动，在 <c>DisposeAsync</c> 取消。
/// 页面切走后组件被销毁，循环随之停止——这是「不在后台持续请求」的唯一保证，
/// 不能只靠页面把开关关掉。
/// </summary>
public partial class PlatformAutoRefresh : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// 可选刷新频率（秒）。与桌面端「实时采集数据」页保持同一组档位。
    /// </summary>
    private static readonly int[] SupportedIntervals = [30, 60, 180, 300];

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _secondsRemaining;
    private bool _lastEnabled;
    private int _lastInterval;
    private DateTimeOffset? _lastUpdatedSnapshot;

    /// <summary>
    /// 是否启用自动刷新。默认关闭：能耗页开着不动时不该持续打接口，
    /// 需要「看板常驻」的用户自己打开；实时采集页由页面显式传 true。
    /// </summary>
    [Parameter]
    public bool Enabled { get; set; }

    /// <summary>
    /// 开关变化回调，配合 <c>@bind-Enabled</c> 使用。
    /// </summary>
    [Parameter]
    public EventCallback<bool> EnabledChanged { get; set; }

    /// <summary>
    /// 刷新间隔（秒）。
    /// </summary>
    [Parameter]
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 间隔变化回调，配合 <c>@bind-IntervalSeconds</c> 使用。
    /// </summary>
    [Parameter]
    public EventCallback<int> IntervalSecondsChanged { get; set; }

    /// <summary>
    /// 最近一次成功加载时间；为空表示本页尚未取到数据。
    /// </summary>
    [Parameter]
    public DateTimeOffset? LastUpdatedAt { get; set; }

    /// <summary>
    /// 页面是否正在刷新。为 true 时这一拍跳过，避免两次请求互相覆盖结果。
    /// </summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>
    /// 到点回调。页面应在此做静默刷新（不遮罩、失败保留旧数据）。
    /// </summary>
    [Parameter]
    public EventCallback Refresh { get; set; }

    private string LastUpdatedText => LastUpdatedAt is { } time
        ? time.ToLocalTime().ToString("HH:mm:ss")
        : "--:--:--";

    private string CountdownText => !Enabled
        ? "自动刷新已暂停"
        : _secondsRemaining <= 0
            ? "即将刷新"
            : $"{_secondsRemaining} 秒后刷新";

    private static int[] IntervalOptions => SupportedIntervals;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
    }

    /// <summary>
    /// 换挡或开关切换后按新间隔重新计时。
    ///
    /// 只在参数真的变化时重置：父组件每次重渲染都会调到这里，
    /// 无条件重置会让倒计时永远停在起点，自动刷新看起来「开着但不动」。
    /// </summary>
    protected override void OnParametersSet()
    {
        bool intervalChanged = IntervalSeconds != _lastInterval;
        bool enabledChanged = Enabled != _lastEnabled;

        if (intervalChanged || enabledChanged)
        {
            _lastInterval = IntervalSeconds;
            _lastEnabled = Enabled;
            _secondsRemaining = Math.Max(1, IntervalSeconds);
        }
        else if (LastUpdatedAt != _lastUpdatedSnapshot)
        {
            // 手动刷新（或上一次定时刷新）完成后重新计时：
            // 否则用户刚点完「刷新」，几秒后又被定时器打一次。
            _secondsRemaining = Math.Max(1, IntervalSeconds);
        }

        _lastUpdatedSnapshot = LastUpdatedAt;
    }

    /// <summary>
    /// 每秒一拍：递减倒计时，到 0 且页面空闲时触发一次刷新。
    ///
    /// 用 <c>Task.Delay</c> 而不是 <c>PeriodicTimer</c>：后者在 WASM 单线程下
    /// 与渲染共用调度，仍要 <c>InvokeAsync</c> 回到渲染上下文，收益不足以抵消多一个类型的复杂度。
    /// </summary>
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                if (!Enabled) continue;

                if (_secondsRemaining > 0) _secondsRemaining--;

                if (_secondsRemaining == 0 && !IsBusy)
                {
                    try
                    {
                        await InvokeAsync(async () => await Refresh.InvokeAsync());
                    }
                    catch (ObjectDisposedException)
                    {
                        // 组件已随页面销毁，循环没有继续的意义。
                        return;
                    }
                    catch (Exception)
                    {
                        // 刷新失败由页面自己用提示条告知用户；计时器必须继续走，
                        // 否则一次网络抖动就会让「自动刷新」永久停表，而界面上开关还显示为开。
                    }

                    _secondsRemaining = Math.Max(1, IntervalSeconds);
                }

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task OnEnabledChangedAsync(bool value)
    {
        Enabled = value;
        _lastEnabled = value;
        _secondsRemaining = Math.Max(1, IntervalSeconds);
        await EnabledChanged.InvokeAsync(value);
    }

    private async Task OnIntervalChangedAsync(int value)
    {
        IntervalSeconds = value;
        _lastInterval = value;
        _secondsRemaining = Math.Max(1, value);
        await IntervalSecondsChanged.InvokeAsync(value);
    }

    /// <summary>
    /// 分钟档加单位，避免下拉框里出现「180」这种要用户心算的选项。
    /// </summary>
    /// <param name="seconds">间隔秒数。</param>
    /// <returns>显示文案。</returns>
    private static string FormatInterval(int seconds)
    {
        return seconds >= 60 && seconds % 60 == 0
            ? $"{seconds / 60} 分钟"
            : $"{seconds} 秒";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_loopCts is null) return;

        await _loopCts.CancelAsync();

        if (_loopTask is not null)
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }

        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }
}
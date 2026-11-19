using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Presentation.AvaloniaApp.Navigation;

namespace DAP.Presentation.AvaloniaApp.ViewModels;

/// <summary>
/// 业务页面的公共基类：统一加载态、错误提示与刷新命令。
/// </summary>
public abstract partial class PageViewModelBase : ViewModelBase
{
    private bool _loaded;

    /// <summary>
    /// 初始化页面基类。
    /// </summary>
    protected PageViewModelBase()
    {
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(true));
    }

    /// <summary>
    /// 页面标识，必须与 <see cref="PageKeys"/> 中的常量一致。
    /// </summary>
    public abstract string PageKey { get; }

    /// <summary>
    /// 页面标题（顶栏与页头共用）。
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// 页面说明。
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// 顶部主色（如 <c>success</c>、<c>tertiary</c>），与 Web 端「页面主色分配」表一致。
    /// </summary>
    public abstract string Accent { get; }

    /// <summary>
    /// 页面头部图标。直接取导航目录里登记的路径数据，避免页面再复制一份图标。
    /// </summary>
    public string HeaderIcon => DesktopNavigationCatalog.FindItem(PageKey)?.Icon ?? string.Empty;

    /// <summary>
    /// 是否正在加载。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    /// <summary>
    /// 状态提示（错误或空数据说明）；为空时不显示提示条。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    /// <summary>
    /// 提示条是否为错误级别。
    /// </summary>
    [ObservableProperty] private bool _isError;

    /// <summary>
    /// 是否空闲（用于按钮可用性）。
    /// </summary>
    public bool IsNotLoading => !IsLoading;

    /// <summary>
    /// 是否有提示信息。
    /// </summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// 刷新命令。
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// 是否已完成过一次加载。
    /// </summary>
    protected bool IsLoaded => _loaded;

    /// <summary>
    /// 最近一次成功加载的时间；从未加载过时为 <c>null</c>。
    /// </summary>
    protected DateTimeOffset? LastLoadedAt { get; private set; }

    /// <summary>
    /// 数据是否在指定窗口内刚加载过。用于「切回页面」这类场景的防抖：
    /// 用户快速来回切换时，窗口内的数据直接复用，不必每次都打一次接口。
    /// </summary>
    /// <param name="window">新鲜度窗口。</param>
    /// <returns>窗口内加载过返回 true。</returns>
    protected bool IsFreshWithin(TimeSpan window)
    {
        return LastLoadedAt is { } loadedAt && DateTimeOffset.Now - loadedAt < window;
    }

    /// <summary>
    /// 首次进入页面时加载；已加载过则直接返回，用户要最新数据时点刷新。
    /// </summary>
    /// <returns>加载任务。</returns>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await LoadAsync(false);
    }

    /// <summary>
    /// 外壳切到本页时调用。默认只在首次进入时加载；需要「切回即刷新」的页面可重写。
    /// </summary>
    /// <returns>加载任务。</returns>
    public virtual Task OnActivatedAsync()
    {
        return EnsureLoadedAsync();
    }

    /// <summary>
    /// 外壳切离本页时调用。默认无动作；带定时器的页面必须在这里停掉，否则会在后台持续请求。
    /// </summary>
    public virtual void OnDeactivated()
    {
    }

    /// <summary>
    /// 执行一次加载。
    /// </summary>
    /// <param name="force">为 true 时忽略「已加载」标记，表示用户主动刷新。</param>
    /// <returns>加载任务。</returns>
    private async Task LoadAsync(bool force)
    {
        if (IsLoading) return;

        if (_loaded && !force) return;

        IsLoading = true;
        Message = null;
        IsError = false;

        try
        {
            await LoadCoreAsync();
            _loaded = true;
            LastLoadedAt = DateTimeOffset.Now;
            OnLoaded();
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = UserFacingErrors.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 加载成功后调用。供派生类通知「数据截至」这类依赖加载时间的派生属性——
    /// <see cref="LastLoadedAt"/> 不是可观察属性，赋值本身不会触发界面刷新。
    /// </summary>
    protected virtual void OnLoaded()
    {
    }

    /// <summary>
    /// 子类实现具体的数据加载。异常直接抛出，由基类统一转成中文提示。
    /// </summary>
    /// <returns>加载任务。</returns>
    protected abstract Task LoadCoreAsync();

    /// <summary>
    /// 筛选条件变化后重新加载。加载中重复触发会被丢弃，避免两次请求互相覆盖结果。
    /// </summary>
    /// <returns>加载任务。</returns>
    protected Task ReloadAsync()
    {
        return LoadAsync(true);
    }

    /// <summary>
    /// 设置业务级别的提示（例如「尚未导入排班」这类非异常情况）。
    /// </summary>
    /// <param name="message">提示文本。</param>
    /// <param name="isError">是否为错误级别。</param>
    protected void SetMessage(string? message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }
}

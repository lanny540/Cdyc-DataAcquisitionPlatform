using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;
using DAP.Presentation.AvaloniaApp.ViewModels.Shell;

namespace DAP.Presentation.AvaloniaApp.ViewModels;

/// <summary>
/// 主窗口视图模型：顶栏模块切换、左侧抽屉分组导航、页面装载与账号区。
///
/// 导航目录只有一份（<see cref="DesktopNavigationCatalog"/>），
/// 顶栏与抽屉都从它取数，避免两处各写一份菜单导致层级漂移。
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly DesktopSession _session;
    private readonly PageFactory _pageFactory;
    private bool _wasAuthenticated;

    /// <summary>
    /// 初始化主窗口视图模型。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    /// <param name="pageFactory">页面工厂。</param>
    /// <param name="login">登录视图模型。</param>
    public MainWindowViewModel(DesktopSession session, PageFactory pageFactory, LoginViewModel login)
    {
        _session = session;
        _pageFactory = pageFactory;
        Login = login;

        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        Modules = new ObservableCollection<NavModuleViewModel>(
            DesktopNavigationCatalog.Modules.Select(module =>
                new NavModuleViewModel(module, SelectModuleAsync, NavigateAsync)));

        _session.PropertyChanged += OnSessionPropertyChanged;

        SelectModule(Modules[0]);

        // 启动时先尝试用已有 Cookie 恢复会话；失败就停在登录页。
        _ = TryRestoreSessionAsync();
    }

    /// <summary>
    /// 顶栏模块。
    /// </summary>
    public ObservableCollection<NavModuleViewModel> Modules { get; }

    /// <summary>
    /// 当前模块的分组。
    /// </summary>
    public ObservableCollection<NavGroupViewModel> CurrentGroups { get; } = [];

    /// <summary>
    /// 当前选中的模块。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrawerDescription))]
    private NavModuleViewModel? _selectedModule;

    /// <summary>
    /// 当前页面。
    /// </summary>
    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    /// <summary>
    /// 抽屉头部说明。
    /// </summary>
    public string DrawerDescription => SelectedModule?.Description ?? string.Empty;

    /// <summary>
    /// 登录视图模型。
    /// </summary>
    public LoginViewModel Login { get; }

    /// <summary>
    /// 桌面会话（视图直接绑定其登录态与账号信息）。
    /// </summary>
    public DesktopSession Session => _session;

    /// <summary>
    /// 服务端地址。
    /// </summary>
    public string ServerAddress => _session.ServerAddress;

    /// <summary>
    /// 客户端版本。
    /// </summary>
    public string VersionText
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;

            return version is null
                ? "v1.0.0"
                : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// 登出命令。
    /// </summary>
    public IAsyncRelayCommand LogoutCommand { get; }

    /// <summary>
    /// 切换模块：重建抽屉分组，并跳到该模块的落地页。
    /// </summary>
    /// <param name="module">目标模块。</param>
    private async Task SelectModuleAsync(NavModuleViewModel module)
    {
        if (SelectModule(module) && _session.IsAuthenticated)
        {
            await NavigateAsync(module.Items[0]);
        }
    }

    /// <summary>
    /// 切换模块的同步部分：只重建导航，不触发数据加载。
    /// </summary>
    /// <param name="module">目标模块。</param>
    /// <returns>模块是否发生变化。</returns>
    private bool SelectModule(NavModuleViewModel module)
    {
        var changed = !ReferenceEquals(SelectedModule, module);

        foreach (NavModuleViewModel item in Modules)
        {
            item.IsSelected = ReferenceEquals(item, module);
        }

        SelectedModule = module;

        CurrentGroups.Clear();

        foreach (DesktopNavGroup group in module.Source.Groups)
        {
            List<NavItemViewModel> items = module.Items
                .Where(item => group.Items.Any(source => source.PageKey == item.PageKey))
                .ToList();

            CurrentGroups.Add(new NavGroupViewModel(group.Title, items));
        }

        return changed;
    }

    /// <summary>
    /// 导航到指定页面。
    /// </summary>
    /// <param name="item">目标条目。</param>
    private async Task NavigateAsync(NavItemViewModel item)
    {
        DesktopNavModule module = DesktopNavigationCatalog.ResolveModule(item.PageKey);
        NavModuleViewModel? moduleViewModel = Modules.FirstOrDefault(candidate => candidate.Id == module.Id);

        if (moduleViewModel is not null && !ReferenceEquals(SelectedModule, moduleViewModel))
        {
            SelectModule(moduleViewModel);
        }

        foreach (NavItemViewModel candidate in Modules.SelectMany(entry => entry.Items))
        {
            candidate.IsSelected = string.Equals(candidate.PageKey, item.PageKey, StringComparison.Ordinal);
        }

        PageViewModelBase? previous = CurrentPage;
        PageViewModelBase next = _pageFactory.Resolve(item.Source);

        if (!ReferenceEquals(previous, next))
        {
            previous?.OnDeactivated();
        }

        CurrentPage = next;

        await next.OnActivatedAsync();
    }

    /// <summary>
    /// 登录态变化：登录后落到默认模块的落地页，登出后清空内容区。
    /// </summary>
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DesktopSession.IsAuthenticated))
        {
            return;
        }

        // NotifyPropertyChangedFor 只在用户对象变化时触发通知，改密刷新用户也会走到这里，
        // 但登录态没变，不能因此重新导航一次（会把用户正在看的页面拉回落地页）。
        var isAuthenticated = _session.IsAuthenticated;

        if (isAuthenticated == _wasAuthenticated)
        {
            return;
        }

        _wasAuthenticated = isAuthenticated;

        if (isAuthenticated)
        {
            _ = NavigateToLandingAsync();
            return;
        }

        CurrentPage?.OnDeactivated();
        CurrentPage = null;

        foreach (NavItemViewModel item in Modules.SelectMany(module => module.Items))
        {
            item.IsSelected = false;
        }
    }

    /// <summary>
    /// 跳到当前模块的落地页。
    /// </summary>
    private async Task NavigateToLandingAsync()
    {
        NavModuleViewModel module = SelectedModule ?? Modules[0];

        // 登录前只做了模块选择，条目尚未标记为选中，这里统一按落地页重走一次导航。
        await NavigateAsync(module.Items[0]);
    }

    /// <summary>
    /// 启动时的会话恢复。
    /// </summary>
    private async Task TryRestoreSessionAsync()
    {
        await _session.TryRestoreAsync();
    }

    /// <summary>
    /// 登出：清空 Cookie 与页面缓存状态。
    /// </summary>
    private async Task LogoutAsync()
    {
        await _session.SignOutAsync();

        // 登出时先停掉页面定时器，否则会话已失效还会继续按 30 秒轮询。
        CurrentPage?.OnDeactivated();
        CurrentPage = null;
        Login.NeedsPasswordChange = false;
        Login.Message = null;
    }
}

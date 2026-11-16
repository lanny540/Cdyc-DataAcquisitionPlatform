using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Shell;

/// <summary>
/// 左侧导航中的一个页面条目。
/// </summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly Func<NavItemViewModel, Task> _navigate;

    /// <summary>
    /// 初始化导航条目。
    /// </summary>
    /// <param name="source">目录条目。</param>
    /// <param name="navigate">点击后的导航动作。</param>
    public NavItemViewModel(DesktopNavItem source, Func<NavItemViewModel, Task> navigate)
    {
        Source = source;
        _navigate = navigate;
        SelectCommand = new AsyncRelayCommand(() => _navigate(this));
    }

    /// <summary>
    /// 目录条目。
    /// </summary>
    public DesktopNavItem Source { get; }

    /// <summary>
    /// 页面标识。
    /// </summary>
    public string PageKey => Source.PageKey;

    /// <summary>
    /// 菜单标题。
    /// </summary>
    public string Title => Source.Title;

    /// <summary>
    /// 图标路径数据。
    /// </summary>
    public string Icon => Source.Icon;

    /// <summary>
    /// 是否为当前页面。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 图标前景色：激活项用 primary 亮蓝（与 Web 端激活态一致），
    /// 非激活项用本页主色的 strong 亮色档——与 Web 端「前景一律 Strong」的分区着色对齐。
    /// </summary>
    public IBrush IconBrush => IsSelected
        ? Palette.Brush("DapPrimaryStrongBrush")
        : Palette.Brush("Dap" + Char.ToUpperInvariant(Source.Accent[0]) + Source.Accent.Substring(1) + "StrongBrush");

    /// <inheritdoc />
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(IconBrush));

    /// <summary>
    /// 选中命令。
    /// </summary>
    public IAsyncRelayCommand SelectCommand { get; }
}

/// <summary>
/// 左侧导航分组。
/// </summary>
public sealed class NavGroupViewModel
{
    /// <summary>
    /// 初始化分组。
    /// </summary>
    /// <param name="title">分组标题。</param>
    /// <param name="items">分组内条目。</param>
    public NavGroupViewModel(string title, IReadOnlyList<NavItemViewModel> items)
    {
        Title = title;
        Items = new ObservableCollection<NavItemViewModel>(items);
    }

    /// <summary>
    /// 分组标题。
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// 分组内条目。
    /// </summary>
    public ObservableCollection<NavItemViewModel> Items { get; }
}

/// <summary>
/// 顶栏模块。
/// </summary>
public sealed partial class NavModuleViewModel : ObservableObject
{
    private readonly Func<NavModuleViewModel, Task> _select;

    /// <summary>
    /// 初始化模块。
    /// </summary>
    /// <param name="source">目录模块。</param>
    /// <param name="select">点击模块后的动作。</param>
    /// <param name="navigate">点击模块内条目后的导航动作。</param>
    public NavModuleViewModel(
        DesktopNavModule source,
        Func<NavModuleViewModel, Task> select,
        Func<NavItemViewModel, Task> navigate)
    {
        Source = source;
        _select = select;

        Items = source
            .AllItems
            .Select(item => new NavItemViewModel(item, navigate))
            .ToList();

        SelectCommand = new AsyncRelayCommand(() => _select(this));
    }

    /// <summary>
    /// 目录模块。
    /// </summary>
    public DesktopNavModule Source { get; }

    /// <summary>
    /// 模块标识。
    /// </summary>
    public string Id => Source.Id;

    /// <summary>
    /// 顶栏显示名称。
    /// </summary>
    public string Title => Source.Title;

    /// <summary>
    /// 模块说明。
    /// </summary>
    public string Description => Source.Description;

    /// <summary>
    /// 模块下全部条目（不含命令，仅用于反查分组归属）。
    /// </summary>
    public IReadOnlyList<NavItemViewModel> Items { get; }

    /// <summary>
    /// 是否为当前模块。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 选中命令。
    /// </summary>
    public IAsyncRelayCommand SelectCommand { get; }
}

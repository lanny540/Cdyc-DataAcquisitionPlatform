using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Ursa.Controls;

namespace DAP.Presentation.AvaloniaApp.Views;

/// <summary>
/// 主窗口。
///
/// 不再固定 1920×1080：现场笔记本与外接屏分辨率差异很大，固定尺寸会让窗口超出屏幕。
/// 目标尺寸在屏幕工作区内自适应，最小尺寸低于常见笔记本分辨率，保证登录表单始终可见。
/// </summary>
public partial class MainWindow : UrsaWindow
{
    private const double PreferredWidth = 1360;
    private const double PreferredHeight = 860;
    private const double MinimumWidth = 1100;
    private const double MinimumHeight = 700;

    /// <summary>
    /// 初始化窗口。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;

        if (screen is null)
        {
            return;
        }

        PixelRect workingArea = screen.WorkingArea;

        var width = Math.Min(PreferredWidth, Math.Max(MinimumWidth, workingArea.Width - 80));
        var height = Math.Min(PreferredHeight, Math.Max(MinimumHeight, workingArea.Height - 80));

        Width = width;
        Height = height;
        MinWidth = Math.Min(MinimumWidth, workingArea.Width);
        MinHeight = Math.Min(MinimumHeight, workingArea.Height);

        Position = new PixelPoint(
            workingArea.X + Math.Max(0, (workingArea.Width - (int) width) / 2),
            workingArea.Y + Math.Max(0, (workingArea.Height - (int) height) / 2));
    }

}

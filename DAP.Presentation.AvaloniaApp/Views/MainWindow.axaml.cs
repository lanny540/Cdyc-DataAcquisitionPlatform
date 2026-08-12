using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Ursa.Controls;

namespace DAP.Presentation.AvaloniaApp.Views;

public partial class MainWindow : UrsaWindow
{
    private const double TargetWidth = 1920;
    private const double TargetHeight = 1080;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        PixelRect workingArea = screen?.WorkingArea ?? new PixelRect(0, 0, (int) TargetWidth, (int) TargetHeight);
        var shouldUseFullscreen = workingArea.Width < TargetWidth || workingArea.Height < TargetHeight;

        if (shouldUseFullscreen)
        {
            Width = workingArea.Width;
            Height = workingArea.Height;
            MinWidth = workingArea.Width;
            MaxWidth = workingArea.Width;
            MinHeight = workingArea.Height;
            MaxHeight = workingArea.Height;
            Position = new PixelPoint(workingArea.X, workingArea.Y);
            WindowState = WindowState.FullScreen;
            return;
        }

        Width = TargetWidth;
        Height = TargetHeight;
        MinWidth = TargetWidth;
        MaxWidth = TargetWidth;
        MinHeight = TargetHeight;
        MaxHeight = TargetHeight;
        WindowState = WindowState.Normal;
        Position = new PixelPoint(
            workingArea.X + Math.Max(0, (workingArea.Width - (int) TargetWidth) / 2),
            workingArea.Y + Math.Max(0, (workingArea.Height - (int) TargetHeight) / 2));
    }

    private void TopDragBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

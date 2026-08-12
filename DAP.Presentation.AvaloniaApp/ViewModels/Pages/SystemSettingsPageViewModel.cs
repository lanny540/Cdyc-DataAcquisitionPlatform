using System.Reflection;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

public partial class SystemSettingsPageViewModel : ViewModelBase
{
    [ObservableProperty] private string _pageTitle = "系统设置";

    [ObservableProperty] private string _pageDescription = "管理主题外观、版本信息与版权声明。";

    [ObservableProperty] private bool _isDarkTheme;

    [ObservableProperty] private string _themeModeText = "浅色";

    [ObservableProperty] private string _appName = "CDYC 数据采集平台客户端";

    [ObservableProperty] private string _appVersion = string.Empty;

    [ObservableProperty] private string _copyright =
        "Copyright © 2026 CDYC. All rights reserved.";

    [ObservableProperty] private string _buildDescription =
        ".NET 10 / Avalonia / Semi / Ursa";

    public SystemSettingsPageViewModel()
    {
        AppVersion = GetAppVersion();
        ApplyThemeState(Application.Current?.ActualThemeVariant ?? ThemeVariant.Light);
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = value
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }

        ThemeModeText = value ? "深色" : "浅色";
    }

    private void ApplyThemeState(ThemeVariant themeVariant)
    {
        IsDarkTheme = themeVariant == ThemeVariant.Dark;
        ThemeModeText = IsDarkTheme ? "深色" : "浅色";
    }

    private static string GetAppVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "v1.0.0"
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }
}

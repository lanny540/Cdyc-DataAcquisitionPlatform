using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;
using DAP.Presentation.AvaloniaApp.ViewModels;
using DAP.Presentation.AvaloniaApp.ViewModels.Pages;
using DAP.Presentation.AvaloniaApp.ViewModels.Shell;
using DAP.Presentation.AvaloniaApp.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.AvaloniaApp;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 注册桌面端服务。
    ///
    /// 会话是单例：它持有 Cookie 容器与两个客户端实例，一旦被重复创建，
    /// 登录得到的会话就只在其中一个实例上有效，表现为「时好时坏」的 401。
    /// 页面是瞬态：由 <see cref="PageFactory"/> 按需创建并缓存，切页不重复打接口。
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false, false)
            .Build();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));

        services.AddSingleton<DesktopSession>();
        services.AddSingleton<PageFactory>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        // 数据指标
        services.AddTransient<EnergyDashboardPageViewModel>();
        services.AddTransient<EnergyHistoryPageViewModel>();
        services.AddTransient<EnergyComparisonPageViewModel>();
        services.AddTransient<EnergyReportPageViewModel>();

        // 数据查看（只读）
        services.AddTransient<CollectionDataPageViewModel>();
        services.AddTransient<ShiftCalendarPageViewModel>();

        return services.BuildServiceProvider();
    }
}

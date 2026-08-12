using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DAP.Presentation.AvaloniaApp.Services;
using DAP.Presentation.AvaloniaApp.ViewModels;
using DAP.Presentation.AvaloniaApp.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    private static IServiceProvider ConfigureServices()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false, false)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));

        services.AddHttpClient<PlatformApiClient>((serviceProvider, client) =>
        {
            ApiOptions options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        services.AddSingleton<SqliteLocalCollectionPointStore>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ViewModels.Pages.HomePageViewModel>();
        services.AddTransient<ViewModels.Pages.SettingsPageViewModel>();
        services.AddTransient<ViewModels.Pages.SystemSettingsPageViewModel>();

        return services.BuildServiceProvider();
    }
}

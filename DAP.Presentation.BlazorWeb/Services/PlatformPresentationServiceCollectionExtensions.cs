using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using DAP.Infrastructure.DataAccess.Services;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Npgsql;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 提供 Blazor Web 宿主所需的服务注册扩展方法。
/// </summary>
public static class PlatformPresentationServiceCollectionExtensions
{
    /// <summary>
    /// 注册页面交互、MudBlazor、数据访问和页面预渲染所需的服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="environment">宿主环境。</param>
    /// <returns>当前服务集合。</returns>
    public static IServiceCollection AddPlatformPresentationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                options.DetailedErrors = environment.IsDevelopment();
            })
            .AddInteractiveWebAssemblyComponents();

        services.AddMudServices();
        services.AddMemoryCache();
        services.AddOpenApi();
        services.Configure<HistoryApiOptions>(configuration.GetSection(HistoryApiOptions.SectionName));
        services.Configure<ManagedDataSchedulerOptions>(
            configuration.GetSection(ManagedDataSchedulerOptions.SectionName));

        services.AddHttpClient("HistoryApi", (serviceProvider, client) =>
            {
                HistoryApiOptions historyOptions = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<HistoryApiOptions>>().Value;

                if (!string.IsNullOrWhiteSpace(historyOptions.BaseAddress))
                {
                    client.BaseAddress = new Uri(AppendTrailingSlash(historyOptions.BaseAddress));
                }

                client.Timeout = TimeSpan.FromSeconds(Math.Max(historyOptions.RequestTimeoutSeconds, 5));
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                HistoryApiOptions historyOptions = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<HistoryApiOptions>>().Value;

                return new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = historyOptions.IgnoreServerCertificateErrors
                        ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        : null
                };
            });

        services.AddSingleton(_ =>
        {
            return new PostgreSqlConnectionSettings(
                ConnectionStringResolver.GetPostgreSqlConnectionString(configuration));
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<PostgreSqlConnectionSettings>();
            return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
        });

        services.AddDbContext<DataAcquisitionPlatformDbContext>((serviceProvider, options) =>
        {
            var settings = serviceProvider.GetRequiredService<PostgreSqlConnectionSettings>();
            options.UseNpgsql(settings.ConnectionString);
        });

        services.AddScoped<ICollectionPointRepository, PostgreSqlCollectionPointRepository>();
        services.AddScoped<IManagedDataDefinitionRepository, PostgreSqlManagedDataDefinitionRepository>();
        services.AddScoped<ICollectionDataRecordRepository, PostgreSqlCollectionDataRecordRepository>();
        services.AddScoped<IPlatformReadRepository, PostgreSqlPlatformReadRepository>();
        services.AddScoped<IDataAcquisitionPlatformService, DataAcquisitionPlatformService>();
        services.AddScoped<IHistoryApiProxyService, HistoryApiProxyService>();
        services.AddScoped<IManagedDataExecutionService, ManagedDataExecutionService>();
        services.AddScoped<IConnectionDiagnosticsService, ConnectionDiagnosticsService>();
        services.AddScoped<IPlatformApiClient, ServerPlatformApiClient>();
        services.AddHostedService<ManagedDataCollectionScheduler>();

        return services;
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}

/// <summary>
/// 表示 PostgreSQL 连接配置。
/// </summary>
/// <param name="ConnectionString">数据库连接字符串。</param>
internal sealed record PostgreSqlConnectionSettings(string ConnectionString);

internal static class ConnectionStringResolver
{
    public static string GetPostgreSqlConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql");
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException("未配置 PostgreSQL 连接字符串 ConnectionStrings:PostgreSql。")
            : connectionString;
    }
}

/// <summary>
/// 为 Auto 模式的首屏预渲染提供服务端 API 适配。
/// </summary>
internal sealed class ServerPlatformApiClient(
    IDataAcquisitionPlatformService platformService,
    IHistoryApiProxyService historyApiProxyService,
    IManagedDataExecutionService managedDataExecutionService,
    IConnectionDiagnosticsService connectionDiagnosticsService) : IPlatformApiClient
{
    public Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        return platformService.GetDashboardOverviewAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        return (await platformService.GetCollectionPointsAsync(cancellationToken)).ToList();
    }

    public Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return platformService.UpsertCollectionPointAsync(request, cancellationToken);
    }

    public Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return platformService.DeleteCollectionPointAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return (await platformService.GetManagedDataDefinitionsAsync(cancellationToken)).ToList();
    }

    public async Task<ManagedDataDefinitionDetailsDto> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        ManagedDataDefinitionDetailsDto? details =
            await platformService.GetManagedDataDefinitionDetailsAsync(id, historyLimit, cancellationToken);
        return details ?? throw new InvalidOperationException("指定的后台数据节点不存在。");
    }

    public Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return platformService.UpsertManagedDataDefinitionAsync(request, cancellationToken);
    }

    public Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return platformService.DeleteManagedDataDefinitionAsync(id, cancellationToken);
    }

    public Task<ManagedDataExecutionResultDto> DebugReadManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return managedDataExecutionService.DebugReadAsync(id, cancellationToken);
    }

    public Task<ManagedDataExecutionResultDto> CollectManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return managedDataExecutionService.CollectAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ServerConnectionStatusDto>> GetServerConnectionStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return (await connectionDiagnosticsService.GetServerStatusesAsync(cancellationToken)).ToList();
    }

    public async Task<IReadOnlyList<ServerConnectionStatusDto>> GetServerConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        return (await connectionDiagnosticsService.GetServerConnectionsAsync(cancellationToken)).ToList();
    }

    public async Task<ServerConnectionStatusDto> CheckServerConnectionStatusAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ServerConnectionStatusDto? status =
            await connectionDiagnosticsService.CheckServerStatusAsync(key, cancellationToken);
        return status ?? throw new InvalidOperationException("指定的服务器连接不存在。");
    }

    public Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return connectionDiagnosticsService.TestManagedDataConnectionAsync(request, cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetHistoryCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return historyApiProxyService.GetCurrentValueAsync(request, cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetHistoryRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return historyApiProxyService.GetRawDataAsync(request, cancellationToken);
    }
}

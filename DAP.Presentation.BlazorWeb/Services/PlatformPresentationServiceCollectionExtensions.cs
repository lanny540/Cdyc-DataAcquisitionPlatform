using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Initialization;
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
        services.AddOpenApi();

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
        services.AddScoped<ICollectionDataRecordRepository, PostgreSqlCollectionDataRecordRepository>();
        services.AddScoped<IPlatformReadRepository, PostgreSqlPlatformReadRepository>();
        services.AddScoped<IDataAcquisitionPlatformService, DataAcquisitionPlatformService>();
        services.AddScoped<IPlatformApiClient, ServerPlatformApiClient>();
        services.AddScoped(sp =>
        {
            var settings = sp.GetRequiredService<PostgreSqlConnectionSettings>();
            var dbContext = sp.GetRequiredService<DataAcquisitionPlatformDbContext>();
            return new PostgreSqlDatabaseInitializer(settings.ConnectionString, dbContext);
        });

        return services;
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
internal sealed class ServerPlatformApiClient(IDataAcquisitionPlatformService platformService) : IPlatformApiClient
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
}

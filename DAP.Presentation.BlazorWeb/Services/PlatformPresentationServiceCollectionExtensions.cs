using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
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

        // 采集服务器的地址、凭据与接口路径保存在数据库中，这里只绑定进程级运行时开关。
        services.Configure<AcquisitionServerRuntimeOptions>(
            configuration.GetSection(AcquisitionServerRuntimeOptions.SectionName));
        services.Configure<ManagedDataSchedulerOptions>(
            configuration.GetSection(ManagedDataSchedulerOptions.SectionName));

        services.AddHttpClient("HistoryApi", (serviceProvider, client) =>
        {
            AcquisitionServerRuntimeOptions runtimeOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<AcquisitionServerRuntimeOptions>>().Value;

            // 基地址与证书策略由每台采集服务器的配置决定，这里只设置兜底超时。
            client.Timeout = TimeSpan.FromSeconds(Math.Max(runtimeOptions.DefaultRequestTimeoutSeconds, 5));
        });

        services.AddHttpClient("AcquisitionServerProbe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
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
        services.AddScoped<IAcquisitionServerConnectionResolver, AcquisitionServerConnectionResolver>();
        services.AddScoped<IHistoryApiProxyService, HistoryApiProxyService>();
        services.AddScoped<IAcquisitionServerProbeService, AcquisitionServerProbeService>();
        services.AddScoped<IAcquisitionServerService, AcquisitionServerService>();
        services.AddScoped<IManagedDataExecutionService, ManagedDataExecutionService>();
        services.AddScoped<IConnectionDiagnosticsService, ConnectionDiagnosticsService>();

        // 排班表是全站共享的配置数据，必须单例：Scoped 会让导入结果随请求结束而丢失。
        services.AddSingleton<IShiftSchedulingService, InMemoryShiftSchedulingService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IEnergyMetricService, DeterministicEnergyMetricService>();
        services.AddScoped<IPlatformApiClient, ServerPlatformApiClient>();
        services.AddHostedService<ManagedDataCollectionScheduler>();

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
internal sealed class ServerPlatformApiClient(
    IDataAcquisitionPlatformService platformService,
    IHistoryApiProxyService historyApiProxyService,
    IAcquisitionServerConnectionResolver serverConnectionResolver,
    IAcquisitionServerService acquisitionServerService,
    IManagedDataExecutionService managedDataExecutionService,
    IConnectionDiagnosticsService connectionDiagnosticsService,
    IShiftSchedulingService shiftSchedulingService,
    IEnergyMetricService energyMetricService) : IPlatformApiClient
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

    public async Task<HistoryApiQueryResponse> GetHistoryCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ServerConnectionSettings settings = await ResolveHistorianServerAsync(
            request.ServerConnectionId, cancellationToken);

        return await historyApiProxyService.GetCurrentValueAsync(
            request, settings.ToHistorianOptions(), cancellationToken);
    }

    public async Task<HistoryApiQueryResponse> GetHistoryRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ServerConnectionSettings settings = await ResolveHistorianServerAsync(
            request.ServerConnectionId, cancellationToken);

        return await historyApiProxyService.GetRawDataAsync(
            request, settings.ToHistorianOptions(), cancellationToken);
    }

    private async Task<ServerConnectionSettings> ResolveHistorianServerAsync(
        Guid? serverConnectionId,
        CancellationToken cancellationToken)
    {
        return await serverConnectionResolver.ResolveAsync(
                   serverConnectionId, AcquisitionServerCatalog.Historian, cancellationToken)
               ?? throw new InvalidOperationException(
                   "尚未配置可用的 GE Historian 采集服务器，请先在采集服务器页面新增并启用。");
    }

    public async Task<IReadOnlyList<AcquisitionServerTypeDescriptor>> GetAcquisitionServerTypesAsync(
        CancellationToken cancellationToken = default)
    {
        return AcquisitionServerCatalog.Descriptors;
    }

    public async Task<IReadOnlyList<AcquisitionServerDto>> GetAcquisitionServersAsync(
        CancellationToken cancellationToken = default)
    {
        return (await acquisitionServerService.GetServersAsync(cancellationToken)).ToList();
    }

    public Task<AcquisitionServerDto> UpsertAcquisitionServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return acquisitionServerService.UpsertServerAsync(request, cancellationToken);
    }

    public async Task<bool> DeleteAcquisitionServerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (succeeded, errorMessage) =
            await acquisitionServerService.DeleteServerAsync(id, cancellationToken);

        if (!succeeded)
        {
            throw new InvalidOperationException(errorMessage ?? "删除采集服务器失败。");
        }

        return true;
    }

    public Task<AcquisitionServerProbeResultDto> TestAcquisitionServerAsync(
        Guid? id,
        AcquisitionServerProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        return acquisitionServerService.TestServerAsync(
            request.ToUpsertRequest(id), request.Identifier, request.RequestPath, request.NodeConfiguration,
            cancellationToken);
    }

    public Task<ShiftScheduleStatusDto> GetShiftScheduleStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return shiftSchedulingService.GetStatusAsync(cancellationToken);
    }

    public async Task<ShiftScheduleValidationDto> GetShiftScheduleValidationAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ShiftScheduleRowDto> rows =
            await shiftSchedulingService.GetAllRowsAsync(cancellationToken);

        return ShiftRotationValidator.Validate(rows);
    }

    public async Task<ShiftScheduleImportResultDto> ImportShiftScheduleAsync(
        Stream excelStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // 预渲染阶段直接复用服务端服务，不经过 HTTP 自调用。
        return await shiftSchedulingService.ImportAsync(fileName, excelStream, cancellationToken);
    }

    public async Task<bool> ClearShiftSchedulesAsync(CancellationToken cancellationToken = default)
    {
        return await shiftSchedulingService.ClearAsync();
    }

    public async Task<IReadOnlyList<DailyShiftScheduleDto>> GetShiftScheduleCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await shiftSchedulingService.GetCalendarAsync(from, to, teamCode, cancellationToken);

        return calendar.ToList();
    }

    public async Task<TeamShiftScheduleDto?> GetTeamShiftScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await shiftSchedulingService.GetCalendarAsync(from, to, teamCode, cancellationToken);

        ShiftScheduleRowDto[] rows = calendar.SelectMany(item => item.Rows).ToArray();

        if (rows.Length == 0)
        {
            return null;
        }

        return new TeamShiftScheduleDto(
            rows[0].TeamCode,
            rows[0].TeamName,
            rows.OrderBy(item => item.WorkDate).ThenBy(item => item.StartTime).ToArray(),
            rows.Count(item => !item.IsRestDay),
            Math.Round(rows.Sum(item => item.DurationHours), 2),
            rows.Count(item => item.IsRestDay));
    }

    public Task<EnergyDashboardDto> GetEnergyDashboardAsync(
        int trendDays = 7,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        return energyMetricService.GetDashboardAsync(trendDays, teamCode, cancellationToken);
    }

    public Task<EnergyHistoryDto> GetEnergyHistoryAsync(
        string teamCode,
        string granularity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        return energyMetricService.GetHistoryAsync(teamCode, granularity, from, to, cancellationToken);
    }

    public Task<EnergyComparisonDto> GetEnergyComparisonAsync(
        string granularity,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return energyMetricService.GetComparisonAsync(granularity, date, cancellationToken);
    }
}

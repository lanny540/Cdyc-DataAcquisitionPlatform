using System.Security.Claims;
using DAP.Clients;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using DAP.Infrastructure.DataAccess.Services;
using DAP.Presentation.BlazorWeb.Client.Services;
using DAP.Presentation.BlazorWeb.Security;
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
            .AddInteractiveServerComponents(options => { options.DetailedErrors = environment.IsDevelopment(); })
            .AddInteractiveWebAssemblyComponents();

        services.AddMudServices();
        services.AddMemoryCache();

        // Cookie 与应用内数据保护用的密钥环必须落盘并固定应用名：
        // 放进默认位置时，重新部署/换容器即丢密钥，所有会话立刻失效（用户表现为「突然全部掉线」）；
        // 多实例部署下每个实例各有一套密钥，用户会在实例之间反复被踢。
        services.AddPlatformDataProtection(configuration, environment);

        // 导航树：服务端按级联认证状态直调构建服务（SSR 与 Server 电路共用），
        // WASM 端在自身 Program.cs 注册 HTTP 实现，两端接口一致。
        services.AddHttpContextAccessor();
        services.AddScoped<NavigationViewService>();
        services.AddScoped<INavigationViewClient, ServerNavigationViewClient>();
        services.AddOpenApi();

        // 认证端点的限流：挡密码喷洒与 PBKDF2 计算型 DoS（见 PlatformRateLimiting）。
        services.AddPlatformRateLimiting();

        // 认证与授权：Cookie 服务端会话 + 三态授权数据。初始化器必须先跑完，
        // 否则启用登录后角色或超管缺失会让所有人进不去。
        services.AddPlatformAuthentication(configuration, environment);
        services.AddHostedService<AuthDataInitializer>();

        // 能源字典（介质 / 用途 / 设备系统）只补不覆盖；失败不阻断启动。
        services.AddHostedService<EnergyDictionaryInitializer>();

        // 开发库的能源种子数据：空库时灌一次，之后不再介入。
        // 必须排在字典初始化器之后——它依赖介质、用途、系统与园区根节点已经就绪。
        services.AddHostedService<ParkEnergyDevDataInitializer>();

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

        services.AddHttpClient("AcquisitionServerProbe", client => { client.Timeout = TimeSpan.FromSeconds(30); });

        services.AddSingleton(_ =>
        {
            return new PostgreSqlConnectionSettings(
                ConnectionStringResolver.GetPostgreSqlConnectionString(configuration));
        });

        // 跨实例互斥：后台任务与手工触发共用，避免多实例重复执行。
        services.AddSingleton<PlatformAdvisoryLock>();

        services.AddSingleton(sp =>
        {
            PostgreSqlConnectionSettings settings = sp.GetRequiredService<PostgreSqlConnectionSettings>();
            return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
        });

        services.AddDbContext<DataAcquisitionPlatformDbContext>((serviceProvider, options) =>
        {
            PostgreSqlConnectionSettings settings = serviceProvider.GetRequiredService<PostgreSqlConnectionSettings>();
            options.UseNpgsql(settings.ConnectionString);
        });

        services.AddScoped<ICollectionPointRepository, PostgreSqlCollectionPointRepository>();
        services.AddScoped<IManagedDataDefinitionRepository, PostgreSqlManagedDataDefinitionRepository>();
        services.AddScoped<ICollectionDataRecordRepository, PostgreSqlCollectionDataRecordRepository>();
        services.AddScoped<IPlatformReadRepository, PostgreSqlPlatformReadRepository>();
        // 具体类必须单独注册：下方授权装饰器按接口重新映射时会解析具体类型，
        // 只注册「接口 → 具体」映射的话 GetRequiredService<具体类>() 会抛「未注册」。
        services.AddScoped<DataAcquisitionPlatformService>();
        services.AddScoped<AcquisitionServerService>();
        services.AddScoped<IAcquisitionServerConnectionResolver, AcquisitionServerConnectionResolver>();
        services.AddScoped<IHistoryApiProxyService, HistoryApiProxyService>();
        services.AddScoped<IAcquisitionServerProbeService, AcquisitionServerProbeService>();
        // 采集点的运行状态写入（数据面，不做权限判定）：调度器与手动采集共用。
        services.AddScoped<CollectionPointStateSynchronizer>();
        services.AddScoped<IManagedDataExecutionService, ManagedDataExecutionService>();
        services.AddScoped<IConnectionDiagnosticsService, ConnectionDiagnosticsService>();

        // 领域服务装饰器（docs/AUTH_DESIGN.md 7.2）：SSR 直调与 HTTP 端点两条路径
        // 共用同一个校验点——写操作要求权限点，拒绝与执行结果一律写审计。
        // 装饰器内部按请求开 scope 取操作者与审计管道，自身生命周期与被包装服务一致。
        services.AddScoped<Security.Auditing.ICurrentUserContext, Security.Auditing.PlatformCurrentUserContext>();
        services.AddScoped<Security.Auditing.AuditLogWriter>();
        services.AddScoped<AdminDomainService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<AdminAuditService>();
        services.AddHostedService<AuditRetentionService>();
        services.AddScoped<IDataAcquisitionPlatformService>(sp => new Security.Auditing.AuditedDataAcquisitionService(
            sp.GetRequiredService<DataAcquisitionPlatformService>(),
            sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddScoped<IAcquisitionServerService>(sp => new Security.Auditing.AuditedAcquisitionServerService(
            sp.GetRequiredService<AcquisitionServerService>(),
            sp.GetRequiredService<IServiceScopeFactory>()));

        // 排班快照保存在 PostgreSQL，所有实例共享；装饰器从当前电路获取操作者。
        services.AddScoped<PostgreSqlShiftSchedulingService>();
        services.AddScoped<IShiftSchedulingService>(sp => new Security.Auditing.AuditedShiftSchedulingService(
            sp.GetRequiredService<PostgreSqlShiftSchedulingService>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<Security.Auditing.ICurrentUserContext>()));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IEnergyMetricService, DeterministicEnergyMetricService>();
        // 园区能源：读表聚合与外部读数接入。口径集中在 ParkEnergyAggregator，
        // 页面、接口、报表三条路径共用同一份实现。
        services.AddScoped<ParkEnergyQueryService>();
        services.AddScoped<IParkEnergyQueryService, Security.Auditing.AuditedParkEnergyQueryService>();
        services.AddScoped<IPlatformApiClient, ServerPlatformApiClient>();
        services.AddHostedService<ManagedDataCollectionScheduler>();

        return services;
    }
}

/// <summary>
/// 服务端导航加载器：优先用组件传入的级联认证状态（SSR 预渲染与 Server 电路均可用），
/// 组件未提供时回退到 IHttpContextAccessor（仅静态 SSR 阶段有值）。
/// </summary>
internal sealed class ServerNavigationViewClient(
    NavigationViewService navigationViewService,
    IHttpContextAccessor httpContextAccessor) : INavigationViewClient
{
    /// <inheritdoc />
    public async Task<NavigationDto?> LoadAsync(
        System.Security.Claims.ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        ClaimsPrincipal? user = principal ?? httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true) return null;

        return await navigationViewService.BuildAsync(user, cancellationToken);
    }
}

/// <summary>
/// 表示 PostgreSQL 连接配置。
/// </summary>
/// <param name="ConnectionString">数据库连接字符串。</param>
public sealed record PostgreSqlConnectionSettings(string ConnectionString);

internal static class ConnectionStringResolver
{
    public static string GetPostgreSqlConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("PostgreSql");
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
    IEnergyMetricService energyMetricService,
    IParkEnergyQueryService parkEnergyQueryService,
    AdminDomainService adminDomainService,
    AdminUserService adminUserService,
    AdminAuditService adminAuditService,
    Security.Directory.DirectorySyncService directorySyncService,
    Microsoft.Extensions.Options.IOptions<PlatformAuthOptions> authOptions) : IPlatformApiClient
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

    public async Task<IReadOnlyList<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CollectionDataRecordDto> records =
            await platformService.GetCollectionDataAsync(Math.Clamp(limit, 1, 500), cancellationToken);
        return records.ToList();
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
        (bool succeeded, string? errorMessage) =
            await acquisitionServerService.DeleteServerAsync(id, cancellationToken);

        if (!succeeded) throw new InvalidOperationException(errorMessage ?? "删除采集服务器失败。");

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

    public async Task<IReadOnlyList<ShiftTeamDto>> GetEnergyTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTeamsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftTeamDto>> GetShiftScheduleTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTeamsAsync(cancellationToken);
    }

    private async Task<ShiftTeamDto[]> GetTeamsAsync(CancellationToken cancellationToken)
    {
        ShiftScheduleStatusDto status = await shiftSchedulingService.GetStatusAsync(cancellationToken);
        return status.Teams.ToArray();
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

        if (rows.Length == 0) return null;

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

    public Task<ParkEnergyOverviewDto> GetParkEnergyOverviewAsync(
        string period,
        bool demo = false,
        CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.GetOverviewAsync(period, demo, cancellationToken);
    }

    public Task<EnergyDictionaryDto> GetEnergyDictionaryAsync(CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.GetDictionaryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EnergyMeterDto>> GetEnergyMetersAsync(
        CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.GetMetersAsync(cancellationToken);
    }

    public Task<EnergyMeterDto> UpsertEnergyMeterAsync(
        EnergyMeterUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.UpsertMeterAsync(request, cancellationToken);
    }

    public Task<bool> DeleteEnergyMeterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.DeleteMeterAsync(id, cancellationToken);
    }

    public Task<EnergyOrgUnitDto> UpsertEnergyOrgUnitAsync(
        EnergyOrgUnitUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.UpsertOrgUnitAsync(request, cancellationToken);
    }

    public Task<EnergyMediumDto> UpdateEnergyMediumFactorAsync(
        Guid mediumId,
        decimal? coalFactor,
        CancellationToken cancellationToken = default)
    {
        return parkEnergyQueryService.UpdateMediumFactorAsync(mediumId, coalFactor, cancellationToken);
    }

    public Task<IReadOnlyList<AdminDomainDto>> GetAdminDomainsAsync(CancellationToken cancellationToken = default)
    {
        return adminDomainService.GetDomainsAsync(cancellationToken);
    }

    public Task<bool> CreateAdminDomainAsync(
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.CreateDomainAsync(request, cancellationToken));
    }

    public Task<bool> UpdateAdminDomainAsync(
        Guid domainId,
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.UpdateDomainAsync(domainId, request, cancellationToken));
    }

    public Task<bool> DeleteAdminDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.DeleteDomainAsync(domainId, cancellationToken));
    }

    public Task<bool> DeleteAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.DeleteDomainPageAsync(pageId, cancellationToken));
    }

    public Task<bool> UpdateAdminDomainPageAsync(
        Guid pageId,
        AdminDomainPageUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.UpdateDomainPageAsync(pageId, request, cancellationToken));
    }

    public Task<bool> ResetAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.ResetDomainPageAsync(pageId, cancellationToken));
    }

    public Task<IReadOnlyList<AdminDepartmentDto>> GetAdminDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        return adminDomainService.GetDepartmentsAsync(cancellationToken);
    }

    public Task<bool> CreateAdminDepartmentAsync(
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.CreateDepartmentAsync(request, cancellationToken));
    }

    public Task<bool> UpdateAdminDepartmentAsync(
        Guid departmentId,
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.UpdateDepartmentAsync(departmentId, request, cancellationToken));
    }

    public Task<bool> UpdateAdminDepartmentGrantsAsync(
        Guid departmentId,
        AdminDepartmentGrantsRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminDomainService.UpdateDepartmentGrantsAsync(departmentId, request, cancellationToken));
    }

    public Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        return adminUserService.GetUsersAsync(keyword, cancellationToken);
    }

    public Task<IReadOnlyList<AdminRoleDto>> GetAdminRolesAsync(CancellationToken cancellationToken = default)
    {
        return adminUserService.GetAssignableRolesAsync(cancellationToken);
    }

    public Task<bool> CreateAdminUserAsync(
        AdminUserCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.CreateUserAsync(request, cancellationToken));
    }

    public Task<bool> UpdateAdminUserAsync(
        Guid userId,
        AdminUserUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.UpdateUserAsync(userId, request, cancellationToken));
    }

    public Task<bool> UpdateAdminUserDepartmentAsync(
        Guid userId,
        AdminUserDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.UpdateUserDepartmentAsync(userId, request, cancellationToken));
    }

    public Task<bool> UpdateAdminUserRolesAsync(
        Guid userId,
        AdminUserRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.UpdateUserRolesAsync(userId, request, cancellationToken));
    }

    public Task<bool> ResetAdminUserPasswordAsync(
        Guid userId,
        AdminUserResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.ResetPasswordAsync(userId, request, cancellationToken));
    }

    public Task<bool> SetAdminUsersEnabledAsync(
        AdminUserBatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.SetUsersEnabledAsync(request, cancellationToken));
    }

    public Task<bool> HandoverSuperAdminAsync(
        SuperAdminHandoverRequest request,
        CancellationToken cancellationToken = default)
    {
        return SucceedAsync(adminUserService.HandoverSuperAdminAsync(request, cancellationToken));
    }

    public Task<AuditLogPageDto> GetAuditLogsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return adminAuditService.QueryAsync(
            new AuditLogFilter(from, to, userName, targetType, result, page, pageSize), cancellationToken);
    }

    public string BuildAuditExportUrl(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result)
    {
        // 首屏预渲染阶段只用于渲染链接，实际下载始终由浏览器直接请求端点。
        List<string> query = [];

        if (from is { } fromValue)
            query.Add(
                $"from={Uri.EscapeDataString(fromValue.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");

        if (to is { } toValue)
            query.Add(
                $"to={Uri.EscapeDataString(toValue.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");

        if (!string.IsNullOrWhiteSpace(userName)) query.Add($"userName={Uri.EscapeDataString(userName.Trim())}");

        if (!string.IsNullOrWhiteSpace(targetType)) query.Add($"targetType={Uri.EscapeDataString(targetType.Trim())}");

        if (!string.IsNullOrWhiteSpace(result)) query.Add($"result={Uri.EscapeDataString(result.Trim())}");

        return query.Count == 0
            ? "/api/audit-logs/export"
            : $"/api/audit-logs/export?{string.Join('&', query)}";
    }

    public async Task<DirectorySyncStatusDto> GetDirectorySyncStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return await directorySyncService.GetStatusAsync(cancellationToken);
    }

    public Task<DirectorySyncRunDto> RunDirectorySyncAsync(CancellationToken cancellationToken = default)
    {
        return directorySyncService.RunAsync(null, "manual", cancellationToken);
    }

    public async Task<bool> ResolveDirectorySyncIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        // 预渲染阶段拿不到 HttpContext，操作者按「系统触发」记录；
        // 浏览器端调用走 HTTP 端点，那里能取到真实操作者。
        AuthOperationResultDto result =
            await directorySyncService.ResolveIssueAsync(issueId, null, cancellationToken);

        if (!result.Succeeded) throw new InvalidOperationException(result.ErrorMessage ?? "操作失败。");

        return true;
    }

    public Task<IReadOnlyList<OidcProviderDto>> GetOidcProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OidcProviderDto> providers = authOptions.Value.Oidc.EnabledProviders()
            .Select(provider => new OidcProviderDto(
                provider.Code,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Code : provider.DisplayName,
                $"/api/auth/oidc/{provider.Code}/challenge"))
            .ToList();

        return Task.FromResult(providers);
    }

    /// <summary>
    /// 把管理服务的操作结果转换为布尔结果，业务失败时抛出带中文原因的异常供页面提示。
    /// </summary>
    private static async Task<bool> SucceedAsync(Task<AuthOperationResultDto> operation)
    {
        AuthOperationResultDto result = await operation;

        if (!result.Succeeded) throw new InvalidOperationException(result.ErrorMessage ?? "操作失败。");

        return true;
    }
}
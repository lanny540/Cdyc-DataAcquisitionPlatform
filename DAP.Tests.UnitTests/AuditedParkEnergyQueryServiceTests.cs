using System.Security.Claims;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 能源配置写操作的审计装饰器测试。
///
/// 内层是真实的 <see cref="ParkEnergyQueryService"/>（SQLite 内存库），
/// 审计写入走真实的 <see cref="AuditLogWriter"/>（固定时钟），
/// 当前会话由 <see cref="ICurrentUserContext"/> 替身提供，
/// 授权链（安全戳比对、会话时效、域与权限判定）按生产逻辑真实执行。
///
/// 钉住的行为：读操作放行且不留审计；未认证的写操作拒绝并记 failed 审计
/// （越权尝试的痕迹不能消失）；已授权的写操作记 success 审计；
/// 业务校验失败记 failed 审计并原样重抛。
/// </summary>
public sealed class AuditedParkEnergyQueryServiceTests
{
    /// <summary>审计时钟固定值：断言 OccurredAt 的确定性依据。</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static async Task SeedCatalogAsync(EnergySqliteTestDatabase database)
    {
        await new EnergyDictionaryInitializer(
                new EnergyScopeFactory(database),
                NullLogger<EnergyDictionaryInitializer>.Instance)
            .StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// 播种一个启用中的超级管理员（含 super-admin 角色）与 energy 业务域，
    /// 并返回（用户标识，安全戳）。授权链会真实查询这份数据。
    /// </summary>
    private static async Task<(Guid UserId, Guid Stamp)> SeedSuperAdminAsync(EnergySqliteTestDatabase database)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid userId = Guid.NewGuid();
        Guid stamp = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(new AppUser
        {
            Id = userId,
            UserName = "admin",
            DisplayName = "管理员",
            IsEnabled = true,
            MustChangePassword = false,
            SecurityStamp = stamp,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Roles.Add(new Role
        {
            Id = roleId,
            Code = AuthorizationCatalog.SuperAdminRole,
            Name = "超级管理员",
            PageLevel = AuthorizationCatalog.SuperAdminPageLevel,
            IsBuiltin = true,
            IsEnabled = true,
            SortOrder = 10,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.BusinessDomains.Add(new BusinessDomain
        {
            Id = Guid.NewGuid(),
            Code = AuthorizationCatalog.EnergyDomain,
            Name = "能源",
            SortOrder = 30,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        return (userId, stamp);
    }

    /// <summary>构造超级管理员的会话主体（claims 与生产 Cookie 的签发口径一致）。</summary>
    private static CurrentUser SuperAdmin(Guid userId, Guid stamp, DateTimeOffset issuedAt)
    {
        AuthUserDto dto = new(
            userId,
            "admin",
            "管理员",
            "动力部",
            [AuthorizationCatalog.SuperAdminRole],
            [AuthorizationCatalog.EnergyDomain],
            AuthorizationCatalog.SuperAdminPageLevel,
            [],
            false,
            true);

        ClaimsPrincipal principal = PlatformClaimsFactory.CreatePrincipal(dto, issuedAt, stamp.ToString());
        return new CurrentUser(userId, "admin", "管理员", "动力部", "192.168.1.10", "UnitTest/1.0", true, principal);
    }

    private static AuditedParkEnergyQueryService CreateService(
        EnergySqliteTestDatabase database,
        CurrentUser? user,
        ParkEnergyQueryService? inner = null)
    {
        return new AuditedParkEnergyQueryService(
            inner ?? new ParkEnergyQueryService(
                database.CreateContext(),
                HostingFakes.CreateConfiguration(),
                HostingFakes.CreateDevelopmentEnvironment(),
                NullLogger<ParkEnergyQueryService>.Instance),
            new StubUserContext(user),
            new AuditScopeFactory(database, new FixedTimeProvider(FixedNow)));
    }

    /// <summary>构造合法的计量点新建请求（四维标识取自已播种的字典）。</summary>
    private static async Task<EnergyMeterUpsertRequest> ValidMeterRequestAsync(EnergySqliteTestDatabase database,
        string code)
    {
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        Guid mediumId = (await db.EnergyMedia.SingleAsync(item => item.Code == "ELEC")).Id;
        Guid orgId = (await db.EnergyOrgUnits.SingleAsync(item => item.Level == EnergyOrgUnit.ParkLevel)).Id;
        Guid usageId = (await db.EnergyUsageTypes.SingleAsync(item => item.Code == "PROD")).Id;
        Guid systemId = (await db.EnergySystems.SingleAsync(item => item.Code == "PROCESS")).Id;

        return new EnergyMeterUpsertRequest(
            null, code, $"{code} 名称", mediumId, orgId, usageId, systemId,
            null, true, 1m, true, null);
    }

    private static async Task<List<AuditLog>> LoadAuditsAsync(EnergySqliteTestDatabase database)
    {
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        return await db.AuditLogs.OrderBy(log => log.OccurredAt).ToListAsync();
    }

    [Fact]
    public async Task ReadOperations_WithoutSession_Should_Pass_Through_Without_Audit()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        AuditedParkEnergyQueryService service = CreateService(database, null);

        // 读操作不做权限判定也不留审计：查询不留痕是审计边界的基本约定。
        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");
        Assert.Equal(ParkEnergyDataStatus.Empty, overview.DataStatus);

        EnergyDictionaryDto dictionary = await service.GetDictionaryAsync();
        Assert.Equal(5, dictionary.Media.Count);

        Assert.Empty(await service.GetMetersAsync());

        Assert.Empty(await LoadAuditsAsync(database));
    }

    [Fact]
    public async Task Write_WithoutSession_Should_Be_Denied_And_Audit_Every_Attempt()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        AuditedParkEnergyQueryService service = CreateService(database, null);

        EnergyMeterUpsertRequest request = await ValidMeterRequestAsync(database, "EM-AUDIT-001");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpsertMeterAsync(request));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpdateMediumFactorAsync(Guid.NewGuid(), 1m));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.IngestReadingsAsync(new IngestEnergyReadingsRequest([])));

        List<AuditLog> audits = await LoadAuditsAsync(database);

        // 连续尝试越权的痕迹必须每条都在：拒绝后吞掉记录等于帮人抹痕迹。
        Assert.Equal(3, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.Equal("failed", audit.Result);
            Assert.Null(audit.UserId);
            Assert.NotNull(audit.Detail);

            // detail 以 JSON 存储（非 ASCII 会被转义），按结构断言而不是找子串。
            using JsonDocument detail = JsonDocument.Parse(audit.Detail!);
            Assert.Equal("会话失效或缺少能源配置权限", detail.RootElement.GetProperty("reason").GetString());
        });

        // 三条记录的时间戳相同（固定时钟），顺序不保证，按集合比较。
        Assert.Equal(
            new[] { "energy.meter.upsert", "energy.factor.update", "energy.readings.ingest" }
                .OrderBy(code => code, StringComparer.Ordinal),
            audits.Select(audit => audit.ActionCode).OrderBy(code => code, StringComparer.Ordinal));

        // 被拒的写操作没有落到业务表。
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(0, await db.EnergyMeters.CountAsync());
    }

    [Fact]
    public async Task SuperAdmin_UpsertMeter_Should_Persist_And_Write_Success_Audit()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);
        AuditedParkEnergyQueryService service =
            CreateService(database, SuperAdmin(userId, stamp, DateTimeOffset.UtcNow.AddMinutes(-5)));

        EnergyMeterUpsertRequest request = await ValidMeterRequestAsync(database, "EM-AUDIT-OK");
        EnergyMeterDto dto = await service.UpsertMeterAsync(request);

        Assert.Equal("EM-AUDIT-OK", dto.Code);

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("energy.meter.upsert", audit.ActionCode);
        Assert.Equal("energy-meter", audit.TargetType);
        Assert.Equal("EM-AUDIT-OK", audit.TargetName);
        Assert.Equal("success", audit.Result);
        Assert.Null(audit.Detail);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("admin", audit.UserName);
        Assert.Equal("动力部", audit.DepartmentName);
        Assert.Equal("192.168.1.10", audit.Ip);
        Assert.Equal("UnitTest/1.0", audit.UserAgent);
        // OccurredAt 来自固定时钟，不随系统时间漂移。
        Assert.Equal(FixedNow, audit.OccurredAt);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.True(await db.EnergyMeters.AnyAsync(meter => meter.Code == "EM-AUDIT-OK"));
    }

    [Fact]
    public async Task DeleteMissingMeter_Should_Return_False_And_Audit_Failed()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);
        AuditedParkEnergyQueryService service =
            CreateService(database, SuperAdmin(userId, stamp, DateTimeOffset.UtcNow));

        // 业务上「删除不存在的计量点」返回 false：审计必须记 failed 而不是 success。
        Assert.False(await service.DeleteMeterAsync(Guid.NewGuid()));

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("energy.meter.delete", audit.ActionCode);
        Assert.Equal("failed", audit.Result);
        Assert.Null(audit.Detail);
    }

    [Fact]
    public async Task Business_Validation_Error_Should_Audit_Failed_And_Rethrow()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);
        AuditedParkEnergyQueryService service =
            CreateService(database, SuperAdmin(userId, stamp, DateTimeOffset.UtcNow));

        EnergyMeterUpsertRequest request = await ValidMeterRequestAsync(database, "EM-AUDIT-DUP");
        await service.UpsertMeterAsync(request);

        // 校验失败（编码重复）原样抛给页面，同时落一条 failed 审计。
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertMeterAsync(request));

        List<AuditLog> audits = await LoadAuditsAsync(database);
        Assert.Equal(2, audits.Count);

        // 两条审计的时间戳相同（固定时钟），用结果而非下标区分先后。
        AuditLog success = Assert.Single(audits, audit => audit.Result == "success");
        Assert.Equal("energy.meter.upsert", success.ActionCode);
        Assert.Equal("EM-AUDIT-DUP", success.TargetName);

        AuditLog failure = Assert.Single(audits, audit => audit.Result == "failed");
        Assert.Contains("ArgumentException", failure.Detail);
        Assert.Equal("EM-AUDIT-DUP", failure.TargetName);
    }

    [Fact]
    public async Task SuperAdmin_IngestEmptyBatch_Should_Succeed_With_SystemPermission()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);
        AuditedParkEnergyQueryService service =
            CreateService(database, SuperAdmin(userId, stamp, DateTimeOffset.UtcNow));

        // 空批次走系统权限点：开发环境端点在无数据时返回 0 条而不是报错。
        // （非空批次依赖 Npgsql 批量写入，SQLite 下不可测，见类注释。）
        IngestEnergyReadingsResultDto result =
            await service.IngestReadingsAsync(new IngestEnergyReadingsRequest([]));

        Assert.Equal(0, result.Accepted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Rejected);

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("energy.readings.ingest", audit.ActionCode);
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task Session_Issued_Long_Ago_Should_Still_Be_Accepted()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);

        // 会话只做滑动续期、无绝对过期：签发于 13 小时前的会话，只要安全戳仍匹配、
        // 身份合法即可继续操作——活跃用户不因登录时间久而被强制重新登录。
        AuditedParkEnergyQueryService service = CreateService(
            database, SuperAdmin(userId, stamp, DateTimeOffset.UtcNow.AddHours(-13)));
        EnergyMeterUpsertRequest request = await ValidMeterRequestAsync(database, "EM-LONGAGO-001");

        await service.UpsertMeterAsync(request);

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("success", audit.Result);
        Assert.Equal(userId, audit.UserId);
    }

    [Fact]
    public async Task StaleSecurityStamp_Should_Be_Denied()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        (Guid userId, Guid stamp) = await SeedSuperAdminAsync(database);

        // Cookie 里的安全戳与库中不一致：管理员重置密码后旧会话必须立即失效。
        Guid staleStamp = Guid.NewGuid();
        AuditedParkEnergyQueryService service = CreateService(
            database, SuperAdmin(userId, staleStamp, DateTimeOffset.UtcNow));
        EnergyMeterUpsertRequest staleRequest = await ValidMeterRequestAsync(database, "EM-STALE-001");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpsertMeterAsync(staleRequest));

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task AuthenticatedUser_WithoutEnergyDomain_Should_Be_Denied()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid userId = Guid.NewGuid();
        Guid stamp = Guid.NewGuid();

        // 一个启用中的普通账号：没有任何角色、任何域授权。
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            db.AppUsers.Add(new AppUser
            {
                Id = userId,
                UserName = "worker",
                DisplayName = "普通用户",
                IsEnabled = true,
                MustChangePassword = false,
                SecurityStamp = stamp,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        AuthUserDto dto = new(
            userId, "worker", "普通用户", null, [], [], AuthorizationCatalog.ViewAccessLevel,
            [], false, false);
        ClaimsPrincipal principal = PlatformClaimsFactory.CreatePrincipal(dto, DateTimeOffset.UtcNow, stamp.ToString());
        CurrentUser user = new(userId, "worker", "普通用户", null, "10.0.0.5", "UnitTest/1.0", false, principal);

        AuditedParkEnergyQueryService service = CreateService(database, user);
        EnergyMeterUpsertRequest viewerRequest = await ValidMeterRequestAsync(database, "EM-VIEWER-001");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpsertMeterAsync(viewerRequest));

        AuditLog audit = Assert.Single(await LoadAuditsAsync(database));
        Assert.Equal("failed", audit.Result);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("worker", audit.UserName);
    }

    /// <summary>固定返回同一操作者的会话上下文替身。</summary>
    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user);
        }
    }

    /// <summary>
    /// 审计作用域工厂：解析 DbContext、真实 AuditLogWriter（固定时钟）与真实 PlatformAuthService。
    /// AuthService 的目录登录、会话校验等依赖在本测试路径上不会被触发，以 null 占位。
    /// </summary>
    private sealed class AuditScopeFactory(EnergySqliteTestDatabase database, TimeProvider timeProvider)
        : IServiceScopeFactory
    {
        private readonly ServiceProvider _provider = Build(database, timeProvider);

        public IServiceScope CreateScope()
        {
            return _provider.CreateScope();
        }

        private static ServiceProvider Build(EnergySqliteTestDatabase database, TimeProvider timeProvider)
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.ClearProviders());
            services.AddScoped(_ => database.CreateContext());
            services.AddScoped<AuditLogWriter>(sp => new AuditLogWriter(
                sp.GetRequiredService<DataAcquisitionPlatformDbContext>(),
                timeProvider,
                NullLogger<AuditLogWriter>.Instance));
            services.AddScoped<PlatformAuthService>(sp => new PlatformAuthService(
                sp.GetRequiredService<DataAcquisitionPlatformDbContext>(),
                Options.Create(new PlatformAuthOptions()),
                timeProvider,
                null!,
                null!,
                null!,
                NullLogger<PlatformAuthService>.Instance));
            return services.BuildServiceProvider();
        }
    }
}

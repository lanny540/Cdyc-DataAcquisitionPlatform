using System.Security.Claims;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="AuditedDataAcquisitionService"/> 授权与审计装饰器测试。
///
/// 配置类写操作（采集点、后台数据定义的增删改）要求平台配置权限点并留痕；
/// 业务数据写入（采集上报、本地同步）与读方法原样转发不审计。
/// 该装饰器不注入用户上下文，操作者由守卫从 DI 作用域解析，覆盖这条解析路径。
/// </summary>
public class AuditedDataAcquisitionServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>固定返回指定操作者的用户上下文替身（注册进作用域供守卫解析）。</summary>
    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user);
        }
    }

    /// <summary>构造真实守卫所需的 DI 作用域工厂：DbContext + 审计写入器 + 用户上下文 + 固定时间。</summary>
    private static IServiceScopeFactory CreateScopeFactory(SqliteTestDatabase database, CurrentUser? user)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddScoped<AuditLogWriter>();
        services.AddScoped<ICurrentUserContext>(_ => new StubUserContext(user));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, params string[] permissions)
    {
        List<Claim> claims = [new(AuthorizationCatalog.PlatformClaimTypes.UserId, userId.ToString())];
        claims.AddRange(permissions.Select(permission =>
            new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication"));
    }

    private static Guid SeedUser(SqliteTestDatabase database, bool enabled = true)
    {
        Guid userId = Guid.NewGuid();

        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(new AppUser
        {
            Id = userId,
            UserName = "tester",
            DisplayName = "测试用户",
            IsEnabled = enabled,
            CreatedAt = FixedNow,
            UpdatedAt = FixedNow
        });
        db.SaveChanges();

        return userId;
    }

    private static AuditedDataAcquisitionService CreateService(
        SqliteTestDatabase database,
        InMemoryDataAcquisitionPlatformService inner,
        CurrentUser? user)
    {
        return new AuditedDataAcquisitionService(inner, CreateScopeFactory(database, user));
    }

    private static CurrentUser CreateOperator(Guid userId, ClaimsPrincipal principal)
    {
        return new CurrentUser(userId, "tester", "测试用户", "测试部门", "10.0.0.8", "UA-Test", false, principal);
    }

    /// <summary>从审计明细 JSON 中取指定属性的字符串值（序列化对非 ASCII 做了转义）。</summary>
    private static string DetailValue(AuditLog audit, string property)
    {
        Assert.NotNull(audit.Detail);
        using JsonDocument document = JsonDocument.Parse(audit.Detail);
        return document.RootElement.GetProperty(property).GetString()!;
    }

    private static List<AuditLog> ReadAudits(SqliteTestDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        // SQLite 不支持 DateTimeOffset 的服务端排序，拉回内存排。
        return db.AuditLogs.ToList().OrderBy(log => log.OccurredAt).ToList();
    }

    private static CollectionPointUpsertRequest PointRequest()
    {
        return new CollectionPointUpsertRequest(null, "CP-01", "一号采集点", "Modbus TCP",
            "192.168.10.21:502", true, "Server");
    }

    [Fact]
    public async Task 新增采集点_持有权限_执行并写成功审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.SystemManagePermission);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        CollectionPointDto point = await service.UpsertCollectionPointAsync(PointRequest());

        Assert.Equal("CP-01", point.Code);
        Assert.Equal("在线", point.CommunicationStatus);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("collection_point.upsert", audit.ActionCode);
        Assert.Equal("collection-point", audit.TargetType);
        Assert.Equal("一号采集点", audit.TargetName);
        Assert.Equal("success", audit.Result);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal(FixedNow, audit.OccurredAt);
        Assert.Equal("一号采集点", DetailValue(audit, "Name"));
    }

    [Fact]
    public async Task 新增采集点_缺少权限点_拒绝并写失败审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpsertCollectionPointAsync(PointRequest()));

        // 拒绝时业务不执行：内存替身里只有种子点位。
        Assert.Single(await inner.GetCollectionPointsAsync());

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("collection_point.upsert", audit.ActionCode);
        Assert.Equal("failed", audit.Result);
        Assert.Equal("缺少权限点", DetailValue(audit, "reason"));
        Assert.Equal(AuthorizationCatalog.SystemManagePermission, DetailValue(audit, "permission"));
    }

    [Fact]
    public async Task 删除采集点_持有权限_执行并写成功审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.SystemManagePermission);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        // 内存替身自带种子点位 MB-01，直接按其标识删除。
        CollectionPointDto seed = (await inner.GetCollectionPointsAsync()).Single();
        Assert.True(await service.DeleteCollectionPointAsync(seed.Id));
        Assert.Empty(await inner.GetCollectionPointsAsync());

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("collection_point.delete", audit.ActionCode);
        Assert.Equal("collection-point", audit.TargetType);
        Assert.Equal(seed.Id.ToString(), audit.TargetId);
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task 保存数据定义_持有权限_执行并写成功审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.SystemManagePermission);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        ManagedDataDefinitionDto definition = await service.UpsertManagedDataDefinitionAsync(
            new ManagedDataDefinitionUpsertRequest(
                null, "MD-01", "定义一", "周期采集", "opc.tcp://server", "ns=2;s=1",
                "造纸一车间", "P01", "温度", "℃", "描述", "{}", "{}", 30, true));

        Assert.Equal("MD-01", definition.Code);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("managed_data.upsert", audit.ActionCode);
        Assert.Equal("managed-data-definition", audit.TargetType);
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task 删除数据定义_缺少权限_拒绝并写失败审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.DeleteManagedDataDefinitionAsync(Guid.NewGuid()));

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("managed_data.delete", audit.ActionCode);
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task 写操作_无操作者_拒绝并写匿名失败审计()
    {
        using SqliteTestDatabase database = new();
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner, null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpsertCollectionPointAsync(PointRequest()));

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("failed", audit.Result);
        Assert.Null(audit.UserId);
        Assert.Equal("会话没有可识别的操作者", DetailValue(audit, "reason"));
    }

    [Fact]
    public async Task 读方法与业务数据写入_直接转发_不写审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        InMemoryDataAcquisitionPlatformService inner = new();
        AuditedDataAcquisitionService service = CreateService(database, inner,
            CreateOperator(userId, principal));

        // 查询、采集上报、本地同步都不是配置类写操作，不需要权限点也不留痕。
        await service.GetDashboardOverviewAsync();
        await service.GetCollectionPointsAsync();
        await service.GetManagedDataDefinitionsAsync();
        await service.GetManagedDataDefinitionDetailsAsync(Guid.NewGuid());
        await service.GetCollectionDataAsync();
        await service.IngestCollectionDataAsync(new IngestCollectionDataRequest(
            "MB-01", "温度", 42.5m, "℃", FixedNow));
        await service.SyncLocalCollectionPointsAsync(new SyncCollectionPointsRequest(
        [
            new LocalCollectionPointDto(
                Guid.NewGuid(), "LOCAL-01", "本地点", "Modbus TCP", "127.0.0.1:502", true, "待同步", FixedNow)
        ]));

        Assert.Equal(2, (await inner.GetCollectionPointsAsync()).Count);
        Assert.Empty(ReadAudits(database));
    }
}

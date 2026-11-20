using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// ServiceAuditGuard 的单元测试（docs/AUTH_DESIGN.md 7.2 / 9.4）。
///
/// 覆盖的核心规则：
/// - 无身份不允许写（IP 不是身份），拒绝也留痕；
/// - 写操作前回查账号是否仍启用（Blazor Server 电路建立后不再重新认证）；
/// - 缺少权限点拒绝并留痕；
/// - 执行成功 / 业务失败 / 异常三种结果都写审计，异常细节截断到 500 字；
/// - 操作内抛出的 ForbiddenAccessException 原样上抛，不追加异常审计。
/// </summary>
public sealed class ServiceAuditGuardTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user);
        }
    }

    /// <summary>
    /// 从审计明细 JSON 中取 reason 字段。
    /// Detail 列按默认 JSON 编码存储，非 ASCII 字符会转成 \uXXXX，直接 Contains 中文会失配。
    /// </summary>
    private static string? DetailReason(string? detail)
    {
        if (detail is null)
        {
            return null;
        }

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(detail);
        return document.RootElement.TryGetProperty("reason", out System.Text.Json.JsonElement reason)
            ? reason.GetString()
            : null;
    }

    private static CurrentUser BuildActor(
        Guid userId, bool isSuperAdmin = false, params string[] permissions)
    {
        List<Claim> claims =
        [
            new(AuthorizationCatalog.PlatformClaimTypes.UserId, userId.ToString()),
            new(AuthorizationCatalog.PlatformClaimTypes.UserName, "actor")
        ];
        if (isSuperAdmin)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true"));
        }

        foreach (string permission in permissions)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission));
        }

        ClaimsIdentity identity = new(claims, "TestAuthentication");
        return CurrentUser.FromPrincipal(new ClaimsPrincipal(identity), "127.0.0.1", "unit-test-agent")!;
    }

    /// <summary>
    /// 构造带 DbContext / AuditLogWriter / ICurrentUserContext 的作用域工厂，
    /// 与生产装配一致（装饰器是单例，依赖全部从请求作用域解析）。
    /// </summary>
    private static IServiceScopeFactory CreateScopeFactory(
        SqliteTestDatabase database, ICurrentUserContext? scopedUser, TimeProvider? time = null)
    {
        FixedTimeProvider fixedTime = new(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped(_ => database.CreateContext());
        services.AddScoped<AuditLogWriter>(sp => new AuditLogWriter(
            sp.GetRequiredService<DataAcquisitionPlatformDbContext>(),
            time ?? fixedTime,
            NullLogger<AuditLogWriter>.Instance));

        if (scopedUser is not null)
        {
            services.AddScoped<ICurrentUserContext>(_ => scopedUser!);
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static AppUser SeedUser(SqliteTestDatabase database, bool isEnabled = true)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = "guard-user",
            DisplayName = "守卫用户",
            DepartmentId = null,
            IsEnabled = isEnabled,
            MustChangePassword = false,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        db.AppUsers.Add(user);
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task ExecuteAsync_Without_User_Should_Forbid_And_Audit()
    {
        using SqliteTestDatabase database = new();
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(null));
        bool operationInvoked = false;

        ForbiddenAccessException ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ServiceAuditGuard.ExecuteAsync(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", "id-1", "11 月排班",
                _ =>
                {
                    operationInvoked = true;
                    return Task.FromResult(1);
                }));

        Assert.False(operationInvoked);
        Assert.Contains("会话已失效", ex.Message);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("failed", audit.Result);
        Assert.Equal("shift.import", audit.ActionCode);
        Assert.Equal("会话没有可识别的操作者", DetailReason(audit.Detail));
    }

    [Fact]
    public async Task ExecuteAsync_Unknown_User_Should_Forbid()
    {
        using SqliteTestDatabase database = new();
        CurrentUser ghost = BuildActor(Guid.NewGuid(), permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(ghost));

        ForbiddenAccessException ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ServiceAuditGuard.ExecuteAsync(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ => Task.FromResult(1)));

        Assert.Contains("账号已停用", ex.Message);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("账号已停用或不存在", DetailReason(audit.Detail));
    }

    [Fact]
    public async Task ExecuteAsync_Disabled_User_Should_Forbid_Before_Writing()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database, false);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));
        bool operationInvoked = false;

        // 停用只对 HTTP 请求生效，电路写操作必须在执行前回查一次库。
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ServiceAuditGuard.ExecuteAsync(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ =>
                {
                    operationInvoked = true;
                    return Task.FromResult(1);
                }));

        Assert.False(operationInvoked);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("failed", audit.Result);
        Assert.Equal("账号已停用或不存在", DetailReason(audit.Detail));
    }

    [Fact]
    public async Task ExecuteAsync_Without_Permission_Should_Forbid_And_Audit()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id); // 无任何权限点
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        ForbiddenAccessException ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ServiceAuditGuard.ExecuteAsync(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ => Task.FromResult(1)));

        Assert.Contains("没有执行该操作的权限", ex.Message);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("缺少权限点", DetailReason(audit.Detail));
        Assert.Contains(AuthorizationCatalog.ShiftManagePermission, audit.Detail);
    }

    [Fact]
    public async Task ExecuteAsync_Authorized_Should_Execute_And_Audit_Success()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        int result = await ServiceAuditGuard.ExecuteAsync(
            scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
            "shift-schedule", "id-1", "11 月排班",
            current =>
            {
                Assert.Equal(actor.UserId, current.UserId);
                return Task.FromResult(42);
            },
            detailFactory: value => new { count = value });

        Assert.Equal(42, result);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("success", audit.Result);
        Assert.Equal("shift.import", audit.ActionCode);
        Assert.Equal("actor", audit.UserName);
        Assert.Contains("\"count\":42", audit.Detail);
    }

    [Fact]
    public async Task ExecuteAsync_Failed_Selector_Should_Audit_Failure()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        // 业务失败（如导入行数 0）不算异常，但仍要在审计里记 failed。
        await ServiceAuditGuard.ExecuteAsync(
            scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
            "shift-schedule", null, null,
            _ => Task.FromResult(0),
            imported => imported > 0);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task ExecuteAsync_Operation_Exception_Should_Audit_And_Rethrow()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServiceAuditGuard.ExecuteAsync<int>(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ => throw new InvalidOperationException("导入文件格式错误")));

        Assert.Equal("导入文件格式错误", ex.Message);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("failed", audit.Result);
        Assert.Equal("导入文件格式错误", DetailReason(audit.Detail));
        Assert.Contains("InvalidOperationException", audit.Detail);
    }

    [Fact]
    public async Task ExecuteAsync_Exception_Detail_Should_Be_Truncated_To_500_Chars()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));
        string longMessage = new('错', 600);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServiceAuditGuard.ExecuteAsync<int>(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ => throw new InvalidOperationException(longMessage)));

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();

        // 异常细节截断到 500 字，避免堆栈撑爆 detail 列。
        Assert.Equal(500, DetailReason(audit.Detail)!.Length);
        Assert.Equal(new string('错', 500), DetailReason(audit.Detail));
    }

    [Fact]
    public async Task ExecuteAsync_Forbidden_From_Operation_Should_Rethrow_Without_Exception_Audit()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        // 业务内部已按授权语义拒绝（例如目标跨部门）：守卫不重复记异常审计。
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ServiceAuditGuard.ExecuteAsync<int>(
                scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
                "shift-schedule", null, null,
                _ => throw new ForbiddenAccessException("目标班组不在你的部门内。")));

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_CallerUserContext_Should_Override_Scoped_Resolution()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, permissions: AuthorizationCatalog.ShiftManagePermission);

        // 作用域内解析到的是「无身份」，调用方显式传入的上下文必须优先生效
        // ——Blazor Server 电路路径的 GetRequiredService 拿不到请求级实现。
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(null));

        int result = await ServiceAuditGuard.ExecuteAsync(
            scopeFactory, AuthorizationCatalog.ShiftManagePermission, "shift.import",
            "shift-schedule", null, null,
            _ => Task.FromResult(7),
            callerUserContext: new StubUserContext(actor));

        Assert.Equal(7, result);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task ExecuteAsync_SuperAdmin_Should_Pass_Permission_Check()
    {
        using SqliteTestDatabase database = new();
        AppUser user = SeedUser(database);
        CurrentUser actor = BuildActor(user.Id, true);
        IServiceScopeFactory scopeFactory = CreateScopeFactory(database, new StubUserContext(actor));

        // 超管不带权限点 claim，也必须通过任意权限点判定。
        int result = await ServiceAuditGuard.ExecuteAsync(
            scopeFactory, AuthorizationCatalog.SystemManagePermission, "domain.update",
            "domain", null, null,
            _ => Task.FromResult(1));

        Assert.Equal(1, result);
    }
}

using System.Security.Claims;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="AuditedShiftSchedulingService"/> 授权与审计装饰器测试。
///
/// SSR 页面直调与 HTTP 端点都会走到装饰器，这里逐条验证守卫语义：
/// 导入与清空要求排班配置权限点、无身份/停用/越权一律拒绝且拒绝也留痕、
/// 业务成功与失败都写审计、读方法原样转发不留痕。
/// </summary>
public class AuditedShiftSchedulingServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly string WorkBook = "nov-2026.xlsx";

    /// <summary>固定返回指定操作者的用户上下文替身。</summary>
    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user);
        }
    }

    /// <summary>记录调用次数的排班服务替身，可注入导入结果或异常。</summary>
    private sealed class StubShiftSchedulingService : IShiftSchedulingService
    {
        public int ImportCalls;
        public int ClearCalls;
        public int StatusCalls;
        public int CalendarCalls;
        public int TeamScheduleCalls;
        public int AllRowsCalls;

        public bool ImportSucceeded = true;
        public bool ClearResult = true;
        public Exception? ImportException;

        public Task<ShiftScheduleImportResultDto> ImportAsync(
            string fileName,
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            if (ImportException is not null)
            {
                throw ImportException;
            }

            ShiftScheduleRowDto row = new(
                new DateOnly(2026, 11, 19), "T1", "一班", "早班",
                new TimeOnly(8, 0), new TimeOnly(16, 0), false, 8);

            return Task.FromResult(new ShiftScheduleImportResultDto(
                ImportSucceeded, fileName, [row, row],
                new ShiftScheduleValidationDto(ImportSucceeded, 2, 1, 1, 1, "均衡", []),
                FixedNow));
        }

        public Task<bool> ClearAsync()
        {
            ClearCalls++;
            return Task.FromResult(ClearResult);
        }

        public Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(new ShiftScheduleStatusDto(
                true, [new ShiftTeamDto("T1", "一班")], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
                2, FixedNow, WorkBook));
        }

        public Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
            DateOnly from,
            DateOnly to,
            string? teamCode = null,
            CancellationToken cancellationToken = default)
        {
            CalendarCalls++;
            return Task.FromResult<IReadOnlyCollection<DailyShiftScheduleDto>>([]);
        }

        public Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
            string teamCode,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            TeamScheduleCalls++;
            return Task.FromResult(new TeamShiftScheduleDto(teamCode, "一班", [], 0, 0, 0));
        }

        public Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(
            CancellationToken cancellationToken = default)
        {
            AllRowsCalls++;
            return Task.FromResult<IReadOnlyCollection<ShiftScheduleRowDto>>([]);
        }
    }

    /// <summary>构造真实守卫所需的 DI 作用域工厂：DbContext + 审计写入器 + 固定时间。</summary>
    private static IServiceScopeFactory CreateScopeFactory(SqliteTestDatabase database)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddScoped<AuditLogWriter>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, params string[] permissions)
    {
        List<Claim> claims = [new(AuthorizationCatalog.PlatformClaimTypes.UserId, userId.ToString())];
        claims.AddRange(permissions.Select(permission =>
            new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication"));
    }

    private static CurrentUser CreateUser(
        Guid userId,
        ClaimsPrincipal principal,
        string userName = "tester")
    {
        return new CurrentUser(userId, userName, "测试用户", "测试部门", "10.0.0.8", "UA-Test", false, principal);
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

    private static AuditedShiftSchedulingService CreateService(
        SqliteTestDatabase database,
        StubShiftSchedulingService inner,
        CurrentUser? user)
    {
        return new AuditedShiftSchedulingService(
            inner,
            CreateScopeFactory(database),
            new StubUserContext(user));
    }

    [Fact]
    public async Task 导入_持有权限且账号启用_执行并写成功审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.ShiftManagePermission);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        ShiftScheduleImportResultDto result =
            await service.ImportAsync(WorkBook, new MemoryStream([1, 2, 3]));

        Assert.Equal(1, inner.ImportCalls);
        Assert.True(result.Succeeded);

        // 审计完整记录操作者与明细：actor 永远是员工个人。
        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("shift.import", audit.ActionCode);
        Assert.Equal("shift-schedule", audit.TargetType);
        Assert.Equal(WorkBook, audit.TargetName);
        Assert.Equal("success", audit.Result);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("tester", audit.UserName);
        Assert.Equal("测试部门", audit.DepartmentName);
        Assert.Equal("10.0.0.8", audit.Ip);
        Assert.Equal("UA-Test", audit.UserAgent);
        Assert.Equal(FixedNow, audit.OccurredAt);
        Assert.Contains("\"importedRows\":2", audit.Detail);
    }

    [Fact]
    public async Task 导入_缺少权限点_拒绝执行并写失败审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        ForbiddenAccessException ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.ImportAsync(WorkBook, new MemoryStream()));

        Assert.Contains("没有执行该操作的权限", ex.Message);
        Assert.Equal(0, inner.ImportCalls);

        // 拒绝也留痕：否则连续尝试越权的痕迹会消失。
        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("shift.import", audit.ActionCode);
        Assert.Equal("failed", audit.Result);
        Assert.Equal("缺少权限点", DetailValue(audit, "reason"));
        Assert.Equal(AuthorizationCatalog.ShiftManagePermission, DetailValue(audit, "permission"));
    }

    [Fact]
    public async Task 导入_会话没有可识别的操作者_拒绝并写匿名审计()
    {
        using SqliteTestDatabase database = new();
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.ImportAsync(WorkBook, new MemoryStream()));

        Assert.Equal(0, inner.ImportCalls);

        // IP 不是身份：没有可归责的操作者就不允许写。
        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("failed", audit.Result);
        Assert.Null(audit.UserId);
        Assert.Null(audit.UserName);
        Assert.Equal("会话没有可识别的操作者", DetailValue(audit, "reason"));
    }

    [Fact]
    public async Task 导入_账号已停用_拒绝执行并写失败审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database, false);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.ShiftManagePermission);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        // Blazor Server 电路建立后不再重新认证：停用必须在写操作前回查一次库。
        ForbiddenAccessException ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.ImportAsync(WorkBook, new MemoryStream()));

        Assert.Contains("账号已停用", ex.Message);
        Assert.Equal(0, inner.ImportCalls);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("failed", audit.Result);
        Assert.Equal("账号已停用或不存在", DetailValue(audit, "reason"));
    }

    [Fact]
    public async Task 导入_业务校验未通过_结果记为failed()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.ShiftManagePermission);
        StubShiftSchedulingService inner = new() { ImportSucceeded = false };
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        ShiftScheduleImportResultDto result =
            await service.ImportAsync(WorkBook, new MemoryStream());

        Assert.False(result.Succeeded);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("failed", audit.Result);
        Assert.Contains("\"Succeeded\":false", audit.Detail);
    }

    [Fact]
    public async Task 导入_业务抛异常_审计记失败并重抛()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.ShiftManagePermission);
        StubShiftSchedulingService inner = new() { ImportException = new InvalidOperationException("解析失败：表头不识别") };
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(WorkBook, new MemoryStream()));

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("failed", audit.Result);
        Assert.Contains("解析失败", DetailValue(audit, "reason"));
        Assert.Equal("InvalidOperationException", DetailValue(audit, "errorType"));
    }

    [Fact]
    public async Task 清空_持有权限_执行并写成功审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId, AuthorizationCatalog.ShiftManagePermission);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        Assert.True(await service.ClearAsync());
        Assert.Equal(1, inner.ClearCalls);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("shift.clear", audit.ActionCode);
        Assert.Equal("shift-schedule", audit.TargetType);
        Assert.Equal("全部排班数据", audit.TargetName);
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task 清空_缺少权限点_拒绝并写失败审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        await Assert.ThrowsAsync<ForbiddenAccessException>(service.ClearAsync);

        Assert.Equal(0, inner.ClearCalls);

        AuditLog audit = Assert.Single(ReadAudits(database));
        Assert.Equal("shift.clear", audit.ActionCode);
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task 读方法直接转发_不写审计()
    {
        using SqliteTestDatabase database = new();
        Guid userId = SeedUser(database);
        ClaimsPrincipal principal = CreatePrincipal(userId);
        StubShiftSchedulingService inner = new();
        AuditedShiftSchedulingService service = CreateService(database, inner, CreateUser(userId, principal));

        // 只读方法不做授权判定也不留痕：查询与登录不留审计。
        await service.GetStatusAsync();
        await service.GetCalendarAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        await service.GetTeamScheduleAsync("T1", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        await service.GetAllRowsAsync();

        Assert.Equal(1, inner.StatusCalls);
        Assert.Equal(1, inner.CalendarCalls);
        Assert.Equal(1, inner.TeamScheduleCalls);
        Assert.Equal(1, inner.AllRowsCalls);
        Assert.Empty(ReadAudits(database));
    }
}

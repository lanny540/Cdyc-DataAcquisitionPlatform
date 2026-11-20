using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// AuditLogWriter 的单元测试（docs/AUTH_DESIGN.md 9.4）。
///
/// 覆盖的核心规则：
/// - actor 字段（用户/部门/IP/UserAgent）完整落库，失败操作同样记录（result = failed）；
/// - detail 序列化为 JSON，null 字段忽略；
/// - 审计写失败不阻断业务，只记 ERROR 日志。
/// </summary>
public sealed class AuditLogWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static CurrentUser BuildActor()
    {
        ClaimsIdentity identity = new(
        [
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserName, "actor-a"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.DisplayName, "操作者"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.DepartmentName, "一部")
        ], "TestAuthentication");
        return CurrentUser.FromPrincipal(new ClaimsPrincipal(identity), "10.1.2.3", "test-agent")!;
    }

    private static AuditLogWriter CreateWriter(DataAcquisitionPlatformDbContext db, TimeProvider? time = null)
    {
        return new AuditLogWriter(db, time ?? new FixedTimeProvider(FixedNow), NullLogger<AuditLogWriter>.Instance);
    }

    [Fact]
    public async Task RecordAsync_Should_Write_All_Actor_Fields()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        CurrentUser actor = BuildActor();

        await CreateWriter(db).RecordAsync(
            actor, "shift.import", "shift-schedule", "id-1", "11 月排班", true,
            new { month = "2026-11" });

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog log = await verify.AuditLogs.SingleAsync();

        Assert.Equal(actor.UserId, log.UserId);
        Assert.Equal("actor-a", log.UserName);
        Assert.Equal("一部", log.DepartmentName);
        Assert.Equal("10.1.2.3", log.Ip);
        Assert.Equal("test-agent", log.UserAgent);
        Assert.Equal("shift.import", log.ActionCode);
        Assert.Equal("shift-schedule", log.TargetType);
        Assert.Equal("id-1", log.TargetId);
        Assert.Equal("11 月排班", log.TargetName);
        Assert.Equal("success", log.Result);
        Assert.Equal(FixedNow, log.OccurredAt);
        Assert.Contains("\"month\":\"2026-11\"", log.Detail);
    }

    [Fact]
    public async Task RecordAsync_Failed_Operation_Should_Be_Recorded()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        // 失败的操作也记录，否则连续尝试越权的痕迹会消失。
        await CreateWriter(db).RecordAsync(
            BuildActor(), "user.update", "user", "id-2", "victim", false,
            new { reason = "越权" });

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog log = await verify.AuditLogs.SingleAsync();
        Assert.Equal("failed", log.Result);
    }

    [Fact]
    public async Task RecordAsync_Null_Actor_Should_Write_Anonymous_Record()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        await CreateWriter(db).RecordAsync(
            null, "domain_page.update", "domain-page", null, null, false,
            new { reason = "会话没有可识别的操作者" });

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog log = await verify.AuditLogs.SingleAsync();
        Assert.Null(log.UserId);
        Assert.Null(log.UserName);
        Assert.Null(log.DepartmentName);
        Assert.Null(log.Ip);
        Assert.Null(log.UserAgent);
    }

    [Fact]
    public async Task RecordAsync_Detail_Should_Skip_Null_Fields()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        await CreateWriter(db).RecordAsync(
            BuildActor(), "domain.update", "domain", "id-3", null, true,
            new { before = (string?)null, after = "新名" });

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog log = await verify.AuditLogs.SingleAsync();

        // null 字段不写入 JSON（JsonIgnoreCondition.WhenWritingNull），避免 detail 膨胀。
        // 非 ASCII 默认转 \uXXXX，用 JSON 解析断言而不是字符串比对。
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(log.Detail!);
        Assert.Equal(["after"], document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("新名", document.RootElement.GetProperty("after").GetString());
    }

    [Fact]
    public async Task RecordAsync_Null_Detail_Should_Write_Null_Column()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        await CreateWriter(db).RecordAsync(BuildActor(), "domain.create", "domain", "id-4", "域", true, null);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog log = await verify.AuditLogs.SingleAsync();
        Assert.Null(log.Detail);
    }

    [Fact]
    public async Task RecordAsync_Write_Failure_Should_Not_Throw()
    {
        SqliteTestDatabase database = new();
        DataAcquisitionPlatformDbContext db = database.CreateContext();
        database.Dispose(); // 连接释放后 SaveChanges 必然失败

        // 审计写失败不阻断业务（排班服务是内存实现、没有事务可挂）。
        Exception? thrown = await Record.ExceptionAsync(() =>
            CreateWriter(db).RecordAsync(BuildActor(), "shift.import", "shift-schedule", null, null, true, null));

        Assert.Null(thrown);
    }

    /// <summary>捕获异步操作的异常，便于断言「不应抛出」。</summary>
    private static class Record
    {
        public static async Task<Exception?> ExceptionAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}

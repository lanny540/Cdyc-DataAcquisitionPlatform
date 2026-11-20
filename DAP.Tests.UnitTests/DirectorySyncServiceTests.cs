using System.Security.Claims;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Presentation.BlazorWeb.Security.Directory;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 目录同步「应用链路」的单元测试（判定规则见 DirectorySyncPlannerTests）。
///
/// 这里钉住的是把决定落库时的铁律：幂等 upsert 只改可同步字段、角色一行不碰、
/// 目录返回 0 个账号时整轮作废、手工停用不被同步悄悄打开、超管永不停用。
/// </summary>
public sealed class DirectorySyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private const string BootstrapPassword = "Initial@123";

    /// <summary>
    /// 无操作的锁句柄：模拟「拿到锁」。
    /// </summary>
    private sealed class NoOpLockHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 咨询锁替身：可配置总是拿不到锁（返回 null）或直接放行。
    /// 基类构造参数用安全占位值，不会真的连接 PostgreSQL。
    /// </summary>
    private sealed class StubAdvisoryLock(bool granted) : PlatformAdvisoryLock(
        new PostgreSqlConnectionSettings("Host=localhost;Database=dap-test;Username=test;Password=test"),
        NullLogger<PlatformAdvisoryLock>.Instance)
    {
        public int AcquireCalls;

        public override Task<IAsyncDisposable?> TryAcquireAsync(
            string name, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            return Task.FromResult<IAsyncDisposable?>(granted ? new NoOpLockHandle() : null);
        }
    }

    /// <summary>
    /// 目录替身：可配置返回的账号列表、认证结果与读取失败。
    /// </summary>
    private sealed class StubLdapDirectory : ILdapDirectory
    {
        private readonly LdapAuthenticationResult _authentication;
        private readonly IReadOnlyList<LdapDirectoryUser> _users;

        public Exception? ListError;
        public int ListCalls;

        public StubLdapDirectory(
            IReadOnlyList<LdapDirectoryUser>? users = null,
            LdapAuthenticationResult? authentication = null)
        {
            _users = users ?? [];
            _authentication = authentication ?? LdapAuthenticationResult.Failed("账号或密码不正确。");
        }

        public Task<LdapAuthenticationResult> AuthenticateAsync(
            string? userName, string? password, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_authentication);
        }

        public Task<IReadOnlyList<LdapDirectoryUser>> ListUsersAsync(
            CancellationToken cancellationToken = default)
        {
            ListCalls++;

            return ListError is null
                ? Task.FromResult(_users)
                : Task.FromException<IReadOnlyList<LdapDirectoryUser>>(ListError);
        }
    }

    private static PlatformAuthOptions EnabledOptions()
    {
        return new PlatformAuthOptions { Ldap = { Enabled = true, DefaultRoleCode = AuthorizationCatalog.ViewerRole } };
    }

    private static DirectorySyncService CreateService(
        DataAcquisitionPlatformDbContext db,
        ILdapDirectory directory,
        PlatformAuthOptions options,
        StubAdvisoryLock advisoryLock,
        UserSessionValidator? validator = null)
    {
        return new DirectorySyncService(
            db,
            directory,
            Options.Create(options),
            new FixedTimeProvider(Now),
            new AuditLogWriter(db, new FixedTimeProvider(Now), NullLogger<AuditLogWriter>.Instance),
            advisoryLock,
            validator ?? new UserSessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            NullLogger<DirectorySyncService>.Instance);
    }

    private static CurrentUser Actor()
    {
        return new CurrentUser(
            Guid.NewGuid(), "admin", "超管", null, "127.0.0.1", "test-agent",
            true, new ClaimsPrincipal(new ClaimsIdentity()));
    }

    // ---------- 种子辅助 ----------

    private static Role SeedRole(string code, bool enabled = true, int pageLevel = 1)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PageLevel = pageLevel,
            IsBuiltin = true,
            IsEnabled = enabled,
            SortOrder = 10,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static Department SeedDepartment(string externalCode)
    {
        return new Department
        {
            Id = Guid.NewGuid(),
            Code = $"D-{externalCode}",
            Name = externalCode,
            ExternalCode = externalCode,
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static AppUser SeedUser(
        string userName, bool enabled = true, string? disabledSource = null,
        Guid? departmentId = null, string password = BootstrapPassword)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = userName,
            DepartmentId = departmentId,
            IsEnabled = enabled,
            DisabledSource = disabledSource,
            MustChangePassword = false,
            PasswordHash = PasswordHasher.Hash(password),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static ExternalIdentity SeedIdentity(Guid userId, string externalId)
    {
        return new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = DirectorySyncService.LdapProvider,
            ExternalId = externalId,
            EmployeeNo = null,
            LastSyncedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static LdapDirectoryUser Record(
        string externalId,
        string userName,
        string displayName = "未命名",
        string? email = null,
        string? departmentCode = null,
        bool enabled = true)
    {
        return new LdapDirectoryUser(externalId, userName, displayName, email, departmentCode, null, enabled);
    }

    // ---------- 启用状态与互斥锁 ----------

    [Fact]
    public async Task RunAsync_Ldap_Disabled_Should_Skip_Without_Touching_Lock()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        StubAdvisoryLock advisoryLock = new(false);
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory(), new PlatformAuthOptions(), advisoryLock);

        DirectorySyncRunDto result = await service.RunAsync(Actor(), "manual");

        Assert.False(result.Succeeded);
        Assert.Equal("目录认证未启用，未执行同步。", result.Message);
        Assert.Equal(0, advisoryLock.AcquireCalls);
    }

    [Fact]
    public async Task RunAsync_Lock_Unavailable_Should_Skip_This_Round()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        StubLdapDirectory directory = new([Record("ext-1", "zhang3")]);
        DirectorySyncService service = CreateService(db, directory, EnabledOptions(), new StubAdvisoryLock(false));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        // 另一实例正在同步：直接跳过，不去读目录。
        Assert.False(result.Succeeded);
        Assert.Equal("已有同步任务正在执行，请稍后刷新查看结果。", result.Message);
        Assert.Equal(0, directory.ListCalls);
    }

    // ---------- 目录读取失败与空结果防护 ----------

    [Fact]
    public async Task RunAsync_Directory_Failure_Should_Abort_With_Failed_Audit()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        StubLdapDirectory directory = new() { ListError = new InvalidOperationException("连接被拒绝") };
        DirectorySyncService service = CreateService(db, directory, EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(Actor(), "manual");

        Assert.False(result.Succeeded);
        Assert.Equal("无法连接目录服务，本次同步已取消，账号保持原状。", result.Message);

        // 失败也留审计：连续尝试越权的痕迹不能消失。
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog? audit = await verify.AuditLogs
            .SingleOrDefaultAsync(log => log.ActionCode == DirectorySyncService.SyncActionCode);
        Assert.NotNull(audit);
        Assert.Equal("failed", audit!.Result);
        Assert.Equal("admin", audit.UserName);
    }

    [Fact]
    public async Task RunAsync_Empty_Directory_Should_Skip_Disable_Phase()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser bound = SeedUser("zhang3");
        db.AppUsers.Add(bound);
        db.ExternalIdentities.Add(SeedIdentity(bound.Id, "ext-1"));
        await db.SaveChangesAsync();

        // 目录挂了、权限被回收、过滤器写错，表现都是「返回 0 个」：
        // 照常执行等于把全平台账号停用。
        DirectorySyncService service =
            CreateService(db, new StubLdapDirectory(), EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.False(result.Succeeded);
        Assert.Contains("目录未返回任何账号", result.Message);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.True((await verify.AppUsers.SingleAsync(item => item.Id == bound.Id)).IsEnabled);
    }

    // ---------- 建号 / 更新 / 无变化 ----------

    [Fact]
    public async Task RunAsync_New_Record_Should_Create_User_With_Default_Role()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department workshop = SeedDepartment("PAPER-1");
        Role viewer = SeedRole(AuthorizationCatalog.ViewerRole);
        db.Departments.Add(workshop);
        db.Roles.Add(viewer);
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", "zhang3@example.com", "PAPER-1")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Disabled);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Issues);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.ExternalIdentities)
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(user => user.UserName == "zhang3");

        Assert.Null(created.PasswordHash);
        Assert.Equal("张三", created.DisplayName);
        Assert.Equal("zhang3@example.com", created.Email);
        Assert.Equal(workshop.Id, created.DepartmentId);
        Assert.True(created.IsEnabled);
        Assert.Contains(created.ExternalIdentities,
            identity => identity.ExternalId == "ext-1" && identity.Provider == DirectorySyncService.LdapProvider);
        Assert.Contains(created.UserRoles, link => link.Role!.Code == AuthorizationCatalog.ViewerRole);
    }

    [Fact]
    public async Task RunAsync_New_Record_With_Missing_Default_Role_Should_Create_Without_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        // 默认角色存在但停用：账号建出来但没有任何角色。
        Role disabledRole = SeedRole(AuthorizationCatalog.ViewerRole, false);
        db.Roles.Add(disabledRole);
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.UserRoles)
            .SingleAsync(user => user.UserName == "zhang3");
        Assert.Empty(created.UserRoles);
    }

    [Fact]
    public async Task RunAsync_Changed_Fields_Should_Update_Without_Touching_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department workshop = SeedDepartment("PAPER-1");
        Role technician = SeedRole(AuthorizationCatalog.TechnicianRole, pageLevel: 2);
        Department other = SeedDepartment("ENERGY-1");
        db.Departments.Add(workshop);
        db.Departments.Add(other);
        db.Roles.Add(technician);

        AppUser user = SeedUser("zhang3", departmentId: workshop.Id);
        user.DisplayName = "张三";
        user.UpdatedAt = Now.AddDays(-1);
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = technician.Id });
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三丰", "zhangsanfeng@example.com", "ENERGY-1")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Created);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser updated = await verify.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(item => item.Id == user.Id);

        Assert.Equal("张三丰", updated.DisplayName);
        Assert.Equal("zhangsanfeng@example.com", updated.Email);
        Assert.Equal(other.Id, updated.DepartmentId);
        Assert.Equal(Now, updated.UpdatedAt);
        // 角色一行不碰：管理员手工配的权限不能被每小时冲一次。
        Assert.Equal([AuthorizationCatalog.TechnicianRole],
            updated.UserRoles.Select(link => link.Role!.Code).ToList());
    }

    [Fact]
    public async Task RunAsync_Unchanged_Record_Should_Not_Rewrite_User()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3");
        user.DisplayName = "张三";
        user.Email = "zhang3@example.com";
        // 种子时间取过去：若同步误写 updated_at，断言立刻失败。
        user.UpdatedAt = Now.AddDays(-1);
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", "zhang3@example.com")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Updated);

        // 每小时一次的全量同步不该把所有行都刷一遍 updated_at。
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser unchanged = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.Equal(Now.AddDays(-1), unchanged.UpdatedAt);
    }

    // ---------- 停用与手工停用保持 ----------

    [Fact]
    public async Task RunAsync_Disabled_In_Directory_Should_Disable_And_Kick_Sessions()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3");
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        await db.SaveChangesAsync();
        string oldStamp = user.SecurityStamp.ToString();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", enabled: false)]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Disabled);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser disabled = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.False(disabled.IsEnabled);
        // 停用来源标记为 directory：目录里恢复后才能自动恢复。
        Assert.Equal(UserDisableSources.Directory, disabled.DisabledSource);
        // 人在目录里被停用：会话必须立刻失效，不能等 12 小时。
        Assert.NotEqual(oldStamp, disabled.SecurityStamp.ToString());
    }

    [Fact]
    public async Task RunAsync_Manually_Disabled_Should_Stay_Disabled_When_Still_In_Directory()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3", false, UserDisableSources.Manual);
        user.DisplayName = "张三";
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", enabled: true)]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        // 管理员手工停用的账号（离职流程、借调、调查期间）必须保持停用。
        Assert.Equal(1, result.KeptManualDisable);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser kept = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.False(kept.IsEnabled);
        Assert.Equal(UserDisableSources.Manual, kept.DisabledSource);
    }

    [Fact]
    public async Task RunAsync_Vanished_User_Should_Be_Disabled_Not_Deleted()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser bound = SeedUser("zhang3");
        db.AppUsers.Add(bound);
        db.ExternalIdentities.Add(SeedIdentity(bound.Id, "ext-1"));
        await db.SaveChangesAsync();

        // 目录里只剩别人：zhang3 已消失。
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-2", "wang5", "王五")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Disabled);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        // 账号保留优先于删除：历史审计里的账号名不能变成悬空引用。
        AppUser vanished = await verify.AppUsers.SingleAsync(item => item.Id == bound.Id);
        Assert.False(vanished.IsEnabled);
        Assert.Equal(UserDisableSources.Directory, vanished.DisabledSource);
    }

    [Fact]
    public async Task RunAsync_Vanished_Super_Admin_Should_Stay_Enabled()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Role superAdminRole = SeedRole(AuthorizationCatalog.SuperAdminRole, pageLevel: 99);
        db.Roles.Add(superAdminRole);
        AppUser root = SeedUser("root");
        db.AppUsers.Add(root);
        db.ExternalIdentities.Add(SeedIdentity(root.Id, "ext-root"));
        db.UserRoles.Add(new UserRole { UserId = root.Id, RoleId = superAdminRole.Id });
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-2", "wang5", "王五")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        // 超管已不在目录中：出于唯一性约束保持启用，需人工确认。
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.True((await verify.AppUsers.SingleAsync(item => item.Id == root.Id)).IsEnabled);
    }

    // ---------- 待处理项（不自动建部门 / 不自动并号） ----------

    [Fact]
    public async Task RunAsync_Unmatched_Department_Should_Record_Issue_And_Keep_Null_Department()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", departmentCode: "UNKNOWN-DEPT")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto first = await service.RunAsync(null, "schedule");

        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(1, first.Issues);

        await using (DataAcquisitionPlatformDbContext verify = database.CreateContext())
        {
            AppUser created = await verify.AppUsers.SingleAsync(item => item.UserName == "zhang3");
            Assert.Null(created.DepartmentId);

            DirectorySyncIssue issue = await verify.DirectorySyncIssues
                .SingleAsync(item => item.Provider == DirectorySyncService.LdapProvider);
            Assert.Equal(DirectorySyncIssue.DepartmentUnmatched, issue.IssueType);
            Assert.Equal("UNKNOWN-DEPT", issue.ExternalCode);
            Assert.Equal(1, issue.Occurrences);
        }

        // 第二轮命中同一项：递增计数，不重复建行。
        DirectorySyncRunDto second = await service.RunAsync(null, "schedule");

        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(1, second.Issues);

        await using DataAcquisitionPlatformDbContext final = database.CreateContext();
        DirectorySyncIssue existing = await final.DirectorySyncIssues
            .SingleAsync(item => item.Provider == DirectorySyncService.LdapProvider);
        Assert.Equal(2, existing.Occurrences);
    }

    [Fact]
    public async Task RunAsync_UserName_Conflict_Should_Record_Issue_Without_Merging()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        // 本地账号 li4：没有绑定目录，同名目录账号不能并号。
        AppUser local = SeedUser("li4");
        db.AppUsers.Add(local);
        await db.SaveChangesAsync();

        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-4", "li4", "李四")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncRunDto result = await service.RunAsync(null, "schedule");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Issues);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(1, await verify.AppUsers.CountAsync(item => item.UserName == "li4"));
        DirectorySyncIssue issue = await verify.DirectorySyncIssues.SingleAsync();
        Assert.Equal(DirectorySyncIssue.UserNameConflict, issue.IssueType);
        Assert.Equal("li4", issue.ExternalCode);
    }

    // ---------- 审计与状态查询 ----------

    [Fact]
    public async Task RunAsync_Success_Should_Write_Success_Audit_With_Summary()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        await service.RunAsync(Actor(), "manual");

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs
            .SingleAsync(log => log.ActionCode == DirectorySyncService.SyncActionCode);
        Assert.Equal("success", audit.Result);

        // detail 是 JSON（非 ASCII 字符按默认编码转义），解析后校验摘要。
        using JsonDocument document = JsonDocument.Parse(audit.Detail!);
        string? summary = document.RootElement.GetProperty("summary").GetString();
        Assert.NotNull(summary);
        Assert.Contains("同步完成", summary);
        Assert.Contains("新建 1", summary);
        Assert.Equal("manual", document.RootElement.GetProperty("trigger").GetString());
    }

    // ---------- 状态查询（GetStatusAsync 依赖 DateTimeOffset 排序） ----------

    /// <summary>
    /// DateTimeOffset → UtcTicks 值转换器：SQLite 提供程序默认把 DateTimeOffset 映射为 TEXT，
    /// 无法翻译 ORDER BY / 比较谓词（GetStatusAsync 对 OccurredAt、LastSeenAt 排序）。
    /// 挂上转换器后排序按 long 翻译，查询可以真实执行（方案来自 sec-admin 已验证的做法）。
    /// </summary>
    private sealed class TicksModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            // 先照常执行 OnModelCreating（含全部实体配置），再复制 SqliteTestModelCustomizer 的
            // PG 归一化（btrim→trim、去默认值表达式、jsonb→TEXT），最后挂 ticks 转换器。
            base.Customize(modelBuilder, context);

            foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (IReadOnlyCheckConstraint checkConstraint in entityType.GetCheckConstraints().ToList())
                {
                    string? rewritten = checkConstraint.Sql?.Replace("btrim(", "trim(", StringComparison.Ordinal);
                    if (rewritten is null || rewritten == checkConstraint.Sql)
                    {
                        continue;
                    }

                    entityType.RemoveCheckConstraint(checkConstraint.Name!);
                    entityType.AddCheckConstraint(checkConstraint.Name!, rewritten);
                }

                foreach (IMutableProperty property in entityType.GetProperties())
                {
                    property.SetDefaultValueSql(null);

                    if (property.GetColumnType() == "jsonb")
                    {
                        property.SetColumnType("TEXT");
                    }

                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(
                            new ValueConverter<DateTimeOffset, long>(
                                value => value.UtcTicks,
                                value => new DateTimeOffset(value, TimeSpan.Zero)));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 带 ticks 转换器的独立内存库：与共享 SqliteTestDatabase 平行、互不影响
    ///（共享骨架不暴露连接，这里自建连接承载同一套生产表）。
    /// </summary>
    private sealed class TicksSqliteDatabase : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public TicksSqliteDatabase()
        {
            _connection.Open();
            using DataAcquisitionPlatformDbContext context = CreateContext();
            context.Database.EnsureCreated();
        }

        public DataAcquisitionPlatformDbContext CreateContext()
        {
            DbContextOptionsBuilder<DataAcquisitionPlatformDbContext> builder = new();
            builder
                .UseSqlite(_connection)
                .ReplaceService<IModelCustomizer, TicksModelCustomizer>();

            return new DataAcquisitionPlatformDbContext(builder.Options);
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task GetStatusAsync_Should_Report_Last_Run_And_Open_Issues()
    {
        using TicksSqliteDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", departmentCode: "UNKNOWN-DEPT")]),
            EnabledOptions(), new StubAdvisoryLock(true));

        await service.RunAsync(null, "schedule");
        DirectorySyncStatusDto status = await service.GetStatusAsync();

        Assert.True(status.Enabled);
        Assert.Equal(Now, status.LastSyncedAt);
        Assert.Equal("success", status.LastResult);
        Assert.NotNull(status.LastSummary);
        Assert.Contains("同步完成", status.LastSummary);
        Assert.Single(status.Issues);
        Assert.Equal(DirectorySyncIssue.DepartmentUnmatched, status.Issues[0].IssueType);
        Assert.Equal("UNKNOWN-DEPT", status.Issues[0].ExternalCode);
    }

    [Fact]
    public async Task GetStatusAsync_Without_Any_Run_Should_Report_Null_Last_Run()
    {
        using TicksSqliteDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory(), EnabledOptions(), new StubAdvisoryLock(true));

        DirectorySyncStatusDto status = await service.GetStatusAsync();

        Assert.True(status.Enabled);
        Assert.Null(status.LastSyncedAt);
        Assert.Null(status.LastResult);
        Assert.Null(status.LastSummary);
        Assert.Empty(status.Issues);
    }

    // ---------- 待处理项处理 ----------

    [Fact]
    public async Task ResolveIssueAsync_Should_Mark_Resolved_And_Write_Audit()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", departmentCode: "UNKNOWN-DEPT")]),
            EnabledOptions(), new StubAdvisoryLock(true));
        await service.RunAsync(null, "schedule");

        Guid issueId;
        await using (DataAcquisitionPlatformDbContext read = database.CreateContext())
        {
            issueId = (await read.DirectorySyncIssues.SingleAsync()).Id;
        }

        AuthOperationResultDto result = await service.ResolveIssueAsync(issueId, Actor());

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DirectorySyncIssue issue = await verify.DirectorySyncIssues.SingleAsync(item => item.Id == issueId);
        Assert.NotNull(issue.ResolvedAt);
        // 处理痕迹本身是运维证据：记录不删除。
        Assert.True(await verify.DirectorySyncIssues.AnyAsync(item => item.Id == issueId));
        Assert.True(await verify.AuditLogs.AnyAsync(log =>
            log.ActionCode == DirectorySyncService.ResolveIssueActionCode && log.Result == "success"));
    }

    [Fact]
    public async Task ResolveIssueAsync_Missing_Issue_Should_Fail()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory(), EnabledOptions(), new StubAdvisoryLock(true));

        AuthOperationResultDto result = await service.ResolveIssueAsync(Guid.NewGuid(), Actor());

        Assert.False(result.Succeeded);
        Assert.Equal("待处理项不存在或已处理。", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveIssueAsync_Already_Resolved_Should_Succeed_Idempotently()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        DirectorySyncService service = CreateService(
            db, new StubLdapDirectory([Record("ext-1", "zhang3", "张三", departmentCode: "UNKNOWN-DEPT")]),
            EnabledOptions(), new StubAdvisoryLock(true));
        await service.RunAsync(null, "schedule");

        Guid issueId;
        DateTimeOffset firstResolvedAt;
        await using (DataAcquisitionPlatformDbContext read = database.CreateContext())
        {
            DirectorySyncIssue issue = await read.DirectorySyncIssues.SingleAsync();
            issueId = issue.Id;
        }

        await service.ResolveIssueAsync(issueId, Actor());

        await using (DataAcquisitionPlatformDbContext read = database.CreateContext())
        {
            firstResolvedAt = (await read.DirectorySyncIssues.SingleAsync(item => item.Id == issueId)).ResolvedAt!
                .Value;
        }

        // 重复处理：幂等成功，不覆盖第一次的处理时间。
        AuthOperationResultDto second = await service.ResolveIssueAsync(issueId, Actor());
        Assert.True(second.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(firstResolvedAt,
            (await verify.DirectorySyncIssues.SingleAsync(item => item.Id == issueId)).ResolvedAt);
    }
}

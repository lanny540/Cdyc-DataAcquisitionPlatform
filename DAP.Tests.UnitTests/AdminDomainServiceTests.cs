using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// AdminDomainService 的单元测试。
///
/// 覆盖的核心规则：
/// - 域编码创建后不可改，删除受「无页面且非系统域」约束；
/// - 页面归属改动把 source 置为 override，启动对账不再覆盖；「恢复默认」还原为 code；
/// - 已下线（orphaned）页面只允许删除，不允许修改配置；
/// - 部门域授权全量覆盖，系统域与不存在的域一律拒绝而不是静默忽略。
/// </summary>
public sealed class AdminDomainServiceTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record SeedData(
        BusinessDomain EnergyDomain,
        BusinessDomain SystemDomain,
        DomainPage EnergyPage,
        DomainPage OrphanedPage,
        Department DeptA,
        Department DeptB);

    private static SeedData Seed(SqliteTestDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();

        BusinessDomain energy = new()
        {
            Id = Guid.NewGuid(),
            Code = AuthorizationCatalog.EnergyDomain,
            Name = "能源",
            SortOrder = 30,
            IsSystem = false,
            IsEnabled = true,
            VisibleWhenEmpty = false,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        BusinessDomain system = new()
        {
            Id = Guid.NewGuid(),
            Code = AuthorizationCatalog.PlatformDomain,
            Name = "平台管理",
            SortOrder = 900,
            IsSystem = true,
            IsEnabled = true,
            VisibleWhenEmpty = false,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        db.BusinessDomains.AddRange(energy, system);

        DomainPage energyPage = new()
        {
            Id = Guid.NewGuid(),
            PageKey = "energy-history",
            DomainId = energy.Id,
            Route = "/energy-history",
            Title = "能耗历史",
            AccessLevel = 1,
            SortOrder = 0,
            IsEnabled = true,
            Source = AuthorizationCatalog.PageSourceCode,
            IsOrphaned = false,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        DomainPage orphanedPage = new()
        {
            Id = Guid.NewGuid(),
            PageKey = "ghost-page",
            DomainId = energy.Id,
            Route = "/ghost",
            Title = "已下线页面",
            AccessLevel = 1,
            SortOrder = 1,
            IsEnabled = true,
            Source = AuthorizationCatalog.PageSourceCode,
            IsOrphaned = true,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        db.DomainPages.AddRange(energyPage, orphanedPage);

        Department deptA = new()
        {
            Id = Guid.NewGuid(),
            Code = "dept-a",
            Name = "一部",
            IsEnabled = true,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        Department deptB = new()
        {
            Id = Guid.NewGuid(),
            Code = "dept-b",
            Name = "二部",
            IsEnabled = true,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        db.Departments.AddRange(deptA, deptB);

        db.DepartmentDomainGrants.Add(new DepartmentDomainGrant
        {
            DepartmentId = deptA.Id, DomainId = energy.Id, GrantedAt = SeedTime
        });

        db.SaveChanges();
        return new SeedData(energy, system, energyPage, orphanedPage, deptA, deptB);
    }

    private static CurrentUser SuperAdminActor()
    {
        // AdminDomainService 内部不做权限判定（授权在端点策略），actor 只用于审计归因。
        ClaimsIdentity identity = new(
        [
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserName, "superadmin"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true")
        ], "TestAuthentication");
        return CurrentUser.FromPrincipal(new ClaimsPrincipal(identity), "127.0.0.1", "unit-test-agent")!;
    }

    private static AdminDomainService CreateService(DataAcquisitionPlatformDbContext db, CurrentUser? actor)
    {
        FixedTimeProvider time = new(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));
        return new AdminDomainService(
            db, new AuditLogWriter(db, time, NullLogger<AuditLogWriter>.Instance),
            new StubUserContext(actor), time);
    }

    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user);
        }
    }

    // ------------------------------------------------------------------
    // GetDomainsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDomainsAsync_Should_Return_Domains_With_Pages()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        IReadOnlyList<AdminDomainDto> domains = await service.GetDomainsAsync();

        Assert.Equal(2, domains.Count);
        AdminDomainDto energy = domains.Single(domain => domain.Code == AuthorizationCatalog.EnergyDomain);
        Assert.False(energy.IsSystem);
        Assert.Equal(2, energy.Pages.Count);
        // 含已下线页面，供超管核对配置。
        Assert.Contains(energy.Pages, page => page.IsOrphaned && page.PageKey == "ghost-page");
        Assert.Equal(AuthorizationCatalog.EnergyDomain, energy.Pages[0].DomainCode);
    }

    // ------------------------------------------------------------------
    // CreateDomainAsync
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("1abc")] // 数字开头
    [InlineData("a")] // 长度不足 2
    [InlineData("-abc")] // 连字符开头
    [InlineData("")] // 空编码
    public async Task CreateDomainAsync_Should_Reject_Invalid_Code(string code)
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDomainAsync(new AdminDomainUpsertRequest(
            code, "新域", 10, true, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal("域编码只能用小写字母、数字与连字符，以字母开头，2–32 位。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateDomainAsync_Should_Reject_Empty_Name()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDomainAsync(new AdminDomainUpsertRequest(
            "new-domain", "  ", 10, true, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal("域名称不能为空，且不超过 32 个字。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateDomainAsync_Should_Reject_Duplicate_Code_After_Normalization()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDomainAsync(new AdminDomainUpsertRequest(
            "  ENERGY  ", "能源二", 10, true, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal("域编码 energy 已存在，请换一个。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateDomainAsync_Should_Create_Domain_And_Audit()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDomainAsync(new AdminDomainUpsertRequest(
            "  Quality  ", "质检", 40, true, true, false));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        BusinessDomain created = await verify.BusinessDomains.SingleAsync(domain => domain.Code == "quality");
        Assert.Equal("质检", created.Name);
        Assert.True(created.IsEnabled);
        Assert.True(created.VisibleWhenEmpty);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain.create");
        Assert.Equal("success", audit.Result);
        Assert.Equal("质检", audit.TargetName);
    }

    // ------------------------------------------------------------------
    // UpdateDomainAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateDomainAsync_Should_Reject_Missing_Domain()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainAsync(
            Guid.NewGuid(), new AdminDomainUpsertRequest("ignored", "改名", 10, true, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal("域不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainAsync_Should_Reject_Empty_Name()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainAsync(
            seed.EnergyDomain.Id, new AdminDomainUpsertRequest("ignored", "", 30, true, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal("域名称不能为空，且不超过 32 个字。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainAsync_NoChange_Should_Not_Write_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainAsync(
            seed.EnergyDomain.Id, new AdminDomainUpsertRequest("ignored", "能源", 30, true, false, false));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateDomainAsync_Should_Update_Fields_And_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainAsync(
            seed.EnergyDomain.Id, new AdminDomainUpsertRequest("ignored", "能源管理", 35, false, true, false));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        BusinessDomain domain = await verify.BusinessDomains.SingleAsync(item => item.Id == seed.EnergyDomain.Id);
        Assert.Equal("能源管理", domain.Name);
        Assert.Equal(35, domain.SortOrder);
        Assert.False(domain.IsEnabled);
        Assert.True(domain.VisibleWhenEmpty);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain.update");
        Assert.Equal("success", audit.Result);
        Assert.Contains("before", audit.Detail);
        Assert.Contains("after", audit.Detail);
    }

    // ------------------------------------------------------------------
    // DeleteDomainAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteDomainAsync_Should_Reject_Missing_Domain()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("域不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteDomainAsync_Should_Reject_System_Domain()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainAsync(seed.SystemDomain.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("系统域是平台管理的固定入口，不允许删除。", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteDomainAsync_Should_Reject_Domain_With_Pages()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainAsync(seed.EnergyDomain.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("该域下还有 2 个页面，请先把页面迁到其他域或停用，再删除域。", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteDomainAsync_Should_Delete_Empty_Domain_And_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid emptyDomainId;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            BusinessDomain empty = new()
            {
                Id = Guid.NewGuid(),
                Code = "to-delete",
                Name = "待删域",
                SortOrder = 50,
                IsSystem = false,
                IsEnabled = true,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            };
            setup.BusinessDomains.Add(empty);
            await setup.SaveChangesAsync();
            emptyDomainId = empty.Id;
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainAsync(emptyDomainId);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.False(await verify.BusinessDomains.AnyAsync(domain => domain.Id == emptyDomainId));
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain.delete");
        Assert.Equal("success", audit.Result);
    }

    // ------------------------------------------------------------------
    // DeleteDomainPageAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteDomainPageAsync_Should_Reject_Missing_Page()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainPageAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("页面不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteDomainPageAsync_Should_Reject_Active_Page()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        // 代码里仍存在的页面删了会被对账重新插入，删除是无效操作。
        AuthOperationResultDto result = await service.DeleteDomainPageAsync(seed.EnergyPage.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("代码中仍存在该页面，不能删除；如需隐藏请改用停用。", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteDomainPageAsync_Should_Delete_Orphaned_Page()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.DeleteDomainPageAsync(seed.OrphanedPage.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.False(await verify.DomainPages.AnyAsync(page => page.Id == seed.OrphanedPage.Id));
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain_page.delete");
        Assert.Equal("success", audit.Result);
    }

    // ------------------------------------------------------------------
    // UpdateDomainPageAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Reject_Missing_Page()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            Guid.NewGuid(), new AdminDomainPageUpdateRequest(2, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("页面不存在，可能已被下线，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Reject_Orphaned_Page()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.OrphanedPage.Id, new AdminDomainPageUpdateRequest(2, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("该页面已在代码中下线，无法修改配置。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Reject_Invalid_Access_Level()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id, new AdminDomainPageUpdateRequest(3, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("页面等级只支持 1（查看）或 2（配置）。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Reject_Unknown_Domain()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id, new AdminDomainPageUpdateRequest(null, Guid.NewGuid(), null));

        Assert.False(result.Succeeded);
        Assert.Equal("目标域不存在。", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDomainPageAsync_Should_Validate_Title(string title)
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id, new AdminDomainPageUpdateRequest(null, null, null, title));

        Assert.False(result.Succeeded);
        Assert.Equal("页面名称不能为空，且不超过 64 个字。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Reject_Title_Longer_Than_64()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id, new AdminDomainPageUpdateRequest(null, null, null, new string('页', 65)));

        Assert.False(result.Succeeded);
        Assert.Equal("页面名称不能为空，且不超过 64 个字。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Empty_GroupTitle_Should_Clear_Group()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            DomainPage page = await setup.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
            page.GroupTitle = "原分组";
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        // 空字符串是「改为直接挂在域下」的显式指令，null 才表示不改。
        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id, new AdminDomainPageUpdateRequest(null, null, null, GroupTitle: "  "));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage updated = await verify.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
        Assert.Null(updated.GroupTitle);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_Should_Set_Source_Override_And_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id,
            new AdminDomainPageUpdateRequest(2, seed.SystemDomain.Id, false, "新页面名", "新分组", 7));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage page = await verify.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
        Assert.Equal(2, page.AccessLevel);
        Assert.Equal(seed.SystemDomain.Id, page.DomainId);
        Assert.False(page.IsEnabled);
        Assert.Equal("新页面名", page.Title);
        Assert.Equal("新分组", page.GroupTitle);
        Assert.Equal(7, page.SortOrder);
        // 改动后 source = override，启动对账不再覆盖管理员的调整。
        Assert.Equal(AuthorizationCatalog.PageSourceOverride, page.Source);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain_page.update");
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task UpdateDomainPageAsync_NoChange_Should_Keep_Source_Code()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        // 与当前值完全一致的修改请求：不写库、不审计、source 保持 code。
        AuthOperationResultDto result = await service.UpdateDomainPageAsync(
            seed.EnergyPage.Id,
            new AdminDomainPageUpdateRequest(1, seed.EnergyDomain.Id, true, "能耗历史", null, 0));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage page = await verify.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
        Assert.Equal(AuthorizationCatalog.PageSourceCode, page.Source);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ------------------------------------------------------------------
    // ResetDomainPageAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResetDomainPageAsync_Should_Reject_Missing_Page()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.ResetDomainPageAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("页面不存在，可能已被下线，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetDomainPageAsync_Should_Restore_Source_Code()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            DomainPage page = await setup.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
            page.Source = AuthorizationCatalog.PageSourceOverride;
            page.Title = "管理员改名";
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.ResetDomainPageAsync(seed.EnergyPage.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage resetPage = await verify.DomainPages.SingleAsync(item => item.Id == seed.EnergyPage.Id);
        // 恢复代码默认值：source 还原为 code，下一次启动对账即按代码刷新（改名暂不回滚）。
        Assert.Equal(AuthorizationCatalog.PageSourceCode, resetPage.Source);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "domain_page.reset");
        Assert.Equal("success", audit.Result);
    }

    // ------------------------------------------------------------------
    // GetDepartmentsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDepartmentsAsync_Should_Return_User_Counts_And_Grants()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            setup.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = "user-a1",
                DisplayName = "一部用户1",
                DepartmentId = seed.DeptA.Id,
                IsEnabled = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid(),
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            });
            setup.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = "user-a2",
                DisplayName = "一部用户2",
                DepartmentId = seed.DeptA.Id,
                IsEnabled = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid(),
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            });
            // 未归属部门的不计入任何部门。
            setup.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = "user-orphan",
                DisplayName = "无部门用户",
                DepartmentId = null,
                IsEnabled = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid(),
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            });
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        IReadOnlyList<AdminDepartmentDto> departments = await service.GetDepartmentsAsync();

        Assert.Equal(2, departments.Count);
        AdminDepartmentDto deptA = departments.Single(dept => dept.Code == "dept-a");
        Assert.Equal(2, deptA.UserCount);
        // 已授权域 = energy（系统域不进授权矩阵）。
        Assert.Equal([seed.EnergyDomain.Id], deptA.GrantedDomainIds);

        AdminDepartmentDto deptB = departments.Single(dept => dept.Code == "dept-b");
        Assert.Equal(0, deptB.UserCount);
        Assert.Empty(deptB.GrantedDomainIds);
    }

    // ------------------------------------------------------------------
    // CreateDepartmentAsync / UpdateDepartmentAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateDepartmentAsync_Should_Validate_Code_And_Name()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto noCode = await service.CreateDepartmentAsync(
            new AdminDepartmentUpsertRequest("", "新部门", null, true));
        Assert.False(noCode.Succeeded);
        Assert.Equal("请填写部门编码。", noCode.ErrorMessage);

        AuthOperationResultDto noName = await service.CreateDepartmentAsync(
            new AdminDepartmentUpsertRequest("dept-c", "  ", null, true));
        Assert.False(noName.Succeeded);
        Assert.Equal("请填写部门名称。", noName.ErrorMessage);
    }

    [Fact]
    public async Task CreateDepartmentAsync_Should_Reject_Duplicate_Code()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDepartmentAsync(
            new AdminDepartmentUpsertRequest(" dept-a ", "一部重复", null, true));

        Assert.False(result.Succeeded);
        Assert.Equal("部门编码 dept-a 已存在。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateDepartmentAsync_Should_Create_And_Audit()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.CreateDepartmentAsync(
            new AdminDepartmentUpsertRequest("dept-c", "三部", "  EXT-C  ", false));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Department created = await verify.Departments.SingleAsync(dept => dept.Code == "dept-c");
        Assert.Equal("三部", created.Name);
        Assert.Equal("EXT-C", created.ExternalCode); // 去空白后落库
        Assert.False(created.IsEnabled);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "department.create");
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_Should_Reject_Missing_Department()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentAsync(
            Guid.NewGuid(), new AdminDepartmentUpsertRequest("ignored", "改名", null, true));

        Assert.False(result.Succeeded);
        Assert.Equal("部门不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_Should_Update_Name_ExternalCode_And_Status()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentAsync(
            seed.DeptA.Id, new AdminDepartmentUpsertRequest("ignored", "一部（新）", "LDAP-1", false));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Department department = await verify.Departments.SingleAsync(dept => dept.Id == seed.DeptA.Id);
        Assert.Equal("一部（新）", department.Name);
        Assert.Equal("LDAP-1", department.ExternalCode);
        Assert.False(department.IsEnabled);
        // 部门编码不可改——它已被授权数据引用。
        Assert.Equal("dept-a", department.Code);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "department.update");
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_NoChange_Should_Not_Write_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentAsync(
            seed.DeptA.Id, new AdminDepartmentUpsertRequest("ignored", "一部", null, true));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ------------------------------------------------------------------
    // UpdateDepartmentGrantsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Should_Reject_Missing_Department()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            Guid.NewGuid(), new AdminDepartmentGrantsRequest([]));

        Assert.False(result.Succeeded);
        Assert.Equal("部门不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Should_Reject_System_Domain()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        // 系统域固定归超管，进入授权矩阵会把它下放给部门。
        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            seed.DeptA.Id, new AdminDepartmentGrantsRequest([seed.SystemDomain.Id]));

        Assert.False(result.Succeeded);
        Assert.Equal("授权目标包含系统域或不存在的域。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Should_Reject_Unknown_Domain()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            seed.DeptA.Id, new AdminDepartmentGrantsRequest([seed.EnergyDomain.Id, Guid.NewGuid()]));

        Assert.False(result.Succeeded);
        Assert.Equal("授权目标包含系统域或不存在的域。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Same_Set_Should_Be_NoOp()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            seed.DeptA.Id, new AdminDepartmentGrantsRequest([seed.EnergyDomain.Id]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Single(await verify.DepartmentDomainGrants.ToListAsync());
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Should_Replace_Grants_And_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid newDomainId;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            BusinessDomain extra = new()
            {
                Id = Guid.NewGuid(),
                Code = "banknote-paper",
                Name = "钞纸",
                SortOrder = 10,
                IsSystem = false,
                IsEnabled = true,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            };
            setup.BusinessDomains.Add(extra);
            await setup.SaveChangesAsync();
            newDomainId = extra.Id;
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        // 全量设置：列表外的 energy 授权被移除，列表内的钞纸被授予。
        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            seed.DeptA.Id, new AdminDepartmentGrantsRequest([newDomainId, newDomainId]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        List<DepartmentDomainGrant> grants = await verify.DepartmentDomainGrants
            .Where(grant => grant.DepartmentId == seed.DeptA.Id)
            .ToListAsync();
        Assert.Equal([newDomainId], grants.Select(grant => grant.DomainId).ToList());
        Assert.Equal("superadmin", await verify.AuditLogs
            .Where(log => log.ActionCode == "department.grants.update")
            .Select(log => log.UserName)
            .SingleAsync());
    }

    [Fact]
    public async Task UpdateDepartmentGrantsAsync_Empty_List_Should_Remove_All()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminDomainService service = CreateService(db, SuperAdminActor());

        AuthOperationResultDto result = await service.UpdateDepartmentGrantsAsync(
            seed.DeptA.Id, new AdminDepartmentGrantsRequest([]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.DepartmentDomainGrants.ToListAsync());
    }
}

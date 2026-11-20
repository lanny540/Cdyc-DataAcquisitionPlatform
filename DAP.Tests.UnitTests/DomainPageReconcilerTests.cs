using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Client.Layout;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// DomainPageReconciler 的单元测试（启动对账，docs/AUTH_DESIGN.md 第 4 节）。
///
/// 覆盖的核心规则：
/// - 代码声明的页面在启动时全部入库（新页面不报错地消失是最常见的线上故障）；
/// - 管理员改过（source = override）的页面不覆盖显示配置，但路由必须跟代码走；
/// - 代码里已不存在的页面标记下线而不是删除，已下线的不重复计数；
/// - 所属域不存在的页面跳过并计数；
/// - 域内排序按目录顺序从 0 递增。
/// </summary>
public sealed class DomainPageReconcilerTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ReconcileTime = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly FixedTimeProvider Time = new(ReconcileTime);

    /// <summary>按授权目录创建内置域（可排除部分域以测试「域缺失跳过」）。</summary>
    private static Dictionary<string, Guid> SeedDomains(SqliteTestDatabase database, params string[] excludeCodes)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Dictionary<string, Guid> domainIds = [];

        foreach (BuiltInDomain builtIn in AuthorizationCatalog.Domains)
        {
            if (excludeCodes.Contains(builtIn.Code))
            {
                continue;
            }

            BusinessDomain domain = new()
            {
                Id = Guid.NewGuid(),
                Code = builtIn.Code,
                Name = builtIn.Name,
                SortOrder = builtIn.SortOrder,
                IsSystem = builtIn.IsSystem,
                IsEnabled = true,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            };
            domainIds[builtIn.Code] = domain.Id;
            db.BusinessDomains.Add(domain);
        }

        db.SaveChanges();
        return domainIds;
    }

    private static Task<DomainPageReconcileResult> ReconcileAsync(SqliteTestDatabase database)
    {
        return DomainPageReconciler.ReconcileAsync(
            database.CreateContext(), Time, NullLogger.Instance);
    }

    [Fact]
    public async Task Reconcile_Fresh_Database_Should_Create_All_Pages()
    {
        using SqliteTestDatabase database = new();
        Dictionary<string, Guid> domainIds = SeedDomains(database);

        DomainPageReconcileResult result = await ReconcileAsync(database);

        Assert.Equal(NavigationCatalog.Pages.Count, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Kept);
        Assert.Equal(0, result.Orphaned);
        Assert.Equal(0, result.Skipped);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        foreach (NavPageDescriptor descriptor in NavigationCatalog.Pages)
        {
            DomainPage page = await verify.DomainPages.SingleAsync(item => item.PageKey == descriptor.Item.PageKey);
            Assert.Equal(descriptor.Item.Href, page.Route);
            Assert.Equal(domainIds[descriptor.Item.DomainCode], page.DomainId);
            Assert.Equal(descriptor.Item.Title, page.Title);
            Assert.Equal(descriptor.Item.AccessLevel, page.AccessLevel);
            Assert.True(page.IsEnabled);
            Assert.False(page.IsOrphaned);
            Assert.Equal(AuthorizationCatalog.PageSourceCode, page.Source);
        }
    }

    [Fact]
    public async Task Reconcile_Second_Run_Should_Update_Existing_Pages()
    {
        using SqliteTestDatabase database = new();
        SeedDomains(database);
        await ReconcileAsync(database);

        // 第二次启动：页面都在，按代码默认值刷新一遍，不重复插入。
        DomainPageReconcileResult result = await ReconcileAsync(database);

        Assert.Equal(0, result.Created);
        Assert.Equal(NavigationCatalog.Pages.Count, result.Updated);
        Assert.Equal(0, result.Orphaned);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(NavigationCatalog.Pages.Count, await verify.DomainPages.CountAsync());
    }

    [Fact]
    public async Task Reconcile_Should_Preserve_Override_Except_Route()
    {
        using SqliteTestDatabase database = new();
        SeedDomains(database);
        await ReconcileAsync(database);
        NavPageDescriptor descriptor = NavigationCatalog.Pages[0];

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            DomainPage page = await setup.DomainPages.SingleAsync(item => item.PageKey == descriptor.Item.PageKey);
            page.Source = AuthorizationCatalog.PageSourceOverride;
            page.Title = "管理员改名";
            page.Route = "/old-route"; // 模拟开发者在代码里改了路由
            page.AccessLevel = descriptor.Item.AccessLevel == 1 ? 2 : 1;
            page.IsOrphaned = true; // 模拟页面曾被标记下线后又在代码里回来
            await setup.SaveChangesAsync();
        }

        DomainPageReconcileResult result = await ReconcileAsync(database);

        Assert.Equal(1, result.Kept);
        Assert.Equal(NavigationCatalog.Pages.Count - 1, result.Updated);
        Assert.Equal(0, result.Orphaned);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage updated = await verify.DomainPages.SingleAsync(item => item.PageKey == descriptor.Item.PageKey);

        // 显示名与等级保留管理员的调整；路由必须跟代码走，否则菜单会指向旧地址。
        Assert.Equal("管理员改名", updated.Title);
        Assert.Equal(2, updated.AccessLevel);
        Assert.Equal(descriptor.Item.Href, updated.Route);
        // 代码里页面回来了就取消下线标记。
        Assert.False(updated.IsOrphaned);
        Assert.Equal(AuthorizationCatalog.PageSourceOverride, updated.Source);
    }

    [Fact]
    public async Task Reconcile_Should_Mark_Missing_Pages_Orphaned_Once()
    {
        using SqliteTestDatabase database = new();
        Dictionary<string, Guid> domainIds = SeedDomains(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            // 库里有、代码里没有的页面：标记下线而不是删除。
            setup.DomainPages.Add(new DomainPage
            {
                Id = Guid.NewGuid(),
                PageKey = "ghost-page",
                DomainId = domainIds[AuthorizationCatalog.EnergyDomain],
                Route = "/ghost",
                Title = "幽灵页面",
                AccessLevel = 1,
                SortOrder = 99,
                IsEnabled = true,
                IsOrphaned = false,
                Source = AuthorizationCatalog.PageSourceCode,
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            });
            await setup.SaveChangesAsync();
        }

        DomainPageReconcileResult first = await ReconcileAsync(database);

        Assert.Equal(1, first.Orphaned);

        await using (DataAcquisitionPlatformDbContext verify = database.CreateContext())
        {
            DomainPage ghost = await verify.DomainPages.SingleAsync(item => item.PageKey == "ghost-page");
            Assert.True(ghost.IsOrphaned);
            Assert.Equal(ReconcileTime, ghost.UpdatedAt);
        }

        // 第二次对账：已下线的页面不重复计数、不重复刷新。
        DomainPageReconcileResult second = await ReconcileAsync(database);
        Assert.Equal(0, second.Orphaned);
    }

    [Fact]
    public async Task Reconcile_Should_Skip_Pages_Whose_Domain_Is_Missing()
    {
        using SqliteTestDatabase database = new();
        // 只建 3 个域：钞纸域缺失，其页面必须跳过而不是挂掉。
        SeedDomains(database, AuthorizationCatalog.BanknotePaperDomain);

        int skippedCount =
            NavigationCatalog.Pages.Count(page => page.Item.DomainCode == AuthorizationCatalog.BanknotePaperDomain);

        DomainPageReconcileResult result = await ReconcileAsync(database);

        Assert.Equal(skippedCount, result.Skipped);
        Assert.Equal(NavigationCatalog.Pages.Count - skippedCount, result.Created);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(
            NavigationCatalog.Pages.Count - skippedCount,
            await verify.DomainPages.CountAsync());
    }

    [Fact]
    public async Task Reconcile_Should_Assign_Sort_Order_Per_Domain()
    {
        using SqliteTestDatabase database = new();
        SeedDomains(database);
        await ReconcileAsync(database);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();

        foreach (string domainCode in AuthorizationCatalog.Domains.Select(domain => domain.Code))
        {
            List<string> catalogOrder = NavigationCatalog.Pages
                .Where(page => page.Item.DomainCode == domainCode)
                .Select(page => page.Item.PageKey)
                .ToList();

            if (catalogOrder.Count == 0)
            {
                continue;
            }

            List<DomainPage> pages = await verify.DomainPages
                .Where(page => page.PageKey != "ghost-page")
                .ToListAsync();

            List<string> storedOrder = pages
                .Where(page => catalogOrder.Contains(page.PageKey))
                .OrderBy(page => page.SortOrder)
                .Select(page => page.PageKey)
                .ToList();

            // 域内排序 = 代码目录顺序，从 0 连续递增。
            Assert.Equal(catalogOrder, storedOrder);
            Assert.Equal(
                Enumerable.Range(0, catalogOrder.Count),
                pages.Where(page => catalogOrder.Contains(page.PageKey))
                    .Select(page => page.SortOrder)
                    .OrderBy(order => order));
        }
    }

    [Fact]
    public async Task Reconcile_Should_Preserve_Disabled_State_For_Code_Source_Pages()
    {
        using SqliteTestDatabase database = new();
        SeedDomains(database);
        await ReconcileAsync(database);
        NavPageDescriptor descriptor = NavigationCatalog.Pages[0];

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            DomainPage page = await setup.DomainPages.SingleAsync(item => item.PageKey == descriptor.Item.PageKey);
            page.IsEnabled = false;
            await setup.SaveChangesAsync();
        }

        await ReconcileAsync(database);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        DomainPage refreshed = await verify.DomainPages.SingleAsync(item => item.PageKey == descriptor.Item.PageKey);

        // code 来源的行：名称 / 分组 / 排序 / 等级按代码刷新，但启停状态保留——
        // 停用是管理员的主动操作，对账不会自动把页面启用回来。
        Assert.False(refreshed.IsEnabled);
    }
}

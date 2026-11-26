using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Extensions.Caching.Memory;

namespace DAP.Tests.UnitTests;

/// <summary>
/// PageAccessResolver 的单元测试。
///
/// 覆盖的核心规则：
/// - 路径归一化：去查询串 / 锚点、去首尾斜杠、大小写不敏感；
/// - 最长前缀匹配：/managed-data-definitions/{id} 不被更短的路由抢走；
/// - 根路由（空串）只匹配根路径；
/// - 只统计启用且未下线、且所属域启用的页面；
/// - 访问映射按 domain-pages-access-map 键缓存 60 秒。
/// </summary>
public sealed class PageAccessResolverTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record SeedData(BusinessDomain EnergyDomain);

    private static SeedData Seed(SqliteTestDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();

        BusinessDomain energy = new()
        {
            Id = Guid.NewGuid(),
            Code = "energy",
            Name = "能源",
            SortOrder = 30,
            IsSystem = false,
            IsEnabled = true,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        BusinessDomain disabled = new()
        {
            Id = Guid.NewGuid(),
            Code = "disabled-domain",
            Name = "停用域",
            SortOrder = 40,
            IsSystem = false,
            IsEnabled = false,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        db.BusinessDomains.AddRange(energy, disabled);

        db.DomainPages.AddRange(
            Page(energy.Id, "root", "/", 1),
            // /energy 是配置级、/energy/history 是查看级：用等级区分最长前缀是否生效。
            Page(energy.Id, "energy", "/energy", 2),
            Page(energy.Id, "energy-history", "/energy/history", 1),
            // 已停用页面不参与判定。
            Page(energy.Id, "admin-config", "/admin/config", 2, false),
            // 已下线页面不参与判定。
            Page(energy.Id, "orphan-page", "/orphan", 1, isOrphaned: true),
            // 所属域停用的页面不参与判定。
            Page(disabled.Id, "disabled-domain-page", "/disabled-domain-page", 1));

        db.SaveChanges();
        return new SeedData(energy);
    }

    private static DomainPage Page(
        Guid domainId, string pageKey, string route, int accessLevel,
        bool isEnabled = true, bool isOrphaned = false)
    {
        return new DomainPage
        {
            Id = Guid.NewGuid(),
            PageKey = pageKey,
            DomainId = domainId,
            Route = route,
            Title = pageKey,
            AccessLevel = accessLevel,
            SortOrder = 0,
            IsEnabled = isEnabled,
            IsOrphaned = isOrphaned,
            Source = "code",
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
    }

    private static PageAccessResolver CreateResolver(DataAcquisitionPlatformDbContext db, IMemoryCache cache)
    {
        return new PageAccessResolver(db, cache);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("///")]
    public async Task ResolveAsync_Root_Path_Should_Match_Root_Page(string path)
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        PageAccessRequirement? requirement = await resolver.ResolveAsync(path);

        Assert.NotNull(requirement);
        Assert.Equal("energy", requirement.DomainCode);
        Assert.Equal(1, requirement.AccessLevel);
    }

    [Fact]
    public async Task ResolveAsync_Exact_Match_Should_Return_Requirement()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        PageAccessRequirement? requirement = await resolver.ResolveAsync("/energy");

        Assert.NotNull(requirement);
        Assert.Equal("energy", requirement.DomainCode);
        Assert.Equal(2, requirement.AccessLevel);
    }

    [Fact]
    public async Task ResolveAsync_Should_Use_Longest_Prefix_Match()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        // /energy/history/2026 必须命中 /energy/history（等级 1）而不是更短的 /energy（等级 2）。
        PageAccessRequirement? requirement = await resolver.ResolveAsync("/energy/history/2026");

        Assert.NotNull(requirement);
        Assert.Equal(1, requirement.AccessLevel);
    }

    [Theory]
    [InlineData("/energy?range=2026")]
    [InlineData("/energy#top")]
    [InlineData("/ENERGY")]
    [InlineData(" /energy/ ")]
    [InlineData("/energy/sub/page")]
    public async Task ResolveAsync_Should_Normalize_Path(string path)
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        PageAccessRequirement? requirement = await resolver.ResolveAsync(path);

        Assert.NotNull(requirement);
        Assert.Equal("energy", requirement.DomainCode);
    }

    [Fact]
    public async Task ResolveAsync_Unregistered_Path_Should_Return_Null()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync("/unknown-page"));
        Assert.Null(await resolver.ResolveAsync("/energ")); // 前缀相同但不是完整段，不命中
    }

    [Fact]
    public async Task ResolveAsync_Should_Exclude_Disabled_Orphaned_And_Disabled_Domain_Pages()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PageAccessResolver resolver = CreateResolver(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveAsync("/admin/config"));
        Assert.Null(await resolver.ResolveAsync("/orphan"));
        Assert.Null(await resolver.ResolveAsync("/disabled-domain-page"));
    }

    [Fact]
    public async Task ResolveAsync_Should_Use_Cached_Map_Within_Window()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        MemoryCache cache = new(new MemoryCacheOptions());
        PageAccessResolver resolver = CreateResolver(db, cache);

        // 首次解析把映射写入缓存（键 domain-pages-access-map）。
        Assert.NotNull(await resolver.ResolveAsync("/energy"));
        Assert.Null(await resolver.ResolveAsync("/new-page"));

        // 新登记的页面在缓存窗口内不可见；同缓存键的新实例同样读到旧映射。
        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            setup.DomainPages.Add(Page(seed.EnergyDomain.Id, "new-page", "/new-page", 1));
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        PageAccessResolver cachedResolver = CreateResolver(db2, cache);
        Assert.Null(await cachedResolver.ResolveAsync("/new-page"));

        // 全新缓存则立即看到新页面。
        PageAccessResolver freshResolver = CreateResolver(db2, new MemoryCache(new MemoryCacheOptions()));
        PageAccessRequirement? requirement = await freshResolver.ResolveAsync("/new-page");
        Assert.NotNull(requirement);
        Assert.Equal("energy", requirement.DomainCode);
    }
}

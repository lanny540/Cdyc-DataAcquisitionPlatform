using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DAP.Tests.UnitTests;

/// <summary>
/// UserSessionValidator 的单元测试（docs/AUTH_DESIGN.md 9.5）。
///
/// 覆盖的核心规则：
/// - 没有安全戳的会话一律不认（失败方向必须是关闭）；
/// - 用户存在、启用、安全戳一致才有效，比对忽略大小写；
/// - 快照带 30 秒缓存：窗口内以缓存为准；
/// - RenewSecurityStamp 换值并主动清缓存，使停用/重置密码立即生效。
/// </summary>
public sealed class UserSessionValidatorTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record SeedData(AppUser EnabledUser, AppUser DisabledUser, Guid EnabledStamp);

    private static SeedData Seed(SqliteTestDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();

        Guid enabledStamp = Guid.NewGuid();
        AppUser enabledUser = new()
        {
            Id = Guid.NewGuid(),
            UserName = "user-a",
            DisplayName = "用户A",
            DepartmentId = null,
            IsEnabled = true,
            MustChangePassword = false,
            SecurityStamp = enabledStamp,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
        AppUser disabledUser = new()
        {
            Id = Guid.NewGuid(),
            UserName = "user-b",
            DisplayName = "用户B",
            DepartmentId = null,
            IsEnabled = false,
            MustChangePassword = false,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };

        db.AppUsers.AddRange(enabledUser, disabledUser);
        db.SaveChanges();
        return new SeedData(enabledUser, disabledUser, enabledStamp);
    }

    private static UserSessionValidator CreateValidator(DataAcquisitionPlatformDbContext db, IMemoryCache cache)
    {
        return new UserSessionValidator(db, cache);
    }

    [Fact]
    public async Task IsSessionValidAsync_Empty_Stamp_Should_Return_False()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        // 无戳覆盖升级前老 Cookie 与被裁剪 claims 的伪造尝试。
        Assert.False(await validator.IsSessionValidAsync(seed.EnabledUser.Id, null));
        Assert.False(await validator.IsSessionValidAsync(seed.EnabledUser.Id, ""));
        Assert.False(await validator.IsSessionValidAsync(seed.EnabledUser.Id, "   "));
    }

    [Fact]
    public async Task IsSessionValidAsync_Matching_Stamp_Should_Return_True()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.True(await validator.IsSessionValidAsync(seed.EnabledUser.Id, seed.EnabledStamp.ToString()));
    }

    [Fact]
    public async Task IsSessionValidAsync_Stamp_Comparison_Should_Ignore_Case()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        // Cookie 里的大小写不做要求，比对按不区分大小写处理。
        Assert.True(await validator.IsSessionValidAsync(
            seed.EnabledUser.Id, seed.EnabledStamp.ToString().ToUpperInvariant()));
    }

    [Fact]
    public async Task IsSessionValidAsync_Wrong_Stamp_Should_Return_False()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.False(await validator.IsSessionValidAsync(seed.EnabledUser.Id, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task IsSessionValidAsync_Disabled_User_Should_Return_False()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.False(await validator.IsSessionValidAsync(
            seed.DisabledUser.Id, seed.DisabledUser.SecurityStamp.ToString()));
    }

    [Fact]
    public async Task IsSessionValidAsync_Missing_User_Should_Return_False()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.False(await validator.IsSessionValidAsync(Guid.NewGuid(), Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task IsSessionValidAsync_Should_Use_Cache_Within_Window()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        // 第一次校验把快照写入缓存。
        Assert.True(await validator.IsSessionValidAsync(seed.EnabledUser.Id, seed.EnabledStamp.ToString()));

        // 另一上下文停用该用户：缓存窗口内仍以旧快照为准（30 秒折中，属既定设计）。
        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser user = await setup.AppUsers.SingleAsync(item => item.Id == seed.EnabledUser.Id);
            user.IsEnabled = false;
            await setup.SaveChangesAsync();
        }

        Assert.True(await validator.IsSessionValidAsync(seed.EnabledUser.Id, seed.EnabledStamp.ToString()));
    }

    [Fact]
    public async Task RenewSecurityStamp_Should_Invalidate_Cached_Session_Immediately()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Assert.True(await validator.IsSessionValidAsync(seed.EnabledUser.Id, seed.EnabledStamp.ToString()));

        // 停用流程：加载实体 → 换戳（清缓存）→ 保存。
        AppUser user = await db.AppUsers.SingleAsync(item => item.Id == seed.EnabledUser.Id);
        validator.RenewSecurityStamp(user);
        user.IsEnabled = false;
        await db.SaveChangesAsync();

        Assert.False(await validator.IsSessionValidAsync(seed.EnabledUser.Id, seed.EnabledStamp.ToString()));
    }

    [Fact]
    public async Task RenewSecurityStamp_Should_Change_Stamp_Value()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));
        AppUser user = await db.AppUsers.SingleAsync(item => item.Id == seed.EnabledUser.Id);
        Guid before = user.SecurityStamp;

        validator.RenewSecurityStamp(user);

        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public async Task IsSessionValidAsync_Should_Cache_Missing_User()
    {
        using SqliteTestDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        UserSessionValidator validator = CreateValidator(db, new MemoryCache(new MemoryCacheOptions()));

        Guid missingUserId = Guid.NewGuid();

        // 「查无此人」也会被缓存，否则每个请求都回查一次。
        Assert.False(await validator.IsSessionValidAsync(missingUserId, Guid.NewGuid().ToString()));

        // 用户随后被创建（同 id），缓存窗口内仍按缺失处理。
        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            setup.AppUsers.Add(new AppUser
            {
                Id = missingUserId,
                UserName = "late-user",
                DisplayName = "后创建用户",
                DepartmentId = null,
                IsEnabled = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid(),
                CreatedAt = SeedTime,
                UpdatedAt = SeedTime
            });
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        UserSessionValidator freshValidator = CreateValidator(verify, new MemoryCache(new MemoryCacheOptions()));
        AppUser created = await verify.AppUsers.SingleAsync(item => item.Id == missingUserId);

        Assert.False(await validator.IsSessionValidAsync(missingUserId, created.SecurityStamp.ToString()));
        Assert.True(await freshValidator.IsSessionValidAsync(missingUserId, created.SecurityStamp.ToString()));
    }
}

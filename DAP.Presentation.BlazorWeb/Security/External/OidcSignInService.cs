using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security.External;

/// <summary>
/// OIDC 登录时的建号与字段刷新（docs/AUTH_DESIGN.md 11.2「首次登录 JIT 建号」）。
///
/// 唯一键是 <c>sub</c> 而不是邮箱或用户名：邮箱会变，用户名可能被回收再分配给另一个人，
/// 用它们做键迟早会把两个真实用户串成一个账号。
/// </summary>
public sealed class OidcSignInService(
    DataAcquisitionPlatformDbContext db,
    DepartmentCodeResolver departmentCodes,
    TimeProvider timeProvider,
    ILogger<OidcSignInService> logger)
{
    /// <summary>
    /// 按提供方与档案取得本地账号：已绑定则刷新可同步字段，未绑定则建号。
    /// **角色只在建号时给默认值**，之后由管理员手工维护。
    /// </summary>
    /// <param name="provider">提供方配置。</param>
    /// <param name="profile">从令牌解析出的用户档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>建号结果。</returns>
    public async Task<OidcProvisionResult> ProvisionAsync(
        OidcProviderOptions provider,
        OidcUserProfile profile,
        CancellationToken cancellationToken = default)
    {
        var providerKey = ProviderKey(provider);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Guid? userId = await db.ExternalIdentities
            .Where(identity => identity.Provider == providerKey && identity.ExternalId == profile.ExternalId)
            .Select(identity => (Guid?)identity.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? departmentId = await departmentCodes.ResolveAsync(profile.DepartmentCode, cancellationToken);

        if (userId is { } existingUserId)
        {
            AppUser? existing = await db.AppUsers
                .FirstOrDefaultAsync(user => user.Id == existingUserId, cancellationToken);

            if (existing is null)
            {
                return new OidcProvisionResult(false, null, "账号已不存在，请联系管理员。");
            }

            if (!existing.IsEnabled)
            {
                return new OidcProvisionResult(false, null, "该账号已停用，请联系管理员。");
            }

            // 姓名与邮箱跟随 IdP；部门只在映射到时更新，映射不到保留原归属。
            if (!string.IsNullOrWhiteSpace(profile.DisplayName)
                && !string.Equals(profile.DisplayName, existing.DisplayName, StringComparison.Ordinal))
            {
                existing.DisplayName = profile.DisplayName;
                existing.UpdatedAt = now;
            }

            if (!string.IsNullOrWhiteSpace(profile.Email)
                && !string.Equals(profile.Email, existing.Email, StringComparison.OrdinalIgnoreCase))
            {
                existing.Email = profile.Email;
                existing.UpdatedAt = now;
            }

            if (departmentId is { } mapped && mapped != existing.DepartmentId)
            {
                existing.DepartmentId = mapped;
                existing.UpdatedAt = now;
            }

            await TouchIdentityAsync(providerKey, profile.ExternalId, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return new OidcProvisionResult(true, existing.Id, null);
        }

        var userName = await EnsureUniqueUserNameAsync(profile.UserName, provider.Code, cancellationToken);

        AppUser created = new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? userName : profile.DisplayName,
            DepartmentId = departmentId,
            Email = profile.Email,
            IsEnabled = true,
            MustChangePassword = false,
            PasswordHash = null,
            AccessFailedCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AppUsers.Add(created);

        db.ExternalIdentities.Add(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = created.Id,
            Provider = providerKey,
            ExternalId = profile.ExternalId,
            LastSyncedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        Role? defaultRole = await db.Roles
            .FirstOrDefaultAsync(role => role.Code == provider.DefaultRoleCode, cancellationToken);

        if (defaultRole is {IsEnabled: true})
        {
            db.UserRoles.Add(new UserRole {UserId = created.Id, RoleId = defaultRole.Id});
        }
        else
        {
            logger.LogWarning(
                "OIDC 账号 {UserName} 建号时默认角色 {RoleCode} 不可用，该账号将看不到任何页面。",
                userName,
                provider.DefaultRoleCode);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // 并发首登：两个标签页、或用户双击登录按钮，两段流程会同时为同一个 sub 建号，
            // 后到的一次撞唯一索引。这不是错误——对方已经建好了，取回既有账号即可，
            // 否则用户看到的是「统一登录失败」，重试一次又正常。
            foreach (var entry in db.ChangeTracker.Entries()
                         .Where(item => item.State == EntityState.Added)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            Guid? concurrentUserId = await db.ExternalIdentities
                .AsNoTracking()
                .Where(identity => identity.Provider == providerKey && identity.ExternalId == profile.ExternalId)
                .Select(identity => (Guid?)identity.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (concurrentUserId is { } id)
            {
                logger.LogInformation(
                    "OIDC 账号 {UserName} 在并发建号中被另一路径抢先创建，已改为复用既有账号。",
                    userName);

                return new OidcProvisionResult(true, id, null);
            }

            logger.LogError(exception, "OIDC 账号 {UserName} 建号失败（数据库拒绝写入）。", userName);
            throw;
        }

        logger.LogInformation(
            "OIDC 账号 {UserName} 首次登录（提供方 {Provider}），已建号，部门映射 {Department}。",
            userName,
            provider.Code,
            departmentId is null ? "未匹配" : departmentId.ToString());

        return new OidcProvisionResult(true, created.Id, null);
    }

    /// <summary>
    /// 构造身份提供方键。多 IdP 下必须带 Code：两家 IdP 各自生成的 <c>sub</c>
    /// 在理论上是同域随机串，只按 <c>oidc</c> 记录会让两个人的身份撞在一起。
    /// </summary>
    /// <param name="provider">提供方配置。</param>
    /// <returns>提供方键。</returns>
    public static string ProviderKey(OidcProviderOptions provider)
    {
        return $"oidc:{provider.Code}";
    }

    private async Task TouchIdentityAsync(
        string providerKey,
        string externalId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ExternalIdentity? identity = await db.ExternalIdentities
            .FirstOrDefaultAsync(item => item.Provider == providerKey && item.ExternalId == externalId,
                cancellationToken);

        if (identity is null)
        {
            return;
        }

        identity.LastSyncedAt = now;
        identity.UpdatedAt = now;
    }

    private async Task<string> EnsureUniqueUserNameAsync(
        string baseName,
        string providerCode,
        CancellationToken cancellationToken)
    {
        var stem = string.IsNullOrWhiteSpace(baseName) ? $"oidc-{providerCode}" : baseName.Trim();
        var candidate = Truncate(stem, 100);

        // 与本地账号同名时不并号：并号等于把一个外部身份挂到别人的账号上。
        // 首选用「账号-提供方」后缀，可预期；仍冲突时再加序号。
        var suffix = 1;

        while (await db.AppUsers.AnyAsync(user => user.UserName == candidate, cancellationToken))
        {
            suffix++;
            var tail = suffix == 2 ? $"-{providerCode}" : $"-{providerCode}{suffix}";
            candidate = Truncate(stem, 100 - tail.Length) + tail;
        }

        return candidate;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

/// <summary>
/// OIDC 建号结果。
/// </summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="UserId">本地账号标识；失败时为 null。</param>
/// <param name="ErrorMessage">失败原因，中文，可直接展示。</param>
public sealed record OidcProvisionResult(bool Succeeded, Guid? UserId, string? ErrorMessage);

using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 域账号在平台侧的建号与字段刷新（docs/AUTH_DESIGN.md 11.1「登录时再做一次实时校验」）。
///
/// 与定时同步共用 <see cref="DirectorySyncPlanner"/>：两条路径若各写一套字段映射，
/// 迟早会出现「同步改的姓名被登录改回去了」这类只在现场才看得见的问题。
/// </summary>
public sealed class DirectoryAccountService(
    DataAcquisitionPlatformDbContext db,
    DepartmentCodeResolver departmentCodes,
    IOptions<PlatformAuthOptions> options,
    TimeProvider timeProvider,
    ILogger<DirectoryAccountService> logger)
{
    private readonly PlatformAuthOptions _options = options.Value;

    /// <summary>
    /// 按目录记录取得本地账号：已存在则刷新可同步字段，不存在则建号。
    /// **角色只在建号时给默认值**，之后不再由目录决定。
    /// </summary>
    /// <param name="record">目录记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地账号；账号名被本地账号占用而无法建号时返回 null。</returns>
    public async Task<DirectoryProvisionResult> EnsureUserAsync(
        LdapDirectoryUser record,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        Guid? userId = await db.ExternalIdentities
            .Where(identity => identity.Provider == DirectorySyncService.LdapProvider
                               && identity.ExternalId == record.ExternalId)
            .Select(identity => (Guid?)identity.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        AppUser? user = userId is null
            ? null
            : await db.AppUsers
                .Include(item => item.ExternalIdentities)
                .Include(item => item.UserRoles)
                .ThenInclude(link => link.Role)
                .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        Guid? departmentId = await departmentCodes.ResolveAsync(record.DepartmentExternalCode, cancellationToken);

        if (user is null)
        {
            return await CreateAsync(record, departmentId, now, cancellationToken);
        }

        var decision = DirectorySyncPlanner.Decide(record, Snapshot(user), departmentId);

        if (decision.RequiresWrite)
        {
            user.DisplayName = decision.DisplayName;
            user.Email = decision.Email;
            user.DepartmentId = decision.DepartmentId;
            user.UpdatedAt = now;
        }

        ExternalIdentity? identity = user.ExternalIdentities.FirstOrDefault(item =>
            item.Provider == DirectorySyncService.LdapProvider
            && string.Equals(item.ExternalId, record.ExternalId, StringComparison.Ordinal));

        if (identity is not null)
        {
            identity.EmployeeNo = record.EmployeeNo ?? identity.EmployeeNo;
            identity.LastSyncedAt = now;
            identity.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DirectoryProvisionResult(true, user, null);
    }

    /// <summary>
    /// 按外部编码匹配部门。匹配不到时返回 null——账号建出来但看不到任何业务域，
    /// 这是刻意为之：宁可让人找管理员，也不要把人塞进一个猜出来的部门。
    /// </summary>
    /// <param name="externalCode">外部编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>部门标识。</returns>
    public Task<Guid?> ResolveDepartmentIdAsync(
        string? externalCode,
        CancellationToken cancellationToken = default)
    {
        return departmentCodes.ResolveAsync(externalCode, cancellationToken);
    }

    private async Task<DirectoryProvisionResult> CreateAsync(
        LdapDirectoryUser record,
        Guid? departmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var userName = DirectoryUserFactory.DeriveUserName(record);

        var occupied = await db.AppUsers.AnyAsync(item => item.UserName == userName, cancellationToken);

        if (occupied)
        {
            logger.LogWarning(
                "域账号 {UserName} 与本地账号同名，未自动建号：并号可能把域身份挂到本地管理员身上。",
                userName);

            return new DirectoryProvisionResult(
                false,
                null,
                $"账号 {userName} 已被平台上的本地账号占用，请联系管理员处理后再登录。");
        }

        AppUser user = DirectoryUserFactory.CreateUser(record, departmentId, now);
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(DirectoryUserFactory.CreateIdentity(
            user.Id,
            record,
            DirectorySyncService.LdapProvider,
            now));

        Role? defaultRole = await db.Roles
            .FirstOrDefaultAsync(role => role.Code == _options.Ldap.DefaultRoleCode, cancellationToken);

        if (defaultRole is {IsEnabled: true})
        {
            db.UserRoles.Add(new UserRole {UserId = user.Id, RoleId = defaultRole.Id});
        }
        else
        {
            logger.LogWarning(
                "域账号 {UserName} 建号时默认角色 {RoleCode} 不可用，该账号将看不到任何页面。",
                user.UserName,
                _options.Ldap.DefaultRoleCode);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("域账号 {UserName} 首次登录，已建号。", user.UserName);
        return new DirectoryProvisionResult(true, user, null);
    }

    private static DirectoryAccountSnapshot Snapshot(AppUser user)
    {
        var isSuperAdmin = user.UserRoles.Any(link =>
            string.Equals(link.Role?.Code, AuthorizationCatalog.SuperAdminRole, StringComparison.Ordinal));

        return new DirectoryAccountSnapshot(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.DepartmentId,
            user.IsEnabled,
            isSuperAdmin);
    }
}

/// <summary>
/// 建号结果。
/// </summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="User">本地账号；失败时为 null。</param>
/// <param name="ErrorMessage">失败原因，中文，可直接展示。</param>
public sealed record DirectoryProvisionResult(bool Succeeded, AppUser? User, string? ErrorMessage);

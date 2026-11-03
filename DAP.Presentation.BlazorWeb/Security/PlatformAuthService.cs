using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 表示一次密码登录的结果。
/// </summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="ErrorMessage">失败原因，成功时为空。文案直接面向用户，不含框架英文信息。</param>
/// <param name="User">成功时的用户授权视图。</param>
/// <param name="SecurityStamp">成功时的会话安全戳，写入 Cookie 用于撤销判定。</param>
public sealed record AuthSignInResult(
    bool Succeeded,
    string? ErrorMessage,
    AuthUserDto? User,
    string? SecurityStamp = null)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="user">用户授权视图。</param>
    /// <param name="securityStamp">会话安全戳。</param>
    /// <returns>登录结果。</returns>
    public static AuthSignInResult Success(AuthUserDto user, string? securityStamp) =>
        new(true, null, user, securityStamp);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="message">失败原因。</param>
    /// <returns>登录结果。</returns>
    public static AuthSignInResult Failed(string message) => new(false, message, null);
}
/// <summary>
/// 承担本地账号的登录校验、授权视图构建与落地页计算。
///
/// 这里的每个查询都以「当前是否启用」为准：停用的域、停用的角色、停用的账号都不产生权限，
/// 但授权记录本身保留——恢复启用后权限自动回来，不需要重新配置。
/// </summary>
public sealed class PlatformAuthService(
    DataAcquisitionPlatformDbContext db,
    IOptions<PlatformAuthOptions> options,
    TimeProvider timeProvider,
    ILdapDirectory ldapDirectory,
    DirectoryAccountService directoryAccounts,
    UserSessionValidator sessionValidator,
    ILogger<PlatformAuthService> logger)
{
    private readonly PlatformAuthOptions _options = options.Value;

    /// <summary>
    /// 用于「账号不存在」分支的占位哈希：只为了让该分支付出与真实校验相同的计算代价，
    /// 从而抹平响应时间差。不参与任何真实身份判断。
    /// </summary>
    private static readonly string DummyPasswordHash =
        PasswordHasher.Hash("dap-dummy-password-for-timing-equalization");

    /// <summary>
    /// 校验账号密码并返回授权视图。
    /// </summary>
    /// <param name="userName">登录账号，大小写不敏感。</param>
    /// <param name="password">明文密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果。</returns>
    public async Task<AuthSignInResult> SignInAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserName = NormalizeUserName(userName);

        if (normalizedUserName.Length == 0 || string.IsNullOrEmpty(password))
        {
            return AuthSignInResult.Failed("请输入账号与密码。");
        }

        AppUser? user = await UsersWithDetails()
            .FirstOrDefaultAsync(item => item.UserName == normalizedUserName, cancellationToken);

        // 注意顺序：**先验凭据，再判状态**。
        // 反过来（先判停用/域账号）等于给了未认证的人一个账号探针：
        // 输任意密码就能区分「账号不存在」「账号已停用」「是域账号但目录没开」，
        // 文案再统一也没用。状态提示本身是有用的（否则离职员工会以为忘了密码），
        // 但只能在证明自己知道密码之后给。
        if (user?.PasswordHash is {Length: > 0})
        {
            return await SignInLocalAsync(user, password, cancellationToken);
        }

        if (_options.Ldap.Enabled)
        {
            return await SignInWithDirectoryAsync(user, normalizedUserName, password, cancellationToken);
        }

        // 账号不存在，或是没有本地密码的域账号但目录认证没启用：对外完全同一句话。
        PasswordHasher.Verify(password, DummyPasswordHash);

        if (user is null)
        {
            logger.LogWarning("登录失败：账号 {UserName} 不存在。", normalizedUserName);
            return AuthSignInResult.Failed("账号或密码不正确。");
        }

        logger.LogWarning(
            "登录失败：账号 {UserName} 是域账号，但 Auth:Ldap:Enabled 为 false，无法校验目录密码。",
            normalizedUserName);

        return AuthSignInResult.Failed("账号或密码不正确。");
    }

    private async Task<AuthSignInResult> SignInLocalAsync(
        AppUser user,
        string? password,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (user.LockoutEndAt is { } lockoutEnd && lockoutEnd > now)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((lockoutEnd - now).TotalMinutes));
            return AuthSignInResult.Failed($"登录失败次数过多，请在 {remainingMinutes} 分钟后重试。");
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            var locked = await RegisterFailedAttemptAsync(user, now, cancellationToken);
            logger.LogWarning("登录失败：账号 {UserName} 密码不正确，累计失败 {Count} 次。",
                user.UserName, user.AccessFailedCount);

            return AuthSignInResult.Failed(locked
                ? $"密码连续错误 {_options.Lockout.MaxFailedAttempts} 次，账号已锁定 {_options.Lockout.LockoutMinutes} 分钟。"
                : "账号或密码不正确。");
        }

        // 停用判定放在凭据校验之后：见 SignInAsync 里关于账号枚举的说明。
        if (!user.IsEnabled)
        {
            logger.LogWarning("登录失败：账号 {UserName} 已停用。", user.UserName);
            return AuthSignInResult.Failed("该账号已停用，请联系管理员。");
        }

        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        AuthUserDto dto = await BuildUserDtoAsync(user, cancellationToken);
        logger.LogInformation("登录成功：账号 {UserName}。", user.UserName);

        return AuthSignInResult.Success(dto, user.SecurityStamp.ToString());
    }

    /// <summary>
    /// 域账号登录：每次登录都回目录校验一次，目录里停用的账号到不了这里
    /// （<see cref="ILdapDirectory.AuthenticateAsync"/> 会先拒绝）。
    ///
    /// 失败次数不在本地累计：AD 有自己的锁定策略，两边各锁一次会让「密码错两次」
    /// 在现场表现成「账号被锁」。
    /// </summary>
    private async Task<AuthSignInResult> SignInWithDirectoryAsync(
        AppUser? user,
        string userName,
        string? password,
        CancellationToken cancellationToken)
    {
        LdapAuthenticationResult result = await ldapDirectory.AuthenticateAsync(userName, password, cancellationToken);

        if (!result.Succeeded || result.User is null)
        {
            // 失败原因本身已带句号，模板里不再补，否则日志会出现「。。」
            logger.LogWarning("目录登录失败：账号 {UserName}，原因 {Reason}", userName, result.ErrorMessage);
            return AuthSignInResult.Failed(result.ErrorMessage ?? "账号或密码不正确。");
        }

        DirectoryProvisionResult provision =
            await directoryAccounts.EnsureUserAsync(result.User, cancellationToken);

        if (!provision.Succeeded || provision.User is null)
        {
            logger.LogWarning("目录登录失败：账号 {UserName}，{Reason}", userName, provision.ErrorMessage);
            return AuthSignInResult.Failed(provision.ErrorMessage ?? "域账号无法登录，请联系管理员。");
        }

        AppUser? reloaded = await UsersWithDetails()
            .FirstOrDefaultAsync(item => item.Id == provision.User.Id, cancellationToken);

        if (reloaded is null || !reloaded.IsEnabled)
        {
            return AuthSignInResult.Failed("该账号已停用，请联系管理员。");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        reloaded.LastLoginAt = now;
        reloaded.AccessFailedCount = 0;
        reloaded.LockoutEndAt = null;
        reloaded.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("目录登录成功：账号 {UserName}。", reloaded.UserName);
        return AuthSignInResult.Success(
            await BuildUserDtoAsync(reloaded, cancellationToken),
            reloaded.SecurityStamp.ToString());
    }

    /// <summary>
    /// 按标识重新加载授权视图。用于 Cookie 中的用户被停用或授权被调整后的下一次请求。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户授权视图；账号不存在或已停用时返回 null。</returns>
    public async Task<AuthUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        AppUser? user = await UsersWithDetails()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsEnabled)
        {
            return null;
        }

        return await BuildUserDtoAsync(user, cancellationToken);
    }

    /// <summary>
    /// 计算登录后的落地页。四个分支必须分开处理：它们的用户诉求完全不同，
    /// 落到同一个 403 页会让人无从判断该找谁。
    /// </summary>
    /// <param name="user">用户授权视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落地页地址。</returns>
    public async Task<string> ResolveLandingPageAsync(
        AuthUserDto user,
        CancellationToken cancellationToken = default)
    {
        // 分支 1：超级管理员短路，不看域。
        if (user.IsSuperAdmin)
        {
            return "/platform-overview";
        }

        // 分支 4：没有任何可见域 —— 缺授权，找管理员。
        if (user.Domains.Count == 0)
        {
            return "/no-access";
        }

        List<Guid> domainIds = await db.BusinessDomains
            .Where(domain => user.Domains.Contains(domain.Code) && domain.IsEnabled)
            .Select(domain => domain.Id)
            .ToListAsync(cancellationToken);

        // 分支 2：可见域中存在有页面的域，取排序最靠前的页面。
        var firstRoute = await db.DomainPages
            .Where(page => domainIds.Contains(page.DomainId)
                           && page.IsEnabled
                           && !page.IsOrphaned
                           && page.Domain!.IsEnabled)
            .OrderBy(page => page.Domain!.SortOrder)
            .ThenBy(page => page.SortOrder)
            .Select(page => page.Route)
            .FirstOrDefaultAsync(cancellationToken);

        // 分支 3：已授权但域内没有页面 —— 内容未就绪，不是无权限。
        return firstRoute ?? "/coming-soon";
    }

    /// <summary>
    /// 修改密码。首次登录强制改密时同样需要提供当前密码。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="currentPassword">当前密码。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        AppUser? user = await db.AppUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsEnabled)
        {
            return new AuthOperationResultDto(false, "账号不存在或已停用。");
        }

        // 域账号的密码在目录里，平台这一侧没有可校验的哈希，也不该在这里改。
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return new AuthOperationResultDto(false, "域账号的密码由目录统一管理，请通过域账号修改。");
        }

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return new AuthOperationResultDto(false, "当前密码不正确。");
        }

        var error = ValidateNewPassword(user.UserName, newPassword);
        if (error is not null)
        {
            return new AuthOperationResultDto(false, error);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return new AuthOperationResultDto(false, "新密码不能与当前密码相同。");
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword!);
        user.MustChangePassword = false;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        // 换安全戳：改密等于「我怀疑旧密码可能外泄」，那之前签发的会话必须一起作废。
        // 当前这次会话由端点重新签发（见 AuthEndpoints.ChangePasswordAsync），用户无感。
        // 必须走校验器而不是直接赋值：它同时清掉会话缓存，否则新旧会话会有一段时间完全颠倒。
        sessionValidator.RenewSecurityStamp(user);
        user.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("账号 {UserName} 已修改密码，已签发会话全部失效。", user.UserName);
        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 读取用户当前的安全戳，用于重新签发会话。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>安全戳字符串；用户不存在时返回 null。</returns>
    public async Task<string?> GetSecurityStampAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Guid? stamp = await db.AppUsers
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (Guid?)user.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken);

        return stamp?.ToString();
    }

    /// <summary>
    /// 校验新密码强度。返回 null 表示通过。
    /// </summary>
    /// <param name="userName">账号，用于拒绝与账号同名的密码。</param>
    /// <param name="password">新密码。</param>
    /// <returns>不通过时的中文提示。</returns>
    public static string? ValidateNewPassword(string userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "请输入新密码。";
        }

        if (password.Length < 8)
        {
            return "密码长度至少 8 位。";
        }

        if (password.Length > 128)
        {
            return "密码长度不能超过 128 位。";
        }

        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
        {
            return "密码不能与账号相同。";
        }

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);

        return hasLetter && hasDigit ? null : "密码需同时包含字母与数字。";
    }

    /// <summary>
    /// 按账号规范化登录名：去空白并转小写，与库中的 check 约束保持一致。
    /// </summary>
    /// <param name="userName">原始输入。</param>
    /// <returns>规范化后的账号。</returns>
    public static string NormalizeUserName(string? userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? string.Empty : userName.Trim().ToLowerInvariant();
    }

    private async Task<bool> RegisterFailedAttemptAsync(
        AppUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.Lockout.MaxFailedAttempts);
        user.AccessFailedCount++;
        user.UpdatedAt = now;

        var locked = user.AccessFailedCount >= maxAttempts;

        if (locked)
        {
            user.LockoutEndAt = now.AddMinutes(Math.Max(1, _options.Lockout.LockoutMinutes));

            // 计数清零：锁定期满后重新给满次数，否则解锁即再锁。
            user.AccessFailedCount = 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        return locked;
    }

    private async Task<AuthUserDto> BuildUserDtoAsync(AppUser user, CancellationToken cancellationToken)
    {
        List<Role> roles = user.UserRoles
            .Select(item => item.Role)
            .Where(role => role is {IsEnabled: true})
            .Select(role => role!)
            .OrderBy(role => role.SortOrder)
            .ToList();

        var isSuperAdmin = roles.Any(role =>
            string.Equals(role.Code, AuthorizationCatalog.SuperAdminRole, StringComparison.Ordinal));

        var pageLevel = roles.Count == 0 ? 0 : roles.Max(role => role.PageLevel);

        var permissions = roles
            .SelectMany(role => role.RolePermissions)
            .Where(item => item.Permission is not null)
            .Select(item => item.Permission!.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        var domains = isSuperAdmin
            ? await db.BusinessDomains
                .Where(domain => domain.IsEnabled)
                .OrderBy(domain => domain.SortOrder)
                .Select(domain => domain.Code)
                .ToListAsync(cancellationToken)
            : await LoadGrantedDomainsAsync(user.DepartmentId, cancellationToken);

        return new AuthUserDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Department?.Name,
            roles.Select(role => role.Code).ToList(),
            domains,
            isSuperAdmin ? AuthorizationCatalog.SuperAdminPageLevel : pageLevel,
            permissions,
            user.MustChangePassword,
            isSuperAdmin,
            false);
    }

    private async Task<IReadOnlyList<string>> LoadGrantedDomainsAsync(
        Guid? departmentId,
        CancellationToken cancellationToken)
    {
        // 没有部门的账号不可见任何业务域：域授权挂在部门上，单值传播。
        if (departmentId is null)
        {
            return [];
        }

        return await db.DepartmentDomainGrants
            .Where(grant => grant.DepartmentId == departmentId
                            && grant.Domain!.IsEnabled
                            && !grant.Domain.IsSystem)
            .OrderBy(grant => grant.Domain!.SortOrder)
            .Select(grant => grant.Domain!.Code)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<AppUser> UsersWithDetails()
    {
        return db.AppUsers
            .Include(user => user.Department)
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ThenInclude(role => role!.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission);
    }
}

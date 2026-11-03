using System.Globalization;
using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 在用户信息与 Cookie 会话的 claims 之间转换。
///
/// 授权判定只认这些 claim：导航过滤、路由守卫、端点策略与领域服务装饰器读同一份数据，
/// 因此全系统只有一条授权路径，不存在「这段按身份判、那段按地址判」的双轨制。
/// </summary>
public static class PlatformClaimsFactory
{
    /// <summary>
    /// 按用户信息生成会话主体。
    /// </summary>
    /// <param name="user">用户授权视图。</param>
    /// <param name="issuedAt">会话签发时间，用于绝对过期判定。</param>
    /// <returns>可直接写入 Cookie 的主体。</returns>
    public static ClaimsPrincipal CreatePrincipal(AuthUserDto user, DateTimeOffset issuedAt)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Name, user.UserName),
            new(AuthorizationCatalog.PlatformClaimTypes.UserId, user.Id.ToString()),
            new(AuthorizationCatalog.PlatformClaimTypes.UserName, user.UserName),
            new(AuthorizationCatalog.PlatformClaimTypes.DisplayName, user.DisplayName),
            new(AuthorizationCatalog.PlatformClaimTypes.PageLevel, user.PageLevel.ToString(CultureInfo.InvariantCulture)),
            new(AuthorizationCatalog.PlatformClaimTypes.IssuedAt,
                issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ];

        if (!string.IsNullOrEmpty(user.DepartmentName))
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.DepartmentName, user.DepartmentName));
        }

        if (user.IsSuperAdmin)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true"));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.MustChangePassword, "true"));
        }

        foreach (var domain in user.Domains)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.Domain, domain));
        }

        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// 读取会话签发时间。缺失或格式非法时返回 null，调用方按「无法判定」处理。
    /// </summary>
    /// <param name="principal">会话主体。</param>
    /// <returns>签发时间。</returns>
    public static DateTimeOffset? ReadIssuedAt(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(AuthorizationCatalog.PlatformClaimTypes.IssuedAt)?.Value;

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>
    /// 判断会话是否具备指定权限点。
    /// </summary>
    /// <param name="principal">会话主体。</param>
    /// <param name="permissionCode">权限点编码。</param>
    /// <returns>具备时返回 true；超级管理员恒为 true。</returns>
    public static bool HasPermission(ClaimsPrincipal? principal, string permissionCode)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (IsSuperAdmin(principal))
        {
            return true;
        }

        return principal.HasClaim(AuthorizationCatalog.PlatformClaimTypes.Permission, permissionCode);
    }

    /// <summary>
    /// 判断会话是否具备指定职能域的可见性。
    /// </summary>
    /// <param name="principal">会话主体。</param>
    /// <param name="domainCode">域编码。</param>
    /// <returns>可见时返回 true；超级管理员恒为 true。</returns>
    public static bool CanAccessDomain(ClaimsPrincipal? principal, string domainCode)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return IsSuperAdmin(principal)
               || principal.HasClaim(AuthorizationCatalog.PlatformClaimTypes.Domain, domainCode);
    }

    /// <summary>
    /// 判断会话是否为超级管理员。
    /// </summary>
    /// <param name="principal">会话主体。</param>
    /// <returns>是超级管理员时返回 true。</returns>
    public static bool IsSuperAdmin(ClaimsPrincipal? principal)
    {
        return principal?.HasClaim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true") == true;
    }
}

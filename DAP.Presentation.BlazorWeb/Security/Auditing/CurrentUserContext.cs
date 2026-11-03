using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 表示当前会话的操作者。审计的 actor 永远是员工个人，不会是班组或设备。
/// </summary>
/// <param name="UserId">用户标识。</param>
/// <param name="UserName">登录账号。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="DepartmentName">部门名称。</param>
/// <param name="Ip">来源地址。</param>
/// <param name="UserAgent">浏览器标识。</param>
/// <param name="IsSuperAdmin">是否超级管理员。</param>
/// <param name="Principal">原始会话主体，权限点判定只认它的 claims。</param>
public sealed record CurrentUser(
    Guid? UserId,
    string? UserName,
    string? DisplayName,
    string? DepartmentName,
    string? Ip,
    string? UserAgent,
    bool IsSuperAdmin,
    ClaimsPrincipal Principal)
{
    /// <summary>
    /// 判断是否具备指定权限点。超级管理员短路为 true。
    /// </summary>
    /// <param name="permissionCode">权限点编码。</param>
    /// <returns>具备时返回 true。</returns>
    public bool HasPermission(string permissionCode)
    {
        return PlatformClaimsFactory.HasPermission(Principal, permissionCode);
    }

    /// <summary>
    /// 从会话主体构建操作者视图。
    /// </summary>
    /// <param name="principal">会话主体。</param>
    /// <param name="ip">来源地址。</param>
    /// <param name="userAgent">浏览器标识。</param>
    /// <returns>操作者视图；主体未认证时返回 null。</returns>
    public static CurrentUser? FromPrincipal(ClaimsPrincipal principal, string? ip, string? userAgent)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userIdRaw = principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.UserId)?.Value;
        Guid.TryParse(userIdRaw, out var userId);

        return new CurrentUser(
            userId == Guid.Empty ? null : userId,
            principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.UserName)?.Value
            ?? principal.Identity?.Name,
            principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.DisplayName)?.Value,
            principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.DepartmentName)?.Value,
            ip,
            userAgent,
            PlatformClaimsFactory.IsSuperAdmin(principal),
            principal);
    }
}

/// <summary>
/// 提供当前操作者。HTTP 端点路径从 <see cref="HttpContext"/> 取；
/// Server 电路内没有 HttpContext，回退到级联认证状态（电路建立时的用户）。
/// 两条路径最终都归结为同一份 claims——全系统只有一条授权路径（docs/AUTH_DESIGN.md 7.2）。
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// 获取当前操作者。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作者；无法确定身份（未认证或脱离请求上下文）时返回 null。</returns>
    Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ICurrentUserContext"/> 的默认实现。
/// </summary>
internal sealed class PlatformCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider services) : ICurrentUserContext
{
    /// <inheritdoc />
    public async Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
    {
        HttpContext? http = httpContextAccessor.HttpContext;

        if (http is not null)
        {
            return CurrentUser.FromPrincipal(
                http.User,
                http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString());
        }

        // Server 电路内没有 HttpContext：尝试从认证状态提供者取电路建立时的用户。
        // 该服务只在交互式服务端组件环境下可用，取不到或调用失败按「无身份」处理。
        AuthenticationStateProvider? provider = services.GetService<AuthenticationStateProvider>();

        if (provider is null)
        {
            return null;
        }

        try
        {
            AuthenticationState state = await provider.GetAuthenticationStateAsync();
            return CurrentUser.FromPrincipal(state.User, null, null);
        }
        catch (InvalidOperationException)
        {
            // 电路外调用：Blazor 认证状态在非组件上下文不可用。
            return null;
        }
    }
}

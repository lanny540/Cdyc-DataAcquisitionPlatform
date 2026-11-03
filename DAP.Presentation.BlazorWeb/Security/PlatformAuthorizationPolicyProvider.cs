using DAP.Core.Shared.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 动态授权策略提供者：按策略名在请求时生成三类策略（docs/AUTH_DESIGN.md 7.3）。
///
/// 域可在后台新增，策略名无法在编译期固定，因此不走 <c>AuthorizeAttribute</c> 静态注册。
/// 判定只读会话 claims，不查库——登录时域、等级、权限点已全部写进 claims，
/// 授权变更后重新登录（或会话重建）即生效，不存在缓存窗口。
/// </summary>
public sealed class PlatformAuthorizationPolicyProvider(
    IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    /// <summary>
    /// 按策略名生成策略：Domain:{code} 判域可见；Access:Configure 判页面等级；
    /// PlatformAdmin 判超管；action.* 判权限点。其余交给默认提供者。
    /// </summary>
    /// <param name="policyName">策略名。</param>
    /// <returns>策略；未识别时返回 null 交给默认提供者。</returns>
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AuthorizationCatalog.PolicyNames.DomainPrefix, StringComparison.Ordinal))
        {
            var domainCode = policyName[AuthorizationCatalog.PolicyNames.DomainPrefix.Length..];

            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAssertion(context => PlatformClaimsFactory.CanAccessDomain(context.User, domainCode))
                .Build());
        }

        if (policyName == AuthorizationCatalog.PolicyNames.Configure)
        {
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAssertion(context => PlatformClaimsFactory.CanConfigure(context.User))
                .Build());
        }

        if (policyName == AuthorizationCatalog.PolicyNames.PlatformAdmin)
        {
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAssertion(context => PlatformClaimsFactory.IsSuperAdmin(context.User))
                .Build());
        }

        if (policyName.StartsWith(AuthorizationCatalog.PolicyNames.PermissionPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAssertion(context => PlatformClaimsFactory.HasPermission(context.User, policyName))
                .Build());
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}

using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="PlatformAuthorizationPolicyProvider"/> 动态策略生成测试。
///
/// 域可在后台新增，策略名无法在编译期固定：Domain:{code} / Access:Configure /
/// PlatformAdmin / action.* 四类策略按请求时生成，判定只读会话 claims 不查库。
/// 这里逐类断言生成的断言式策略对各类会话主体的判定结果。
/// </summary>
public class PlatformAuthorizationPolicyProviderTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly PlatformAuthorizationPolicyProvider Provider = new(
        Options.Create(new AuthorizationOptions()));

    private static ClaimsPrincipal CreatePrincipal(
        IReadOnlyList<string> domains,
        int pageLevel,
        IReadOnlyList<string> permissions,
        bool isSuperAdmin = false)
    {
        AuthUserDto user = new(
            Guid.NewGuid(), "tester", "测试用户", null, ["viewer"],
            domains, pageLevel, permissions,
            MustChangePassword: false, IsSuperAdmin: isSuperAdmin);

        return PlatformClaimsFactory.CreatePrincipal(user, IssuedAt);
    }

    /// <summary>取策略中唯一的断言式需求并按会话主体求值。</summary>
    private static async Task<bool> EvaluateAsync(AuthorizationPolicy? policy, ClaimsPrincipal principal)
    {
        Assert.NotNull(policy);
        AssertionRequirement requirement = Assert.Single(policy.Requirements.OfType<AssertionRequirement>());

        AuthorizationHandlerContext context = new(policy.Requirements, principal, null);
        return await requirement.Handler(context);
    }

    [Fact]
    public async Task 未识别的策略名_交给默认提供者_返回null()
    {
        Assert.Null(await Provider.GetPolicyAsync("NotAPolicyName"));
    }

    [Fact]
    public async Task 默认策略_要求已认证用户()
    {
        AuthorizationPolicy policy = await Provider.GetDefaultPolicyAsync();

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData(AuthorizationCatalog.EnergyDomain, true)]
    [InlineData(AuthorizationCatalog.BanknotePaperDomain, false)]
    public async Task 域策略_按会话中的域claims判定(string domainCode, bool expected)
    {
        ClaimsPrincipal principal = CreatePrincipal(
            [AuthorizationCatalog.EnergyDomain], AuthorizationCatalog.ViewAccessLevel, []);

        bool result = await EvaluateAsync(
            await Provider.GetPolicyAsync($"Domain:{domainCode}"), principal);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task 域策略_超管恒为true_无需域claim()
    {
        ClaimsPrincipal superAdmin = CreatePrincipal([], 1, [], isSuperAdmin: true);

        bool result = await EvaluateAsync(
            await Provider.GetPolicyAsync($"Domain:{AuthorizationCatalog.PlatformDomain}"),
            superAdmin);

        Assert.True(result);
    }

    [Fact]
    public async Task 域策略_域编码为空仍能生成策略_判定为false()
    {
        ClaimsPrincipal viewer = CreatePrincipal(["energy"], 1, []);

        // Domain: 前缀后面为空字符串：普通用户没有空编码的域 claim，判 false。
        bool result = await EvaluateAsync(await Provider.GetPolicyAsync("Domain:"), viewer);

        Assert.False(result);
    }

    [Theory]
    [InlineData(AuthorizationCatalog.ConfigureAccessLevel, true)]
    [InlineData(AuthorizationCatalog.ViewAccessLevel, false)]
    [InlineData(0, false)]
    public async Task 配置策略_按页面等级判定(int pageLevel, bool expected)
    {
        ClaimsPrincipal principal = CreatePrincipal([], pageLevel, []);

        bool result = await EvaluateAsync(
            await Provider.GetPolicyAsync(AuthorizationCatalog.PolicyNames.Configure), principal);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task 平台管理策略_仅超管通过()
    {
        ClaimsPrincipal superAdmin = CreatePrincipal([], 1, [], isSuperAdmin: true);
        ClaimsPrincipal viewer = CreatePrincipal(
            [AuthorizationCatalog.EnergyDomain], AuthorizationCatalog.SuperAdminPageLevel, []);

        // 等级 99 ≠ 超管：判定只认 SuperAdmin claim，不看等级数值。
        bool superAdminResult = await EvaluateAsync(
            await Provider.GetPolicyAsync(AuthorizationCatalog.PolicyNames.PlatformAdmin), superAdmin);
        bool viewerResult = await EvaluateAsync(
            await Provider.GetPolicyAsync(AuthorizationCatalog.PolicyNames.PlatformAdmin), viewer);

        Assert.True(superAdminResult);
        Assert.False(viewerResult);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task 权限点策略_按会话中的权限claims判定(bool hasPermission, bool expected)
    {
        ClaimsPrincipal principal = CreatePrincipal(
            [], 1, hasPermission ? [AuthorizationCatalog.ShiftManagePermission] : []);

        bool result = await EvaluateAsync(
            await Provider.GetPolicyAsync(AuthorizationCatalog.ShiftManagePermission), principal);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task 权限点策略_超管恒为true()
    {
        ClaimsPrincipal superAdmin = CreatePrincipal([], 1, [], isSuperAdmin: true);

        bool result = await EvaluateAsync(
            await Provider.GetPolicyAsync(AuthorizationCatalog.ShiftManagePermission), superAdmin);

        Assert.True(result);
    }
}

using System.Net;
using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Tests.UnitTests;

/// <summary>
/// CurrentUser / PlatformCurrentUserContext 的单元测试。
///
/// 覆盖的核心规则：
/// - FromPrincipal 未认证返回 null，认证后按 claims 解析各字段，UserId 非法时为 null；
/// - UserName 缺失时回退到 Identity.Name；
/// - HasPermission 超管短路，其余只认 permission claim；
/// - HTTP 路径从 HttpContext 取（含 IP / UserAgent），电路路径回退到 AuthenticationStateProvider。
/// </summary>
public sealed class CurrentUserContextTests
{
    private static ClaimsPrincipal BuildPrincipal(
        Guid? userId = null,
        string? userName = null,
        string? identityName = null,
        string? displayName = null,
        string? departmentName = null,
        bool isSuperAdmin = false,
        string[]? permissions = null,
        bool isAuthenticated = true)
    {
        List<Claim> claims = [];

        if (identityName is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, identityName));
        }

        if (userId is { } id)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.UserId, id.ToString()));
        }

        if (userName is not null)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.UserName, userName));
        }

        if (displayName is not null)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.DisplayName, displayName));
        }

        if (departmentName is not null)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.DepartmentName, departmentName));
        }

        if (isSuperAdmin)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true"));
        }

        foreach (string permission in permissions ?? [])
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission));
        }

        ClaimsIdentity identity = new(claims, isAuthenticated ? "TestAuthentication" : null);
        return new ClaimsPrincipal(identity);
    }

    // ------------------------------------------------------------------
    // CurrentUser.FromPrincipal
    // ------------------------------------------------------------------

    [Fact]
    public void FromPrincipal_Unauthenticated_Should_Return_Null()
    {
        ClaimsPrincipal principal = BuildPrincipal(Guid.NewGuid(), isAuthenticated: false);

        Assert.Null(CurrentUser.FromPrincipal(principal, "127.0.0.1", "agent"));
    }

    [Fact]
    public void FromPrincipal_Should_Parse_All_Claims()
    {
        Guid userId = Guid.NewGuid();
        ClaimsPrincipal principal = BuildPrincipal(
            userId, "zhang3", displayName: "张三",
            departmentName: "一部", isSuperAdmin: false,
            permissions: [AuthorizationCatalog.UserManagePermission]);

        CurrentUser? user = CurrentUser.FromPrincipal(principal, "10.1.2.3", "test-agent");

        Assert.NotNull(user);
        Assert.Equal(userId, user.UserId);
        Assert.Equal("zhang3", user.UserName);
        Assert.Equal("张三", user.DisplayName);
        Assert.Equal("一部", user.DepartmentName);
        Assert.Equal("10.1.2.3", user.Ip);
        Assert.Equal("test-agent", user.UserAgent);
        Assert.False(user.IsSuperAdmin);
        Assert.True(user.HasPermission(AuthorizationCatalog.UserManagePermission));
        Assert.False(user.HasPermission(AuthorizationCatalog.SystemManagePermission));
    }

    [Fact]
    public void FromPrincipal_Missing_UserId_Claim_Should_Return_Null_UserId()
    {
        ClaimsPrincipal principal = BuildPrincipal(userName: "anonymous", identityName: "anonymous");

        CurrentUser? user = CurrentUser.FromPrincipal(principal, null, null);

        Assert.NotNull(user);
        Assert.Null(user.UserId);
    }

    [Fact]
    public void FromPrincipal_Invalid_UserId_Claim_Should_Return_Null_UserId()
    {
        ClaimsIdentity identity = new(
        [
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserId, "not-a-guid"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.UserName, "broken")
        ], "TestAuthentication");

        CurrentUser? user = CurrentUser.FromPrincipal(new ClaimsPrincipal(identity), null, null);

        Assert.NotNull(user);
        Assert.Null(user.UserId);
    }

    [Fact]
    public void FromPrincipal_UserName_Should_Fall_Back_To_Identity_Name()
    {
        ClaimsPrincipal principal = BuildPrincipal(identityName: "legacy-name");

        CurrentUser? user = CurrentUser.FromPrincipal(principal, null, null);

        Assert.NotNull(user);
        Assert.Equal("legacy-name", user.UserName);
    }

    [Fact]
    public void FromPrincipal_SuperAdmin_Flag_Should_Be_Parsed()
    {
        ClaimsPrincipal principal = BuildPrincipal(Guid.NewGuid(), isSuperAdmin: true);

        CurrentUser? user = CurrentUser.FromPrincipal(principal, null, null);

        Assert.NotNull(user);
        Assert.True(user.IsSuperAdmin);
    }

    // ------------------------------------------------------------------
    // CurrentUser.HasPermission
    // ------------------------------------------------------------------

    [Fact]
    public void HasPermission_SuperAdmin_Should_Short_Circuit()
    {
        // 超管不带任何权限点 claim，也必须对任意权限点返回 true。
        ClaimsPrincipal principal = BuildPrincipal(Guid.NewGuid(), isSuperAdmin: true);
        CurrentUser user = CurrentUser.FromPrincipal(principal, null, null)!;

        Assert.True(user.HasPermission(AuthorizationCatalog.SystemManagePermission));
        Assert.True(user.HasPermission("action.anything.else"));
    }

    [Fact]
    public void HasPermission_Unauthenticated_Principal_Should_Return_False()
    {
        ClaimsPrincipal unauthenticated = BuildPrincipal(isAuthenticated: false);
        CurrentUser user = new(null, null, null, null, null, null, false, unauthenticated);

        Assert.False(user.HasPermission(AuthorizationCatalog.UserManagePermission));
    }

    // ------------------------------------------------------------------
    // PlatformCurrentUserContext：HTTP 路径
    // ------------------------------------------------------------------

    [Fact]
    public async Task PlatformCurrentUserContext_HttpContext_Should_Provide_User_With_Ip_And_UserAgent()
    {
        Guid userId = Guid.NewGuid();
        DefaultHttpContext http = new() { User = BuildPrincipal(userId, "http-user") };
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        http.Request.Headers.UserAgent = "browser-agent";

        HttpContextAccessor accessor = new() { HttpContext = http };
        PlatformCurrentUserContext context = new(accessor, new EmptyServiceProvider());

        CurrentUser? user = await context.GetUserAsync();

        Assert.NotNull(user);
        Assert.Equal(userId, user.UserId);
        Assert.Equal("10.1.2.3", user.Ip);
        Assert.Equal("browser-agent", user.UserAgent);
    }

    [Fact]
    public async Task PlatformCurrentUserContext_HttpContext_Unauthenticated_Should_Return_Null()
    {
        DefaultHttpContext http = new() { User = BuildPrincipal(isAuthenticated: false) };
        HttpContextAccessor accessor = new() { HttpContext = http };
        PlatformCurrentUserContext context = new(accessor, new EmptyServiceProvider());

        Assert.Null(await context.GetUserAsync());
    }

    // ------------------------------------------------------------------
    // PlatformCurrentUserContext：Server 电路回退路径
    // ------------------------------------------------------------------

    [Fact]
    public async Task PlatformCurrentUserContext_Circuit_Should_Fall_Back_To_AuthenticationState()
    {
        Guid userId = Guid.NewGuid();
        StubAuthenticationStateProvider provider = new(
            new AuthenticationState(BuildPrincipal(userId, "circuit-user")));

        HttpContextAccessor accessor = new() { HttpContext = null };
        ServiceCollection services = new();
        services.AddSingleton<AuthenticationStateProvider>(provider);
        IServiceProvider providerServices = services.BuildServiceProvider();
        PlatformCurrentUserContext context = new(accessor, providerServices);

        CurrentUser? user = await context.GetUserAsync();

        Assert.NotNull(user);
        Assert.Equal(userId, user.UserId);
        Assert.Equal("circuit-user", user.UserName);
        // 电路内没有 HttpContext，来源信息为空。
        Assert.Null(user.Ip);
        Assert.Null(user.UserAgent);
    }

    [Fact]
    public async Task PlatformCurrentUserContext_Circuit_Without_Provider_Should_Return_Null()
    {
        HttpContextAccessor accessor = new() { HttpContext = null };
        ServiceCollection services = new();
        IServiceProvider providerServices = services.BuildServiceProvider();
        PlatformCurrentUserContext context = new(accessor, providerServices);

        Assert.Null(await context.GetUserAsync());
    }

    [Fact]
    public async Task PlatformCurrentUserContext_Circuit_ProviderError_Should_Return_Null()
    {
        StubAuthenticationStateProvider provider = new(null);
        HttpContextAccessor accessor = new() { HttpContext = null };
        ServiceCollection services = new();
        services.AddSingleton<AuthenticationStateProvider>(provider);
        IServiceProvider providerServices = services.BuildServiceProvider();
        PlatformCurrentUserContext context = new(accessor, providerServices);

        // 电路外调用抛 InvalidOperationException，按「无身份」处理。
        Assert.Null(await context.GetUserAsync());
    }

    /// <summary>认证状态提供者替身：state 为 null 时模拟电路外调用抛 InvalidOperationException。</summary>
    private sealed class StubAuthenticationStateProvider(AuthenticationState? state) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return state is null
                ? throw new InvalidOperationException("Blazor 认证状态在非组件上下文不可用。")
                : Task.FromResult(state);
        }
    }

    /// <summary>空容器替身：GetService 恒返回 null。</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

using System.Security.Claims;
using DAP.Core.Shared.Contracts;
using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 会话声明工厂的分支补充：无安全戳、无部门、车间终端、强制改密与读取回退行为。
/// </summary>
public sealed class PlatformClaimsFactoryTests
{
    [Fact]
    public void CreatePrincipal_WithoutOptionalFields_ShouldOnlyContainBaseClaims()
    {
        AuthUserDto user = new(
            Guid.NewGuid(),
            "user01",
            "用户一",
            DepartmentName: null,
            Roles: [],
            Domains: [],
            PageLevel: 1,
            Permissions: [],
            MustChangePassword: false,
            IsSuperAdmin: false,
            IsBoardTerminal: false);

        ClaimsPrincipal principal = PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow);

        Assert.Null(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.SecurityStamp));
        Assert.Null(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.DepartmentName));
        Assert.Null(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin));
        Assert.Null(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.BoardTerminal));
        Assert.Null(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.MustChangePassword));
    }

    [Fact]
    public void CreatePrincipal_WithBoardTerminalAndMustChangePassword_ShouldAddFlags()
    {
        AuthUserDto user = new(
            Guid.NewGuid(),
            "board01",
            "车间终端",
            "钞纸车间",
            Roles: [],
            Domains: ["banknote-paper"],
            PageLevel: 1,
            Permissions: [],
            MustChangePassword: true,
            IsSuperAdmin: false,
            IsBoardTerminal: true);

        ClaimsPrincipal principal = PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow, "stamp-1");

        Assert.NotNull(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.SecurityStamp));
        Assert.Equal("钞纸车间", principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.DepartmentName)?.Value);
        Assert.NotNull(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.BoardTerminal));
        Assert.NotNull(principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.MustChangePassword));
        Assert.Equal("banknote-paper", principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.Domain)?.Value);
    }

    [Fact]
    public void ReadIssuedAt_ShouldReturnNull_WhenClaimMissingOrInvalid()
    {
        ClaimsPrincipal empty = new(new ClaimsIdentity());

        Assert.Null(PlatformClaimsFactory.ReadIssuedAt(null));
        Assert.Null(PlatformClaimsFactory.ReadIssuedAt(empty));

        ClaimsIdentity invalid = new(
        [
            new Claim(AuthorizationCatalog.PlatformClaimTypes.IssuedAt, "not-a-number")
        ]);
        Assert.Null(PlatformClaimsFactory.ReadIssuedAt(new ClaimsPrincipal(invalid)));
    }

    [Fact]
    public void ReadIssuedAt_ShouldRoundTripUnixSeconds()
    {
        DateTimeOffset issuedAt = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        AuthUserDto user = new(
            Guid.NewGuid(),
            "user02",
            "用户二",
            null,
            [],
            [],
            1,
            [],
            false,
            false);

        ClaimsPrincipal principal = PlatformClaimsFactory.CreatePrincipal(user, issuedAt);

        Assert.Equal(issuedAt, PlatformClaimsFactory.ReadIssuedAt(principal));
    }

    [Fact]
    public void ReadPageLevel_ShouldFallBackToZero_WhenClaimMissingOrInvalid()
    {
        ClaimsPrincipal empty = new(new ClaimsIdentity());
        Assert.Equal(0, PlatformClaimsFactory.ReadPageLevel(empty));
        Assert.False(PlatformClaimsFactory.CanConfigure(empty));

        ClaimsIdentity invalid = new(
        [
            new Claim(AuthorizationCatalog.PlatformClaimTypes.PageLevel, "abc")
        ]);
        Assert.Equal(0, PlatformClaimsFactory.ReadPageLevel(new ClaimsPrincipal(invalid)));
    }
}

using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 登录回跳地址校验的单元测试。三条入口（表单登录、OIDC 挑战、OIDC 回调）共用这一份实现，
/// 任何一处放松都会把登录页变成开放重定向跳板。
/// </summary>
public class PlatformReturnUrlTests
{
    [Theory]
    [InlineData("/energy-dashboard", "/energy-dashboard")]
    [InlineData("/user-management?keyword=zhang", "/user-management?keyword=zhang")]
    [InlineData("/", "/")]
    public void Same_Site_Relative_Url_Should_Pass(string input, string expected)
    {
        Assert.Equal(expected, PlatformReturnUrl.Resolve(input, "/landing"));
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("evil.example.com")]
    [InlineData("javascript:alert(1)")]
    public void External_Url_Should_Fall_Back_To_Landing(string input)
    {
        Assert.Equal("/landing", PlatformReturnUrl.Resolve(input, "/landing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Value_Should_Fall_Back_To_Landing(string? input)
    {
        Assert.Equal("/landing", PlatformReturnUrl.Resolve(input, "/landing"));
    }

    /// <summary>
    /// 登录页与认证接口自身不能作为回跳目标，否则会形成重定向环。
    /// </summary>
    [Theory]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=%2Fenergy-dashboard")]
    [InlineData("/api/auth/me")]
    public void Auth_Paths_Should_Fall_Back_To_Landing(string input)
    {
        Assert.Equal("/landing", PlatformReturnUrl.Resolve(input, "/landing"));
    }
}

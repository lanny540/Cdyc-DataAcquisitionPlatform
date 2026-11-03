using System.Security.Claims;
using DAP.Presentation.BlazorWeb.Security.External;

namespace DAP.Tests.UnitTests;

/// <summary>
/// OIDC claims 映射与配置校验的单元测试（docs/AUTH_DESIGN.md 11.2）。
///
/// 真实 IdP 上很难逐个复现「claim 名换了」「groups 带前缀」「sub 缺失」这些差异，
/// 因此把映射抽成纯函数在这里钉住。
/// </summary>
public class OidcClaimsMapperTests
{
    private static Claim[] Claims(params (string Type, string Value)[] items)
    {
        return items.Select(item => new Claim(item.Type, item.Value)).ToArray();
    }

    [Fact]
    public void Map_Should_Read_Standard_Claims()
    {
        var profile = OidcClaimsMapper.Map(
            Claims(
                ("sub", "abc-123"),
                ("name", "张三"),
                ("email", "zhang3@example.com"),
                ("preferred_username", "Zhang3")),
            new OidcProviderOptions());

        Assert.NotNull(profile);
        Assert.Equal("abc-123", profile!.ExternalId);
        Assert.Equal("zhang3", profile.UserName);
        Assert.Equal("张三", profile.DisplayName);
        Assert.Equal("zhang3@example.com", profile.Email);
        Assert.Null(profile.DepartmentCode);
    }

    /// <summary>
    /// sub 是唯一键。缺失时**必须拒绝**而不是退回 email：
    /// 邮箱会变、用户名会被回收，用它们做键迟早把两个真实用户串成一个账号。
    /// </summary>
    [Fact]
    public void Missing_Subject_Should_Be_Rejected()
    {
        OidcUserProfile? profile = OidcClaimsMapper.Map(
            Claims(("email", "zhang3@example.com"), ("name", "张三")),
            new OidcProviderOptions());

        Assert.Null(profile);
    }

    [Fact]
    public void User_Name_Should_Fall_Back_To_Email_Local_Part()
    {
        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("email", "Li.Si@example.com")),
            new OidcProviderOptions());

        Assert.Equal("li.si", profile!.UserName);
    }

    [Fact]
    public void User_Name_Should_Fall_Back_To_Subject_Tail()
    {
        var profile = OidcClaimsMapper.Map(Claims(("sub", "0123456789abcdef")), new OidcProviderOptions());

        Assert.Equal("oidc-0123456789ab", profile!.UserName);
        Assert.Equal("oidc-0123456789ab", profile.DisplayName);
    }

    [Fact]
    public void Department_Should_Use_Configured_Claim_And_Strip_Prefix()
    {
        OidcProviderOptions options = new()
        {
            DepartmentClaims = ["groups"],
            DepartmentPrefix = "APP-DEPT-"
        };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("groups", "APP-DEPT-paper-1")),
            options);

        Assert.Equal("paper-1", profile!.DepartmentCode);
    }

    [Fact]
    public void Department_Should_Try_Claims_In_Order()
    {
        OidcProviderOptions options = new() { DepartmentClaims = ["department", "groups"] };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("groups", "fallback")),
            options);

        Assert.Equal("fallback", profile!.DepartmentCode);

        var preferred = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("department", "primary"), ("groups", "fallback")),
            options);

        Assert.Equal("primary", preferred!.DepartmentCode);
    }

    [Fact]
    public void Department_Should_Be_Null_When_Prefix_Strips_Everything()
    {
        OidcProviderOptions options = new()
        {
            DepartmentClaims = ["groups"],
            DepartmentPrefix = "APP-DEPT-"
        };

        var profile = OidcClaimsMapper.Map(Claims(("sub", "abc"), ("groups", "APP-DEPT-")), options);

        Assert.Null(profile!.DepartmentCode);
    }

    /// <summary>
    /// AD / Okta 的 groups 是「全量组」多值：只取第一个值会把 staff 当成部门。
    /// 配了前缀就要按前缀挑。
    /// </summary>
    [Fact]
    public void Multi_Valued_Department_Claim_Should_Pick_Prefixed_Value()
    {
        OidcProviderOptions options = new()
        {
            DepartmentClaims = ["groups"],
            DepartmentPrefix = "dept:"
        };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("groups", "staff"), ("groups", "dept:PRINT2"), ("groups", "vpn")),
            options);

        Assert.Equal("PRINT2", profile!.DepartmentCode);
    }

    /// <summary>
    /// 多值里没有任何一个带前缀时，退回第一个值而不是直接丢部门——
    /// 部门匹配失败应该进待处理表让管理员看见，不能静默变成「无部门」。
    /// </summary>
    [Fact]
    public void Multi_Valued_Department_Claim_Should_Fall_Back_To_First_Value()
    {
        OidcProviderOptions options = new()
        {
            DepartmentClaims = ["groups"],
            DepartmentPrefix = "dept:"
        };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("groups", "staff"), ("groups", "vpn")),
            options);

        Assert.Equal("staff", profile!.DepartmentCode);
    }

    /// <summary>
    /// 未配置前缀时行为不变：多值取第一个。
    /// </summary>
    [Fact]
    public void Multi_Valued_Department_Claim_Should_Use_First_Value_Without_Prefix()
    {
        OidcProviderOptions options = new() { DepartmentClaims = ["groups"] };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("groups", "PAPER1"), ("groups", "PRINT2")),
            options);

        Assert.Equal("PAPER1", profile!.DepartmentCode);
    }

    [Fact]
    public void Custom_Claim_Names_Should_Be_Honored()
    {
        OidcProviderOptions options = new()
        {
            NameClaim = "cn",
            EmailClaim = "mail",
            UserNameClaim = "uid"
        };

        var profile = OidcClaimsMapper.Map(
            Claims(("sub", "abc"), ("cn", "李四"), ("mail", "li4@example.com"), ("uid", "Li4")),
            options);

        Assert.Equal("李四", profile!.DisplayName);
        Assert.Equal("li4@example.com", profile.Email);
        Assert.Equal("li4", profile.UserName);
    }

    [Fact]
    public void Derived_User_Name_Should_Be_Truncated()
    {
        var longName = new string('a', 150);

        Assert.Equal(100, OidcClaimsMapper.DeriveUserName(longName, "abc").Length);
    }

    [Fact]
    public void Callback_Path_Should_Stay_Under_Auth_Api()
    {
        OidcProviderOptions options = new() { Code = "corp" };

        Assert.Equal("/api/auth/oidc/corp/callback", options.CallbackPath);
        Assert.Equal("oidc-corp", options.SchemeName);
    }
}

/// <summary>
/// 外部身份源配置校验的单元测试。
/// </summary>
public class ExternalIdentityOptionsTests
{
    [Fact]
    public void Disabled_Ldap_Should_Not_Require_Anything()
    {
        Assert.Empty(new DAP.Presentation.BlazorWeb.Security.Directory.LdapOptions().Validate());
    }

    [Fact]
    public void Enabled_Ldap_Without_Host_Should_Fail()
    {
        DAP.Presentation.BlazorWeb.Security.Directory.LdapOptions options = new() { Enabled = true };

        IReadOnlyList<string> errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("Host"));
        Assert.Contains(errors, error => error.Contains("BaseDn"));
        Assert.Contains(errors, error => error.Contains("BindDn"));
    }

    [Fact]
    public void User_Filter_Without_Placeholder_Should_Fail()
    {
        DAP.Presentation.BlazorWeb.Security.Directory.LdapOptions options = new()
        {
            Enabled = true,
            Host = "ldap.example.com",
            BindDn = "cn=svc,dc=example,dc=com",
            BindPassword = "secret",
            BaseDn = "dc=example,dc=com",
            UserFilter = "(sAMAccountName=zhang3)"
        };

        Assert.Contains(options.Validate(), error => error.Contains("{0}"));
    }

    [Fact]
    public void Plain_Transport_To_Remote_Host_Should_Be_Rejected()
    {
        DAP.Presentation.BlazorWeb.Security.Directory.LdapOptions options = new()
        {
            Enabled = true,
            Host = "ldap.example.com",
            BindDn = "cn=svc,dc=example,dc=com",
            BindPassword = "secret",
            BaseDn = "dc=example,dc=com",
            TransportSecurity = DAP.Presentation.BlazorWeb.Security.Directory.LdapTransportSecurity.None
        };

        Assert.Contains(options.Validate(), error => error.Contains("明文"));
    }

    /// <summary>
    /// 明文只在本机测试目录上放行：判定依据是地址本身，而不是「开发环境」标志
    /// ——标志会被误带进生产配置。
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void Plain_Transport_To_Loopback_Should_Be_Allowed(string host)
    {
        DAP.Presentation.BlazorWeb.Security.Directory.LdapOptions options = new()
        {
            Enabled = true,
            Host = host,
            BindDn = "cn=svc,dc=example,dc=com",
            BindPassword = "secret",
            BaseDn = "dc=example,dc=com",
            TransportSecurity = DAP.Presentation.BlazorWeb.Security.Directory.LdapTransportSecurity.None
        };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Disabled_Oidc_Should_Not_Require_Anything()
    {
        Assert.Empty(new OidcOptions().Validate());
    }

    [Fact]
    public void Enabled_Oidc_Without_Providers_Should_Fail()
    {
        OidcOptions options = new() { Enabled = true };

        Assert.Contains(options.Validate(), error => error.Contains("提供方"));
    }

    [Theory]
    [InlineData("Corp", false)]
    [InlineData("co", true)]
    [InlineData("c", false)]
    [InlineData("corp-login", true)]
    [InlineData("corp_login", false)]
    public void Provider_Code_Format_Should_Be_Enforced(string code, bool expectedValid)
    {
        OidcProviderOptions provider = new()
        {
            Code = code,
            Authority = "https://idp.example.com",
            ClientId = "dap"
        };

        Assert.Equal(expectedValid, provider.Validate().Count == 0);
    }

    [Fact]
    public void Duplicate_Provider_Code_Should_Be_Rejected()
    {
        OidcProviderOptions provider = new()
        {
            Code = "corp",
            Authority = "https://idp.example.com",
            ClientId = "dap"
        };

        OidcOptions options = new() { Enabled = true, Providers = [provider, provider] };

        Assert.Contains(options.Validate(), error => error.Contains("重复"));
    }

    [Fact]
    public void Http_Authority_Should_Be_Rejected_Unless_Explicitly_Allowed()
    {
        OidcProviderOptions provider = new()
        {
            Code = "corp",
            Authority = "http://localhost:9000",
            ClientId = "dap",
            RequireHttpsMetadata = true
        };

        Assert.Contains(provider.Validate(), error => error.Contains("https"));

        provider.RequireHttpsMetadata = false;
        Assert.Empty(provider.Validate());
    }
}

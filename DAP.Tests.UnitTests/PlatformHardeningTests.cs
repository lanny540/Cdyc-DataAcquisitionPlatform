using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 传输层与导出层的加固测试：同源校验、咨询锁键、CSV 公式注入。
///
/// 这三处都属于「写错了不会报错、只会在生产上出事」的类型：
/// 同源校验放过跨站请求 = CSRF 防护形同虚设；锁键不稳定 = 多实例互斥失效；
/// CSV 转义漏一个字符 = 别人的 Excel 里执行命令。
/// </summary>
public class PlatformHardeningTests
{
    [Theory]
    [InlineData("/api/auth/login", "POST")]
    [InlineData("/api/auth/change-password", "POST")]
    [InlineData("/api/admin/users", "PUT")]
    [InlineData("/api/shift-schedules/import", "POST")]
    [InlineData("/api/collection-points", "POST")]
    [InlineData("/api/collection-data/ingest", "POST")]
    public void Same_Origin_Guard_Should_Cover_Unsafe_Auth_Requests(string path, string method)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Method = method;

        Assert.True(PlatformSameOriginGuard.RequiresCheck(context.Request));
    }

    [Theory]
    [InlineData("/api/auth/oidc/corp/callback", "POST")] // IdP 用 form_post 跨站回跳
    [InlineData("/api/auth/me", "GET")]
    [InlineData("/_blazor/negotiate", "POST")] // SignalR 不在 /api 下，也不该被拦
    public void Same_Origin_Guard_Should_Exempt_These_Requests(string path, string method)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Method = method;

        Assert.False(PlatformSameOriginGuard.RequiresCheck(context.Request));
    }

    [Fact]
    public void Same_Origin_Guard_Should_Compare_Origin_With_Host()
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("dap.cdyc.local");

        context.Request.Headers.Origin = "http://dap.cdyc.local";
        Assert.True(PlatformSameOriginGuard.IsSameOrigin(context.Request));

        // 跨站来源必须被拒：这是 SameSite 被削弱后唯一还站得住的防线。
        context.Request.Headers.Origin = "http://evil.example.com";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));
    }

    [Fact]
    public void Same_Origin_Guard_Should_Fall_Back_To_Referer_And_Allow_No_Header()
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("dap.cdyc.local");

        // 没有 Origin 与 Referer：非浏览器客户端（curl、健康检查），放行。
        Assert.True(PlatformSameOriginGuard.IsSameOrigin(context.Request));

        context.Request.Headers.Referer = "http://dap.cdyc.local/login";
        Assert.True(PlatformSameOriginGuard.IsSameOrigin(context.Request));

        context.Request.Headers.Referer = "http://evil.example.com/x";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));
    }

    [Fact]
    public void Board_Terminal_Principal_Should_Carry_Board_Claim_Without_Privileges()
    {
        // 车间只读会话在 WASM 端也必须是「已认证」：否则免登录看板在
        // WASM 缓存命中后会被路由守卫踢回登录页（Id 为 Guid.Empty，不对应任何员工）。
        AuthUserDto board = new(
            Guid.Empty,
            "车间只读终端",
            "车间只读终端",
            null,
            [],
            [AuthorizationCatalog.BanknotePaperDomain],
            AuthorizationCatalog.ViewAccessLevel,
            [],
            false,
            false,
            true);

        var principal = PlatformClaimsFactory.CreatePrincipal(board, DateTimeOffset.UtcNow);

        Assert.True(PlatformClaimsFactory.IsBoardTerminal(principal));
        Assert.False(PlatformClaimsFactory.IsSuperAdmin(principal));
        Assert.Equal(AuthorizationCatalog.ViewAccessLevel, PlatformClaimsFactory.ReadPageLevel(principal));
        Assert.True(PlatformClaimsFactory.CanAccessDomain(principal, AuthorizationCatalog.BanknotePaperDomain));
        Assert.False(PlatformClaimsFactory.HasPermission(principal, AuthorizationCatalog.UserManagePermission));
    }

    [Fact]
    public void Secret_Protector_Should_Round_Trip_And_Tolerate_Legacy_Plaintext()
    {
        var protector = new PlatformSecretProtector(new EphemeralDataProtectionProvider());

        var cipher = protector.Protect("Pa55word");

        Assert.True(PlatformSecretProtector.IsProtected(cipher));
        Assert.NotEqual("Pa55word", cipher);
        Assert.Equal("Pa55word", protector.Unprotect(cipher));

        // 幂等：掩码分支会把库里的密文原样带回，再加密一次不能套两层。
        Assert.Equal(cipher, protector.Protect(cipher));

        // 历史数据是明文：没有前缀就按原值使用，不需要迁移。
        Assert.Equal("legacy-plain", protector.Unprotect("legacy-plain"));

        Assert.Equal(string.Empty, protector.Protect(null));
        Assert.Equal(string.Empty, protector.Unprotect(null));
    }

    [Fact]
    public void Secret_Protector_Should_Degrade_Instead_Of_Throwing_On_Foreign_Key()
    {
        var writer = new PlatformSecretProtector(new EphemeralDataProtectionProvider());
        var reader = new PlatformSecretProtector(new EphemeralDataProtectionProvider());

        var cipher = writer.Protect("Pa55word");

        // 换了密钥环：解不开就当作「未配置密码」，而不是把密文当密码发给上游。
        Assert.Equal(string.Empty, reader.Unprotect(cipher));
    }

    [Fact]
    public void Advisory_Lock_Key_Should_Be_Stable_And_Distinct()
    {
        var first = PlatformAdvisoryLock.ComputeKey("directory-sync");
        var second = PlatformAdvisoryLock.ComputeKey("directory-sync");
        var other = PlatformAdvisoryLock.ComputeKey("audit-retention");

        // 稳定：不能用 string.GetHashCode（每进程随机种子），否则重启后锁名就变了。
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData("正常文本", "正常文本")]
    [InlineData("含,逗号", "\"含,逗号\"")]
    [InlineData("含\"引号", "\"含\"\"引号\"")]
    public void Csv_Field_Should_Escape_Delimiters(string input, string expected)
    {
        Assert.Equal(expected, CsvField.Escape(input));
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A0")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tTAB")]
    public void Csv_Field_Should_Neutralize_Formula_Injection(string payload)
    {
        var escaped = CsvField.Escape(payload);

        // 必须以单引号开头：Excel 才会当成字面量而不是公式。
        Assert.StartsWith("'", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_Field_Should_Keep_Empty_Value_Empty()
    {
        Assert.Equal(string.Empty, CsvField.Escape(null));
        Assert.Equal(string.Empty, CsvField.Escape(string.Empty));
    }
}

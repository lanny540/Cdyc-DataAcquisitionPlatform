using DAP.Presentation.BlazorWeb.Security.External;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="OidcAuthenticationExtensions"/> 的提供方注册测试。
///
/// 平台复用框架的 OpenIdConnect 处理器，安全细节（PKCE、correlation/nonce Cookie、
/// 回调路径落在 /api/auth 登录闸门豁免段）全部来自这里的注册参数，写错即登录被绕过。
/// </summary>
public class OidcAuthenticationExtensionsTests
{
    /// <summary>构造一个已启用、可用的企业 IdP 配置。</summary>
    private static OidcOptions CreateOptions(params OidcProviderOptions[] providers)
    {
        return new OidcOptions { Enabled = true, Providers = [.. providers] };
    }

    private static OidcProviderOptions CreateProvider()
    {
        return new OidcProviderOptions
        {
            Code = "corp",
            DisplayName = "企业统一登录",
            Authority = "https://idp.example.com",
            ClientId = "dap-client",
            ClientSecret = "secret-1",
            Scopes = ["openid", "profile"],
            NameClaim = "name",
            RequireHttpsMetadata = true
        };
    }

    /// <summary>克隆并覆盖单个属性（OidcProviderOptions 是 class，没有 with）。</summary>
    private static OidcProviderOptions Clone(
        OidcProviderOptions source, Action<OidcProviderOptions> mutate)
    {
        OidcProviderOptions copy = new()
        {
            Code = source.Code,
            DisplayName = source.DisplayName,
            Authority = source.Authority,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            Scopes = [.. source.Scopes],
            NameClaim = source.NameClaim,
            RequireHttpsMetadata = source.RequireHttpsMetadata
        };
        mutate(copy);
        return copy;
    }

    /// <summary>注册后解析 DI，取 scheme 列表与指定 scheme 的 OIDC 选项。</summary>
    private static (IServiceProvider Provider, AuthenticationBuilder Builder) Build(
        OidcOptions options, bool requireSecureCookie)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());

        AuthenticationBuilder builder = services.AddAuthentication(
            CookieAuthenticationDefaults.AuthenticationScheme);
        builder.AddPlatformOidcProviders(options, requireSecureCookie);

        // OIDC 处理器的后置配置需要数据保护提供者：用内存实现，不落盘。
        services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();

        return (services.BuildServiceProvider(), builder);
    }

    private static OpenIdConnectOptions GetOptions(IServiceProvider provider, string scheme)
    {
        return provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(scheme);
    }

    [Fact]
    public async Task 已启用的提供方_注册为独立scheme()
    {
        (IServiceProvider provider, _) = Build(CreateOptions(CreateProvider()), false);

        AuthenticationScheme[] schemes =
            (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
            .Where(scheme => scheme.Name.StartsWith("oidc-", StringComparison.Ordinal))
            .ToArray();

        AuthenticationScheme scheme = Assert.Single(schemes);
        Assert.Equal("oidc-corp", scheme.Name);
        Assert.Equal(typeof(OpenIdConnectHandler), scheme.HandlerType);
    }

    [Fact]
    public void 未启用OIDC_不注册任何scheme()
    {
        OidcOptions options = new() { Enabled = false, Providers = [CreateProvider()] };

        (IServiceProvider provider, _) = Build(options, false);

        // Enabled=false 时 EnabledProviders 返回空：即使配置了提供方也不注册。
        OpenIdConnectOptions resolved = GetOptions(provider, "oidc-corp");
        Assert.Null(resolved.Authority);
    }

    [Fact]
    public void 不可用的提供方_被跳过()
    {
        OidcProviderOptions unusable = Clone(CreateProvider(), copy => copy.Authority = " ");
        OidcProviderOptions usable = Clone(CreateProvider(), copy => copy.Code = "usable");

        (IServiceProvider provider, _) = Build(CreateOptions(unusable, usable), false);

        // 缺 Authority 的提供方不注册，不影响其他提供方。
        OpenIdConnectOptions unusableOptions = GetOptions(provider, "oidc-corp");
        OpenIdConnectOptions usableOptions = GetOptions(provider, "oidc-usable");

        Assert.Null(unusableOptions.Authority);
        Assert.Equal("https://idp.example.com", usableOptions.Authority);
    }

    [Fact]
    public void 注册参数_授权码加PKCE_令牌不落前端()
    {
        (IServiceProvider provider, _) = Build(CreateOptions(CreateProvider()), false);
        OpenIdConnectOptions options = GetOptions(provider, "oidc-corp");

        Assert.Equal("code", options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        // 回调必须落在 /api/auth 下：登录闸门对这一段放行，否则回跳会被 302 回登录页。
        Assert.Equal("/api/auth/oidc/corp/callback", options.CallbackPath.Value);
        Assert.Equal("/api/auth/oidc/corp/signed-out", options.SignedOutCallbackPath.Value);
        Assert.Equal("https://idp.example.com", options.Authority);
        Assert.Equal("dap-client", options.ClientId);
        Assert.Equal("secret-1", options.ClientSecret);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal("name", options.TokenValidationParameters.NameClaimType);
        Assert.True(options.GetClaimsFromUserInfoEndpoint);
        Assert.False(options.MapInboundClaims);
        Assert.Equal(["openid", "profile"], options.Scope.OrderBy(scope => scope, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(true, CookieSecurePolicy.Always)]
    [InlineData(false, CookieSecurePolicy.SameAsRequest)]
    public void 注册参数_协议Cookie安全策略随会话判定(bool requireSecureCookie, CookieSecurePolicy expected)
    {
        (IServiceProvider provider, _) = Build(CreateOptions(CreateProvider()), requireSecureCookie);

        OpenIdConnectOptions options = GetOptions(provider, "oidc-corp");

        // correlation/nonce Cookie 与平台会话 Cookie 用同一 Secure 判定，
        // 并保持 Lax：默认 None 已被现代浏览器拒绝。
        Assert.Equal(SameSiteMode.Lax, options.CorrelationCookie.SameSite);
        Assert.Equal(expected, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(expected, options.NonceCookie.SecurePolicy);
    }

    [Fact]
    public void 注册参数_空ClientSecret按公共客户端处理()
    {
        OidcProviderOptions publicClient = Clone(CreateProvider(), copy => copy.ClientSecret = " ");

        (IServiceProvider provider, _) = Build(CreateOptions(publicClient), false);

        OpenIdConnectOptions options = GetOptions(provider, "oidc-corp");

        // 留空表示公共客户端，只靠 PKCE，不写空字符串密钥。
        Assert.Null(options.ClientSecret);
    }
}

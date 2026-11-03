using System.Security.Claims;
using System.Text.RegularExpressions;

namespace DAP.Presentation.BlazorWeb.Security.External;

/// <summary>
/// OIDC 对接配置（docs/AUTH_DESIGN.md 11.2）。对应 appsettings 的 <c>Auth:Oidc</c> 节。
///
/// 多 IdP 靠配置扩展：每段 <see cref="OidcProviderOptions"/> 注册一个独立的认证 scheme
/// （<c>oidc-{code}</c>），新增一家只需要加一段配置，不动代码。
/// </summary>
public sealed class OidcOptions
{
    /// <summary>
    /// 获取或设置是否启用 OIDC 登录。默认关闭。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置身份提供方列表。
    /// </summary>
    public List<OidcProviderOptions> Providers { get; set; } = [];

    /// <summary>
    /// 返回已启用的提供方。
    /// </summary>
    /// <returns>提供方列表。</returns>
    public IReadOnlyList<OidcProviderOptions> EnabledProviders()
    {
        return Enabled
            ? Providers.Where(provider => provider.IsUsable()).ToList()
            : [];
    }

    /// <summary>
    /// 校验配置。返回空集合表示通过。
    /// </summary>
    /// <returns>中文错误说明集合。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (!Enabled)
        {
            return errors;
        }

        if (Providers.Count == 0)
        {
            errors.Add("Auth:Oidc:Enabled 为 true 但没有任何提供方配置。");
            return errors;
        }

        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);

        foreach (OidcProviderOptions provider in Providers)
        {
            errors.AddRange(provider.Validate());

            if (!codes.Add(provider.Code))
            {
                errors.Add($"Auth:Oidc:Providers 中的 Code「{provider.Code}」重复：它同时是回调地址的一部分。");
            }
        }

        return errors;
    }
}

/// <summary>
/// 单个 OIDC 提供方的配置。
/// </summary>
public sealed class OidcProviderOptions
{
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.Compiled);

    /// <summary>
    /// 获取或设置提供方编码。用于 scheme 名与回调路径，只能是小写字母、数字与短横线。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置登录页上显示的名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Authority（发现文档的基地址）。会自动拼接 <c>/.well-known/openid-configuration</c>。
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置客户端标识。
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置客户端密钥。留空表示公共客户端，只靠 PKCE。
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置请求的 scope。必须包含 <c>openid</c>。
    /// </summary>
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>
    /// 获取或设置姓名对应的 claim 名。
    /// </summary>
    public string NameClaim { get; set; } = "name";

    /// <summary>
    /// 获取或设置邮箱对应的 claim 名。
    /// </summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>
    /// 获取或设置登录账号对应的 claim 名。仅用于生成平台账号名，不参与唯一性判断。
    /// </summary>
    public string UserNameClaim { get; set; } = "preferred_username";

    /// <summary>
    /// 获取或设置映射到部门的 claim 名列表，按顺序取第一个有值的。
    /// </summary>
    public string[] DepartmentClaims { get; set; } = ["groups"];

    /// <summary>
    /// 获取或设置部门取值前缀。claim 值以此开头时先去掉，再与 <c>departments.external_code</c> 比对，
    /// 例如 <c>APP-DEPT-</c>。
    /// </summary>
    public string DepartmentPrefix { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置首次登录建号时分配的默认角色编码。默认 <c>viewer</c>。
    /// </summary>
    public string DefaultRoleCode { get; set; } = "viewer";

    /// <summary>
    /// 获取或设置是否要求元数据地址为 HTTPS。仅内网测试环境才应关闭。
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// 获取认证 scheme 名称。
    /// </summary>
    public string SchemeName => $"oidc-{Code}";

    /// <summary>
    /// 获取回调路径。必须落在 <c>/api/auth</c> 下，否则会被登录闸门拦成 302。
    /// </summary>
    public string CallbackPath => $"/api/auth/oidc/{Code}/callback";

    /// <summary>
    /// 判断该提供方是否具备可用配置。
    /// </summary>
    /// <returns>可用时 true。</returns>
    public bool IsUsable()
    {
        return !string.IsNullOrWhiteSpace(Code)
               && !string.IsNullOrWhiteSpace(Authority)
               && !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// 校验单个提供方配置。
    /// </summary>
    /// <returns>中文错误说明集合。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        var prefix = $"Auth:Oidc:Providers[{Code}]";

        if (!CodePattern.IsMatch(Code))
        {
            errors.Add($"{prefix}: Code 只能是小写字母、数字与短横线，长度 2~32。");
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out Uri? authority))
        {
            errors.Add($"{prefix}: Authority 必须是绝对地址。");
        }
        else if (RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{prefix}: Authority 必须是 https，或显式关闭 RequireHttpsMetadata。");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errors.Add($"{prefix}: ClientId 未配置。");
        }

        if (!Scopes.Any(scope => string.Equals(scope, "openid", StringComparison.Ordinal)))
        {
            errors.Add($"{prefix}: Scopes 必须包含 openid。");
        }

        return errors;
    }
}

/// <summary>
/// OIDC 登录后从 claims 里解析出的用户档案。
/// </summary>
/// <param name="ExternalId">唯一标识（sub）。</param>
/// <param name="UserName">派生出的平台账号名。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="Email">邮箱。</param>
/// <param name="DepartmentCode">部门外部标识，映射不到时为 null。</param>
public sealed record OidcUserProfile(
    string ExternalId,
    string UserName,
    string DisplayName,
    string? Email,
    string? DepartmentCode);

/// <summary>
/// 把 OIDC 的 claims 映射成平台用户档案。抽成纯函数是为了能单测「claim 名换了」「部门带前缀」
/// 「groups 多值」这些配置差异——它们在真实 IdP 上很难逐个复现。
/// </summary>
public static class OidcClaimsMapper
{
    /// <summary>
    /// 映射用户档案。**没有 sub 一律返回 null**：sub 是唯一键，
    /// 用 email 或 preferred_username 代替代价是被回收的账号会串到别人身上。
    /// </summary>
    /// <param name="claims">令牌中的 claims。</param>
    /// <param name="options">提供方配置。</param>
    /// <returns>用户档案；缺少 sub 时返回 null。</returns>
    public static OidcUserProfile? Map(IEnumerable<Claim> claims, OidcProviderOptions options)
    {
        List<Claim> list = [.. claims];

        var subject = ReadFirst(list, "sub")?.Trim();

        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var displayName = ReadFirst(list, options.NameClaim)?.Trim();
        var email = ReadFirst(list, options.EmailClaim)?.Trim();
        var rawUserName = ReadFirst(list, options.UserNameClaim)?.Trim();

        if (string.IsNullOrWhiteSpace(rawUserName))
        {
            // 邮箱的本地部分比完整邮箱更适合当账号名：完整邮箱会被 user_name 的
            // lower() 约束和长度限制挤掉，而且登录框里也不好看。
            rawUserName = email is { Length: > 0 } && email.Contains('@', StringComparison.Ordinal)
                ? email[..email.IndexOf('@', StringComparison.Ordinal)]
                : null;
        }

        var userName = DeriveUserName(rawUserName, subject);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userName;
        }

        return new OidcUserProfile(
            subject,
            userName,
            displayName!,
            string.IsNullOrWhiteSpace(email) ? null : email,
            ReadDepartment(list, options));
    }

    /// <summary>
    /// 派生平台账号名：小写、去空白，取不到时用 sub 的前 12 位兜底。
    /// </summary>
    /// <param name="rawUserName">原始账号名。</param>
    /// <param name="subject">sub。</param>
    /// <returns>账号名。</returns>
    public static string DeriveUserName(string? rawUserName, string subject)
    {
        var candidate = rawUserName?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            var tail = subject.Length > 12 ? subject[..12] : subject;
            candidate = $"oidc-{tail}".ToLowerInvariant();
        }

        return candidate.Length > 100 ? candidate[..100] : candidate;
    }

    /// <summary>
    /// 按配置顺序找第一个有值的部门 claim，去掉前缀后返回。
    ///
    /// 一个 claim 可能带多个值（AD / Okta 的 <c>groups</c> 一次给出全部组），
    /// 此时只取第一个值会把 <c>staff</c> 之流当成部门。配置了前缀就按前缀挑，
    /// 挑不到再退回第一个值——宁可让部门匹配失败进待处理表，也不要静默丢部门。
    /// </summary>
    /// <param name="claims">claim 列表。</param>
    /// <param name="options">提供方配置。</param>
    /// <returns>部门外部标识；映射不到时返回 null。</returns>
    public static string? ReadDepartment(IEnumerable<Claim> claims, OidcProviderOptions options)
    {
        List<Claim> list = [.. claims];

        foreach (var name in options.DepartmentClaims)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            List<string> values = [.. list
                .Where(claim => string.Equals(claim.Type, name, StringComparison.Ordinal))
                .Select(claim => claim.Value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)];

            if (values.Count == 0)
            {
                continue;
            }

            if (options.DepartmentPrefix.Length > 0)
            {
                var prefixed = values.FirstOrDefault(value =>
                    value.StartsWith(options.DepartmentPrefix, StringComparison.Ordinal));

                if (prefixed is not null)
                {
                    var stripped = prefixed[options.DepartmentPrefix.Length..];

                    if (!string.IsNullOrWhiteSpace(stripped))
                    {
                        return stripped;
                    }
                }
            }

            var first = values[0];

            if (options.DepartmentPrefix.Length > 0
                && first.StartsWith(options.DepartmentPrefix, StringComparison.Ordinal))
            {
                first = first[options.DepartmentPrefix.Length..];
            }

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return null;
    }

    private static string? ReadFirst(IReadOnlyList<Claim> claims, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, name, StringComparison.Ordinal))?.Value;
    }
}

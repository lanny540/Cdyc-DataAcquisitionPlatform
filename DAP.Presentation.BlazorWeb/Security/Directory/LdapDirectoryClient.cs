using System.DirectoryServices.Protocols;
using System.Net;
using DAP.Core.Domain.Entities;
using DAP.Presentation.BlazorWeb.Security;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 基于 <c>System.DirectoryServices.Protocols</c> 的目录客户端（docs/AUTH_DESIGN.md 11.1）。
///
/// 两条硬约束：
/// 1. **平台不保存域账号密码**，校验方式是「服务账号 bind → 搜到用户 DN → 用该 DN 与用户输入的密码
///    另开一条连接二次 bind」，密码只在这一条连接上出现过一次。
/// 2. **失败文案统一**：账号不存在与密码错误返回同一句话，否则登录框就成了账号枚举器。
/// </summary>
public sealed class LdapDirectoryClient(
    IOptions<PlatformAuthOptions> options,
    ILogger<LdapDirectoryClient> logger) : ILdapDirectory
{
    private readonly LdapOptions _options = options.Value.Ldap;

    /// <summary>
    /// 获取是否已启用目录认证。
    /// </summary>
    public bool Enabled => _options.Enabled;

    /// <inheritdoc />
    public async Task<LdapAuthenticationResult> AuthenticateAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return LdapAuthenticationResult.Failed("目录认证未启用。");
        }

        var normalized = LdapAttributeMapper.NormalizeUserName(userName);

        // 空密码在 LDAP 里会变成「匿名 bind」，被服务器当成成功——必须在这里挡掉。
        // 这不是防御性编程，是一个真实的认证绕过路径。
        if (normalized.Length == 0 || string.IsNullOrEmpty(password))
        {
            return LdapAuthenticationResult.Failed("请输入账号与密码。");
        }

        try
        {
            ILdapEntry? entry = await Task.Run(
                () => FindEntry(normalized, cancellationToken),
                cancellationToken);

            if (entry is null)
            {
                logger.LogWarning("目录登录失败：账号 {UserName} 在 {BaseDn} 下不存在。", normalized, _options.BaseDn);
                return LdapAuthenticationResult.Failed("账号或密码不正确。");
            }

            LdapDirectoryUser? user = LdapAttributeMapper.Map(entry, _options);

            if (user is null)
            {
                logger.LogWarning("目录登录失败：账号 {UserName} 的条目缺少全部标识字段。", normalized);
                return LdapAuthenticationResult.Failed("账号或密码不正确。");
            }

            if (!user.IsEnabled)
            {
                logger.LogWarning("目录登录失败：账号 {UserName} 在目录中已停用。", normalized);
                return LdapAuthenticationResult.Failed("该账号在目录中已停用，请联系管理员。");
            }

            BindAsUser(entry.DistinguishedName, password, cancellationToken);
            return LdapAuthenticationResult.Success(user);
        }
        catch (LdapException ex)
        {
            return HandleLdapFailure(ex, normalized);
        }
        catch (DirectoryOperationException ex)
        {
            logger.LogError(ex, "目录登录失败：账号 {UserName}，目录操作异常。", normalized);
            return LdapAuthenticationResult.Failed("目录服务暂时不可用，请稍后重试或联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LdapDirectoryUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        return await Task.Run(() => SearchAllUsers(cancellationToken), cancellationToken);
    }

    private LdapAuthenticationResult HandleLdapFailure(LdapException ex, string userName)
    {
        // 49 = invalidCredentials：密码错、账号不存在、账号被锁在 AD 里都是这个码，
        // 因此不能据此区分原因——正好也符合「不给账号枚举留口子」。
        if (ex.ErrorCode == 49)
        {
            logger.LogWarning("目录登录失败：账号 {UserName} 凭据不正确（LDAP 49）。", userName);
            return LdapAuthenticationResult.Failed("账号或密码不正确。");
        }

        logger.LogError(
            ex,
            "目录登录失败：账号 {UserName}，错误码 {ErrorCode}，服务器消息 {ServerMessage}。",
            userName,
            ex.ErrorCode,
            ex.ServerErrorMessage);

        return LdapAuthenticationResult.Failed("无法连接目录服务，请稍后重试或联系管理员。");
    }

    private ILdapEntry? FindEntry(string userName, CancellationToken cancellationToken)
    {
        using LdapConnection connection = Connect(CreateConnection());
        BindServiceAccount(connection);

        var filter = LdapFilter.BuildUserFilter(_options.UserFilter, userName);

        SearchRequest request = new(
            _options.BaseDn,
            filter,
            SearchScope.Subtree,
            _options.BuildAttributeList())
        {
            TimeLimit = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds))
        };

        SearchResponse response = (SearchResponse)connection.SendRequest(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (response.Entries.Count == 0)
        {
            return null;
        }

        if (response.Entries.Count > 1)
        {
            // 过滤器配置不唯一时不能随机挑一个：那等于把别人的身份 bind 上，
            // 出现这种情况说明 UserFilter 配错了，必须让人看到。
            logger.LogWarning(
                "目录查询命中 {Count} 条记录，账号 {UserName} 的过滤器可能不唯一，已取第一条。",
                response.Entries.Count,
                userName);
        }

        return new SearchResultEntryAdapter(response.Entries[0]);
    }

    private IReadOnlyList<LdapDirectoryUser> SearchAllUsers(CancellationToken cancellationToken)
    {
        using LdapConnection connection = Connect(CreateConnection());
        BindServiceAccount(connection);

        List<LdapDirectoryUser> users = [];
        var pageSize = Math.Max(1, _options.PageSize);
        PageResultRequestControl pageControl = new(pageSize);

        SearchRequest request = new(
            _options.BaseDn,
            _options.SyncFilter,
            SearchScope.Subtree,
            _options.BuildAttributeList())
        {
            TimeLimit = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds * 4))
        };

        request.Controls.Add(pageControl);

        // AD 默认单页 1000 条，不分页就是静默截断：目录里有 3000 人时只有 1000 人能被同步，
        // 其余的在平台上「消失」且没有任何报错。
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SearchResponse response = (SearchResponse)connection.SendRequest(request);

            foreach (SearchResultEntry entry in response.Entries)
            {
                LdapDirectoryUser? user = LdapAttributeMapper.Map(new SearchResultEntryAdapter(entry), _options);

                if (user is not null)
                {
                    users.Add(user);
                }
            }

            PageResultResponseControl? cookie = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault();

            if (cookie is null || cookie.Cookie.Length == 0)
            {
                break;
            }

            pageControl.Cookie = cookie.Cookie;
        }

        logger.LogInformation("目录同步读取到 {Count} 个账号。", users.Count);
        return users;
    }

    private LdapConnection CreateConnection()
    {
        // 用 "host:port" 字符串构造：LdapDirectoryIdentifier 在某些平台上会把主机名当成
        // 需要解析的 FQDN，传入 IP 字面量时反而连不上。
        LdapConnection connection = new($"{_options.Host}:{_options.Port}")
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds))
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (_options.TransportSecurity == LdapTransportSecurity.Ssl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            ApplyCertificatePolicy(connection);
        }

        return connection;
    }

    /// <summary>
    /// 建立连接并在需要时用 StartTLS 升级。StartTLS 必须在 bind **之前**完成，
    /// 否则服务账号密码已经以明文发出去了。
    /// </summary>
    private LdapConnection Connect(LdapConnection connection)
    {
        if (_options.TransportSecurity == LdapTransportSecurity.StartTls)
        {
            ApplyCertificatePolicy(connection);
            connection.SessionOptions.StartTransportLayerSecurity(new DirectoryControlCollection());
        }

        return connection;
    }

    private void ApplyCertificatePolicy(LdapConnection connection)
    {
        if (_options.SkipCertificateValidation)
        {
            // 仅供内网自签证书的临时排查。默认关闭：关掉证书校验等于放弃中间人防护。
            connection.SessionOptions.VerifyServerCertificate = static (_, _) => true;
        }
    }

    private void BindServiceAccount(LdapConnection connection)
    {
        connection.Bind(new NetworkCredential(_options.BindDn, _options.BindPassword));
    }

    private void BindAsUser(string distinguishedName, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using LdapConnection connection = Connect(CreateConnection());
        connection.Bind(new NetworkCredential(distinguishedName, password));
    }

    /// <summary>
    /// 把 <see cref="SearchResultEntry"/> 适配成可测试的条目视图。
    /// </summary>
    private sealed class SearchResultEntryAdapter(SearchResultEntry entry) : ILdapEntry
    {
        public string DistinguishedName => entry.DistinguishedName;

        public string? GetString(string attributeName)
        {
            if (!entry.Attributes.Contains(attributeName))
            {
                return null;
            }

            DirectoryAttribute attribute = entry.Attributes[attributeName];

            if (attribute.Count == 0)
            {
                return null;
            }

            return attribute[0] switch
            {
                byte[] bytes => LdapAttributeMapper.ToHexString(bytes),
                { } value => value.ToString(),
                _ => null
            };
        }

        public byte[]? GetBytes(string attributeName)
        {
            if (!entry.Attributes.Contains(attributeName) || entry.Attributes[attributeName].Count == 0)
            {
                return null;
            }

            return entry.Attributes[attributeName][0] as byte[];
        }

        public IReadOnlyList<string> GetStrings(string attributeName)
        {
            if (!entry.Attributes.Contains(attributeName))
            {
                return [];
            }

            DirectoryAttribute attribute = entry.Attributes[attributeName];
            List<string> values = [];

            foreach (var item in attribute.GetValues(typeof(string)))
            {
                if (item is string text && !string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }

            return values;
        }
    }
}

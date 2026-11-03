namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// LDAP 传输加密方式（docs/AUTH_DESIGN.md 11.1）。
/// </summary>
public enum LdapTransportSecurity
{
    /// <summary>
    /// 明文。仅允许本机测试目录使用，其他主机在启动时拒绝。
    /// </summary>
    None,

    /// <summary>
    /// LDAPS：连上即 TLS，默认端口 636。
    /// </summary>
    Ssl,

    /// <summary>
    /// 先以明文建连，再用 StartTLS 升级，端口通常是 389。
    /// </summary>
    StartTls
}

/// <summary>
/// LDAP / AD 对接配置（docs/AUTH_DESIGN.md 11.1）。对应 appsettings 的 <c>Auth:Ldap</c> 节。
///
/// 默认关闭：未配置目录时应用不应因为一个空配置就去连 636 端口。
/// 打开后用户名密码走「服务账号 bind → 按账号搜到用户 DN → 用该 DN 与用户密码二次 bind」，
/// 平台不保存域账号密码。
/// </summary>
public sealed class LdapOptions
{
    /// <summary>
    /// 获取或设置是否启用目录认证与同步。默认关闭。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置目录服务器地址。
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置目录服务器端口。LDAPS 默认 636，StartTLS 用 389。
    /// </summary>
    public int Port { get; set; } = 636;

    /// <summary>
    /// 获取或设置传输加密方式。生产环境必须是 <see cref="LdapTransportSecurity.Ssl"/>
    /// 或 <see cref="LdapTransportSecurity.StartTls"/>。
    /// </summary>
    public LdapTransportSecurity TransportSecurity { get; set; } = LdapTransportSecurity.Ssl;

    /// <summary>
    /// 获取或设置是否跳过服务端证书校验。仅用于内网自签证书的临时排查，默认关闭。
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    /// <summary>
    /// 获取或设置服务账号 DN（只读权限即可）。
    /// </summary>
    public string BindDn { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置服务账号密码。
    /// </summary>
    public string BindPassword { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置搜索起点 DN，例如 <c>DC=example,DC=com</c>。
    /// </summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置按账号查询用户的过滤器模板，<c>{0}</c> 为转义后的账号。
    /// </summary>
    public string UserFilter { get; set; } = "(sAMAccountName={0})";

    /// <summary>
    /// 获取或设置同步全量用户时使用的过滤器。
    /// </summary>
    public string SyncFilter { get; set; } = "(objectClass=person)";

    /// <summary>
    /// 获取或设置作为登录账号的属性名。同时用于派生平台账号名。
    /// </summary>
    public string LoginAttribute { get; set; } = "sAMAccountName";

    /// <summary>
    /// 获取或设置唯一标识属性名（AD 为 objectGUID）。缺失时回退登录账号。
    /// </summary>
    public string UniqueIdAttribute { get; set; } = "objectGUID";

    /// <summary>
    /// 获取或设置姓名属性名。
    /// </summary>
    public string DisplayNameAttribute { get; set; } = "displayName";

    /// <summary>
    /// 获取或设置邮箱属性名。
    /// </summary>
    public string MailAttribute { get; set; } = "mail";

    /// <summary>
    /// 获取或设置部门属性名。
    /// </summary>
    public string DepartmentAttribute { get; set; } = "department";

    /// <summary>
    /// 获取或设置工号属性名，写入 <c>external_identities.employee_no</c>，本期不参与鉴权。
    /// </summary>
    public string EmployeeNoAttribute { get; set; } = "employeeID";

    /// <summary>
    /// 获取或设置表示账号启停的属性名。AD 用 <c>userAccountControl</c>；无此属性时视为启用。
    /// </summary>
    public string DisabledFlagAttribute { get; set; } = "userAccountControl";

    /// <summary>
    /// 获取或设置禁用位掩码。AD 的 ACCOUNTDISABLE 为 2。
    /// </summary>
    public int DisabledFlagMask { get; set; } = 2;

    /// <summary>
    /// 获取或设置部门属性的回退层级：属性为空时从 DN 的 OU 层级取名称，
    /// 1 表示离叶子最近的 OU。0 表示不启用回退。
    /// </summary>
    public int DepartmentOuLevel { get; set; }

    /// <summary>
    /// 获取或设置分页大小。AD 默认单页 1000，不分页会让大目录静默截断。
    /// </summary>
    public int PageSize { get; set; } = 500;

    /// <summary>
    /// 获取或设置定时全量同步的间隔（分钟）。设为 0 或负数表示只允许手工触发。
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 获取或设置目录账号首次建号时分配的默认角色编码。
    /// </summary>
    public string DefaultRoleCode { get; set; } = "viewer";

    /// <summary>
    /// 获取或设置连接与服务端操作超时（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 校验配置是否可用于连接。返回空集合表示通过。
    ///
    /// 这里刻意返回完整列表而不是遇到第一个错误就返回：
    /// 部署时一次把缺的项全列出来，比改一项重启一次要好。
    /// </summary>
    /// <returns>中文错误说明集合。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (!Enabled)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add("Auth:Ldap:Host 未配置。");
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add("Auth:Ldap:Port 必须在 1 到 65535 之间。");
        }

        if (string.IsNullOrWhiteSpace(BindDn))
        {
            errors.Add("Auth:Ldap:BindDn 未配置：需要一个只读服务账号来检索用户。");
        }

        if (string.IsNullOrWhiteSpace(BaseDn))
        {
            errors.Add("Auth:Ldap:BaseDn 未配置：不知道从哪个节点开始搜索。");
        }

        if (!UserFilter.Contains("{0}", StringComparison.Ordinal))
        {
            errors.Add("Auth:Ldap:UserFilter 必须包含 {0} 占位符，否则查不到对应用户。");
        }

        if (PageSize < 1)
        {
            errors.Add("Auth:Ldap:PageSize 必须大于 0。");
        }

        if (TimeoutSeconds < 1)
        {
            errors.Add("Auth:Ldap:TimeoutSeconds 必须大于 0。");
        }

        // 明文只允许出现在本机测试目录上：域账号密码以明文过网，等同于把口令交给同网段任何人。
        // 判定用 loopback 而不是「开发环境」标志，因为标志会被误带到生产配置里。
        if (TransportSecurity == LdapTransportSecurity.None && !IsLoopbackHost(Host))
        {
            errors.Add(
                "Auth:Ldap:TransportSecurity 为 None，域账号密码会以明文经过网络。"
                + "除本机测试目录外必须使用 Ssl 或 StartTls。");
        }

        return errors;
    }

    /// <summary>
    /// 判断主机是否为本机地址。
    /// </summary>
    /// <param name="host">主机名或地址。</param>
    /// <returns>本机时返回 true。</returns>
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var value = host.Trim();

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(value, out System.Net.IPAddress? address)
               && System.Net.IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// 构造用于读取的属性名集合，按配置去重。
    /// </summary>
    /// <returns>属性名数组。</returns>
    public string[] BuildAttributeList()
    {
        return new[]
            {
                LoginAttribute,
                UniqueIdAttribute,
                DisplayNameAttribute,
                MailAttribute,
                DepartmentAttribute,
                EmployeeNoAttribute,
                DisabledFlagAttribute
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

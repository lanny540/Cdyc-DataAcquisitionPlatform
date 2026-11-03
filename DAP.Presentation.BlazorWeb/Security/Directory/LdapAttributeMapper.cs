using System.Globalization;
using System.Text;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// LDAP 过滤器与属性的纯函数工具。
///
/// **过滤器必须转义**：账号来自登录框，未转义时一个 <c>*</c> 就能让 <c>(sAMAccountName=*)</c>
/// 匹配到目录里的第一个人，等于拿别人的身份做二次 bind——这是 LDAP 注入，
/// 也是本项目里唯一一处「用户输入直接进协议」的地方。
/// </summary>
public static class LdapFilter
{
    /// <summary>
    /// 按 RFC 4515 转义过滤器中的字面值。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>转义后的值。</returns>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append(@"\5c");
                    break;
                case '*':
                    builder.Append(@"\2a");
                    break;
                case '(':
                    builder.Append(@"\28");
                    break;
                case ')':
                    builder.Append(@"\29");
                    break;
                case '\0':
                    builder.Append(@"\00");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 把账号填进过滤器模板。模板中除 <c>{0}</c> 外的内容按原样保留。
    /// </summary>
    /// <param name="template">过滤器模板，例如 <c>(sAMAccountName={0})</c>。</param>
    /// <param name="userName">登录账号。</param>
    /// <returns>完整过滤器。</returns>
    public static string BuildUserFilter(string template, string? userName)
    {
        return template.Replace("{0}", Escape(userName), StringComparison.Ordinal);
    }
}

/// <summary>
/// 把目录条目映射成平台认识的用户视图。
/// </summary>
public static class LdapAttributeMapper
{
    /// <summary>
    /// 把 DN 中的 OU 层级解析出来。DN 从左到右由近及远，
    /// <c>level = 1</c> 取离叶子最近的那一级 OU。
    ///
    /// 用途是部门属性的回退：有些目录不填 <c>department</c>，但组织信息在 OU 上。
    /// </summary>
    /// <param name="distinguishedName">条目 DN。</param>
    /// <param name="level">1 起的层级；小于 1 表示不启用。</param>
    /// <returns>OU 名称；解析不到时返回 null。</returns>
    public static string? ResolveOuName(string? distinguishedName, int level)
    {
        if (level < 1 || string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        List<string> ous = [];

        foreach (var part in SplitDn(distinguishedName))
        {
            if (part.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
            {
                ous.Add(part[3..]);
            }
        }

        return ous.Count >= level ? ous[level - 1] : null;
    }

    /// <summary>
    /// 按 DN 语法拆分 RDN 列表，识别 <c>\,</c> 与 <c>\\</c> 转义。
    /// 不处理跨 RDN 的多值（<c>+</c>），因为 OU 不会那样写。
    /// </summary>
    /// <param name="distinguishedName">DN。</param>
    /// <returns>RDN 列表。</returns>
    public static IReadOnlyList<string> SplitDn(string distinguishedName)
    {
        List<string> parts = [];
        StringBuilder current = new();

        for (var index = 0; index < distinguishedName.Length; index++)
        {
            var character = distinguishedName[index];

            if (character == '\\' && index + 1 < distinguishedName.Length)
            {
                current.Append(character);
                current.Append(distinguishedName[index + 1]);
                index++;
                continue;
            }

            if (character == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString().Trim());
        }

        return parts;
    }

    /// <summary>
    /// 把 objectGUID 这类二进制唯一标识转成十六进制串。已经是字符串的属性原样返回。
    /// </summary>
    /// <param name="bytes">字节数组。</param>
    /// <returns>小写十六进制串。</returns>
    public static string ToHexString(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 映射一条目录条目。缺失唯一标识时回退登录账号——回退值必须稳定，
    /// 否则每次同步都会当成新账号重复建号。
    /// </summary>
    /// <param name="entry">目录条目。</param>
    /// <param name="options">目录配置。</param>
    /// <returns>用户视图；连登录账号都取不到时返回 null。</returns>
    public static LdapDirectoryUser? Map(ILdapEntry entry, LdapOptions options)
    {
        var userName = NormalizeUserName(entry.GetString(options.LoginAttribute));

        var externalId = ReadExternalId(entry, options);

        if (externalId.Length == 0)
        {
            externalId = userName;
        }

        if (externalId.Length == 0)
        {
            return null;
        }

        var displayName = entry.GetString(options.DisplayNameAttribute)?.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userName.Length > 0 ? userName : externalId;
        }

        var department = entry.GetString(options.DepartmentAttribute)?.Trim();

        if (string.IsNullOrWhiteSpace(department))
        {
            department = ResolveOuName(entry.DistinguishedName, options.DepartmentOuLevel);
        }

        var email = entry.GetString(options.MailAttribute)?.Trim();
        var employeeNo = entry.GetString(options.EmployeeNoAttribute)?.Trim();

        return new LdapDirectoryUser(
            externalId,
            userName,
            displayName!,
            string.IsNullOrWhiteSpace(email) ? null : email,
            string.IsNullOrWhiteSpace(department) ? null : department,
            string.IsNullOrWhiteSpace(employeeNo) ? null : employeeNo,
            ReadEnabled(entry, options));
    }

    /// <summary>
    /// 规范化登录账号：去空白、转小写。库里的 <c>user_name</c> 有
    /// <c>user_name = lower(user_name)</c> 的约束，这里不转就写不进去。
    /// </summary>
    /// <param name="userName">原始账号。</param>
    /// <returns>规范化后的账号。</returns>
    public static string NormalizeUserName(string? userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? string.Empty : userName.Trim().ToLowerInvariant();
    }

    private static string ReadExternalId(ILdapEntry entry, LdapOptions options)
    {
        var raw = entry.GetString(options.UniqueIdAttribute)?.Trim();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var bytes = entry.GetBytes(options.UniqueIdAttribute);

        return bytes is { Length: > 0 } ? ToHexString(bytes) : string.Empty;
    }

    private static bool ReadEnabled(ILdapEntry entry, LdapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DisabledFlagAttribute))
        {
            return true;
        }

        var raw = entry.GetString(options.DisabledFlagAttribute);

        // 属性缺失（OpenLDAP 没有 userAccountControl）时不能当成停用：
        // 那会让整个目录的账号在第一次同步后全部失效。
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
        {
            return true;
        }

        return (flags & options.DisabledFlagMask) == 0;
    }
}

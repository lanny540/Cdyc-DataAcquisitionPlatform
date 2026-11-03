namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 表示目录中的一条记录。抽出这一层是为了让属性映射可以脱离 LDAP 客户端单独测试：
/// 真实实现包装 <c>System.DirectoryServices.Protocols.SearchResultEntry</c>，
/// 单元测试用内存实现即可覆盖「属性缺失」「多值」「DN 回退」这些分支。
/// </summary>
public interface ILdapEntry
{
    /// <summary>
    /// 获取条目的 DN。
    /// </summary>
    string DistinguishedName { get; }

    /// <summary>
    /// 读取单值属性的字符串形式。属性不存在或为空时返回 null。
    /// </summary>
    /// <param name="attributeName">属性名。</param>
    /// <returns>属性值。</returns>
    string? GetString(string attributeName);

    /// <summary>
    /// 读取二进制属性（如 objectGUID）。属性不存在时返回 null。
    /// </summary>
    /// <param name="attributeName">属性名。</param>
    /// <returns>字节数组。</returns>
    byte[]? GetBytes(string attributeName);

    /// <summary>
    /// 读取多值属性的全部字符串值。
    /// </summary>
    /// <param name="attributeName">属性名。</param>
    /// <returns>值列表，属性不存在时为空。</returns>
    IReadOnlyList<string> GetStrings(string attributeName);
}

/// <summary>
/// 目录侧的统一用户视图。平台只认这些字段，不向上传递 LDAP 的原始属性字典。
/// </summary>
/// <param name="ExternalId">唯一标识（AD 为 objectGUID 的十六进制串）。</param>
/// <param name="UserName">登录账号。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="Email">邮箱。</param>
/// <param name="DepartmentExternalCode">部门外部标识，对应 <c>departments.external_code</c>。</param>
/// <param name="EmployeeNo">工号，本期不参与鉴权。</param>
/// <param name="IsEnabled">目录中是否启用。</param>
public sealed record LdapDirectoryUser(
    string ExternalId,
    string UserName,
    string DisplayName,
    string? Email,
    string? DepartmentExternalCode,
    string? EmployeeNo,
    bool IsEnabled);

/// <summary>
/// 表示一次域账号校验的结果。
/// </summary>
/// <param name="Succeeded">是否通过。</param>
/// <param name="ErrorMessage">失败原因，成功时为空；文案已是中文，可直接展示。</param>
/// <param name="User">通过时的目录用户视图。</param>
public sealed record LdapAuthenticationResult(bool Succeeded, string? ErrorMessage, LdapDirectoryUser? User)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="user">目录用户视图。</param>
    /// <returns>校验结果。</returns>
    public static LdapAuthenticationResult Success(LdapDirectoryUser user) => new(true, null, user);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="message">中文失败原因。</param>
    /// <returns>校验结果。</returns>
    public static LdapAuthenticationResult Failed(string message) => new(false, message, null);
}

/// <summary>
/// 目录服务抽象。抽出来有两个目的：登录链路可以脱离真实目录做单元测试，
/// 以及将来接其他目录（非 AD）时不必改登录与同步的代码。
/// </summary>
public interface ILdapDirectory
{
    /// <summary>
    /// 校验域账号密码。实现方**不得**保存用户密码，只做一次绑定校验。
    /// </summary>
    /// <param name="userName">登录账号。</param>
    /// <param name="password">明文密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果。</returns>
    Task<LdapAuthenticationResult> AuthenticateAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取全部目录账号，供定时全量同步使用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目录账号列表。</returns>
    Task<IReadOnlyList<LdapDirectoryUser>> ListUsersAsync(CancellationToken cancellationToken = default);
}

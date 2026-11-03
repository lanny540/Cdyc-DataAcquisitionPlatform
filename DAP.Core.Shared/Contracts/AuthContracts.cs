namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示当前登录用户的完整授权视图。一次返回，前端不再分别查权限。
/// </summary>
/// <param name="Id">用户标识。</param>
/// <param name="UserName">登录账号。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="DepartmentName">部门名称，未归属部门时为空。</param>
/// <param name="Roles">角色编码集合。</param>
/// <param name="Domains">可见职能域编码集合。超级管理员为全部已启用域。</param>
/// <param name="PageLevel">页面等级，取全部已启用角色的最大值。</param>
/// <param name="Permissions">权限点编码集合。</param>
/// <param name="MustChangePassword">是否必须先修改密码。</param>
/// <param name="IsSuperAdmin">是否超级管理员。为 true 时域与页面等级均短路。</param>
/// <param name="IsBoardTerminal">
/// 是否车间只读终端会话（IP 白名单签发）。这类会话没有 <see cref="Id"/>，
/// 但 WASM 端必须能把它与「未登录」区分开，否则免登录看板在 WASM 缓存命中后会被踢回登录页。
/// </param>
public sealed record AuthUserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    string? DepartmentName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Domains,
    int PageLevel,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    bool IsSuperAdmin,
    bool IsBoardTerminal = false);

/// <summary>
/// 表示修改密码请求。
/// </summary>
/// <param name="CurrentPassword">当前密码。首次登录强制改密时仍需提供。</param>
/// <param name="NewPassword">新密码。</param>
public sealed record ChangePasswordRequestDto(string CurrentPassword, string NewPassword);

/// <summary>
/// 表示一次认证相关操作的结果。
/// </summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="ErrorMessage">失败原因，成功时为空。</param>
public sealed record AuthOperationResultDto(bool Succeeded, string? ErrorMessage);

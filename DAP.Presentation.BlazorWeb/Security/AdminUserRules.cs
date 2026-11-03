using System.Text.RegularExpressions;
using DAP.Core.Shared.Catalog;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 用户管理的纯判定规则（docs/AUTH_DESIGN.md 9.3）。
///
/// 抽成静态方法是为了让「部门边界」「角色授予上限」这两条安全约束可以被单测直接覆盖，
/// 而不必为了测一个 if 去搭数据库。判定与执行分离：规则只回答「能不能」，
/// 服务负责落库与审计。
/// </summary>
public static partial class AdminUserRules
{
    /// <summary>
    /// 账号名允许的字符集：小写字母、数字、点、下划线、连字符，3–32 位，以字母或数字开头。
    /// 统一小写，避免「Admin 与 admin 是两个账号」这类登录歧义。
    /// </summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();

    /// <summary>
    /// 判断账号名是否合法。调用前应先用 <c>PlatformAuthService.NormalizeUserName</c> 归一化。
    /// </summary>
    /// <param name="userName">归一化后的账号名。</param>
    /// <returns>合法时返回 true。</returns>
    public static bool IsValidUserName(string? userName)
    {
        return !string.IsNullOrWhiteSpace(userName) && UserNamePattern().IsMatch(userName);
    }

    /// <summary>
    /// 判断某个角色是否可以由当前操作者授予。
    ///
    /// 规则：超级管理员角色永远不经用户管理分配（只能「移交超管」）；
    /// 部门管理员不能授予部门管理员及以上（防自我提权链）；其余角色均可。
    /// </summary>
    /// <param name="actorIsSuperAdmin">操作者是否超级管理员。</param>
    /// <param name="roleCode">待授予角色编码。</param>
    /// <returns>可授予时返回 true。</returns>
    public static bool IsAssignableRoleCode(bool actorIsSuperAdmin, string roleCode)
    {
        if (string.Equals(roleCode, AuthorizationCatalog.SuperAdminRole, StringComparison.Ordinal))
        {
            return false;
        }

        if (actorIsSuperAdmin)
        {
            return true;
        }

        return roleCode is AuthorizationCatalog.ViewerRole or AuthorizationCatalog.TechnicianRole;
    }

    /// <summary>
    /// 判断操作者能否管理目标账号，不能管理时返回中文原因。
    ///
    /// 「本部门」= department_id 精确相等，不递归：组织模型没有层级，
    /// LDAP 侧同样是一个用户一个部门、部门之间平级。
    /// </summary>
    /// <param name="actorIsSuperAdmin">操作者是否超级管理员。</param>
    /// <param name="actorDepartmentId">操作者部门；部门管理员未归属部门时为空。</param>
    /// <param name="targetDepartmentId">目标账号部门。</param>
    /// <returns>可管理时返回 null，否则返回中文原因。</returns>
    public static string? DescribeScopeViolation(
        bool actorIsSuperAdmin,
        Guid? actorDepartmentId,
        Guid? targetDepartmentId)
    {
        if (actorIsSuperAdmin)
        {
            return null;
        }

        if (actorDepartmentId is null)
        {
            return "当前账号未归属部门，无法管理用户。";
        }

        return targetDepartmentId == actorDepartmentId
            ? null
            : "只能管理本部门的账号。";
    }
}

using DAP.Core.Domain.Entities;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 同步动作。
/// </summary>
public enum DirectorySyncAction
{
    /// <summary>
    /// 本地不存在，需要建号。
    /// </summary>
    Create,

    /// <summary>
    /// 本地存在且有可同步字段变化。
    /// </summary>
    Update,

    /// <summary>
    /// 目录中已停用，需要停用本地账号。
    /// </summary>
    Disable,

    /// <summary>
    /// 无变化。**不写库**——每小时一次的全量同步不该把所有行都刷一遍 updated_at。
    /// </summary>
    Unchanged
}

/// <summary>
/// 本地账号在同步视角下的快照。只带同步会碰的字段，避免把整个聚合根拖进判定逻辑。
/// </summary>
/// <param name="UserId">用户标识。</param>
/// <param name="UserName">登录账号。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="Email">邮箱。</param>
/// <param name="DepartmentId">部门标识。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="IsSuperAdmin">是否超级管理员。</param>
public sealed record DirectoryAccountSnapshot(
    Guid UserId,
    string UserName,
    string DisplayName,
    string? Email,
    Guid? DepartmentId,
    bool IsEnabled,
    bool IsSuperAdmin);

/// <summary>
/// 一条目录记录对应的同步决定。
/// </summary>
/// <param name="Action">动作。</param>
/// <param name="DisplayName">目标姓名。</param>
/// <param name="Email">目标邮箱。</param>
/// <param name="DepartmentId">目标部门。</param>
/// <param name="IsEnabled">目标启用状态。</param>
/// <param name="ChangedFields">变化的字段名，写审计用。</param>
public sealed record DirectorySyncDecision(
    DirectorySyncAction Action,
    string DisplayName,
    string? Email,
    Guid? DepartmentId,
    bool IsEnabled,
    IReadOnlyList<string> ChangedFields)
{
    /// <summary>
    /// 获取是否需要写库。
    /// </summary>
    public bool RequiresWrite => Action != DirectorySyncAction.Unchanged;
}

/// <summary>
/// 同步判定。抽成纯函数的原因：这里每一条规则出错都会造成生产事故
/// （把手工角色冲掉、把超管停用、每次同步都重建账号），必须能被单测钉住。
/// </summary>
public static class DirectorySyncPlanner
{
    /// <summary>
    /// 计算同步决定。
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item>姓名、邮箱、部门以目录为准，但**目录为空时不清空本地已有值**——目录里少填一个字段
    /// 不该让平台上的姓名变成空白；</item>
    /// <item>启停以目录为准（目录是外部账号存在性的权威来源），但**超级管理员不会被停用**，
    /// 否则一次目录误操作就能把平台锁死；</item>
    /// <item>角色不在这里处理：手工分配的角色永远由人来改。</item>
    /// </list>
    /// </summary>
    /// <param name="record">目录记录。</param>
    /// <param name="local">本地快照；不存在时为 null。</param>
    /// <param name="mappedDepartmentId">按 external_code 匹配到的部门；匹配不到为 null。</param>
    /// <returns>同步决定。</returns>
    public static DirectorySyncDecision Decide(
        LdapDirectoryUser record,
        DirectoryAccountSnapshot? local,
        Guid? mappedDepartmentId)
    {
        if (local is null)
        {
            return new DirectorySyncDecision(
                DirectorySyncAction.Create,
                record.DisplayName,
                record.Email,
                mappedDepartmentId,
                record.IsEnabled,
                ["created"]);
        }

        List<string> changed = [];

        var displayName = local.DisplayName;

        if (!string.IsNullOrWhiteSpace(record.DisplayName)
            && !string.Equals(record.DisplayName, local.DisplayName, StringComparison.Ordinal))
        {
            displayName = record.DisplayName;
            changed.Add("display_name");
        }

        var email = local.Email;

        if (!string.IsNullOrWhiteSpace(record.Email)
            && !string.Equals(record.Email, local.Email, StringComparison.OrdinalIgnoreCase))
        {
            email = record.Email;
            changed.Add("email");
        }

        var departmentId = local.DepartmentId;

        // 匹配不到部门时保留原归属：目录里部门字段写错一次就把人踢出所有业务域，代价太大。
        if (mappedDepartmentId is { } mapped && mapped != local.DepartmentId)
        {
            departmentId = mapped;
            changed.Add("department_id");
        }

        var isEnabled = local.IsEnabled;

        if (!record.IsEnabled && local.IsEnabled && !local.IsSuperAdmin)
        {
            isEnabled = false;
            changed.Add("is_enabled");
        }
        else if (record.IsEnabled && !local.IsEnabled)
        {
            isEnabled = true;
            changed.Add("is_enabled");
        }

        var action = changed.Count == 0
            ? DirectorySyncAction.Unchanged
            : isEnabled == false && local.IsEnabled
                ? DirectorySyncAction.Disable
                : DirectorySyncAction.Update;

        return new DirectorySyncDecision(action, displayName, email, departmentId, isEnabled, changed);
    }
}

/// <summary>
/// 由目录记录构造本地账号与身份绑定。登录时建号与定时同步共用同一份构造逻辑，
/// 否则两条路径建出来的账号会在字段上慢慢分叉。
/// </summary>
public static class DirectoryUserFactory
{
    /// <summary>
    /// 从目录记录派生平台账号名：小写、去空白，空值时用外部标识兜底。
    /// </summary>
    /// <param name="record">目录记录。</param>
    /// <returns>账号名。</returns>
    public static string DeriveUserName(LdapDirectoryUser record)
    {
        var candidate = LdapAttributeMapper.NormalizeUserName(record.UserName);

        if (candidate.Length == 0)
        {
            var tail = record.ExternalId.Length > 12 ? record.ExternalId[..12] : record.ExternalId;
            candidate = $"ldap-{tail}".ToLowerInvariant();
        }

        return candidate.Length > 100 ? candidate[..100] : candidate;
    }

    /// <summary>
    /// 创建本地账号。外部账号不设本地密码：<c>password_hash</c> 为空即表示「只能用目录登录」。
    /// </summary>
    /// <param name="record">目录记录。</param>
    /// <param name="departmentId">部门标识。</param>
    /// <param name="now">当前时间。</param>
    /// <returns>待写入的账号。</returns>
    public static AppUser CreateUser(LdapDirectoryUser record, Guid? departmentId, DateTimeOffset now)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = DeriveUserName(record),
            DisplayName = string.IsNullOrWhiteSpace(record.DisplayName)
                ? DeriveUserName(record)
                : record.DisplayName,
            DepartmentId = departmentId,
            Email = record.Email,
            IsEnabled = record.IsEnabled,
            MustChangePassword = false,
            PasswordHash = null,
            AccessFailedCount = 0,
            LastLoginAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 创建外部身份绑定。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="record">目录记录。</param>
    /// <param name="provider">提供方，ldap 或 oidc。</param>
    /// <param name="now">当前时间。</param>
    /// <returns>待写入的身份绑定。</returns>
    public static ExternalIdentity CreateIdentity(
        Guid userId,
        LdapDirectoryUser record,
        string provider,
        DateTimeOffset now)
    {
        return new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            ExternalId = record.ExternalId,
            EmployeeNo = record.EmployeeNo,
            LastSyncedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}

namespace DAP.Core.Domain.Entities;

/// <summary>
/// 表示一个部门。部门是用户的唯一归属，也是「职能域」授权的授予对象。
///
/// 刻意不设上级部门字段：已确认部门之间平级，与 LDAP 侧的组织模型一致。
/// </summary>
public sealed class Department
{
    /// <summary>
    /// 获取或设置部门标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置部门编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置部门名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置外部目录中的部门标识（LDAP 的 department 属性或 OIDC 的组名）。
    /// </summary>
    public string? ExternalCode { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置部门内的用户集合。
    /// </summary>
    public ICollection<AppUser> Users { get; set; } = [];

    /// <summary>
    /// 获取或设置部门被授予的职能域集合。
    /// </summary>
    public ICollection<DepartmentDomainGrant> DomainGrants { get; set; } = [];
}

/// <summary>
/// 表示一个平台账号。账号必须是员工个人，不得为班组设共享账号。
/// </summary>
public sealed class AppUser
{
    /// <summary>
    /// 获取或设置用户标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置登录账号。
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置姓名。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属部门标识。单值可空：外部账号未同步到部门时为空，此时不可见任何业务域。
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// 获取或设置邮箱。
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置是否必须先修改密码。首次登录强制改密。
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// 获取或设置密码哈希。外部账号（LDAP / OIDC）为空，不参与本地校验。
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// 获取或设置连续登录失败次数。
    /// </summary>
    public int AccessFailedCount { get; set; }

    /// <summary>
    /// 获取或设置锁定截止时间，为空表示未锁定。
    /// </summary>
    public DateTimeOffset? LockoutEndAt { get; set; }

    /// <summary>
    /// 获取或设置最近一次登录成功时间。
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置所属部门。
    /// </summary>
    public Department? Department { get; set; }

    /// <summary>
    /// 获取或设置用户的角色集合。
    /// </summary>
    public ICollection<UserRole> UserRoles { get; set; } = [];

    /// <summary>
    /// 获取或设置外部身份绑定集合。
    /// </summary>
    public ICollection<ExternalIdentity> ExternalIdentities { get; set; } = [];
}

/// <summary>
/// 表示账号在外部目录（LDAP / OIDC）中的身份绑定。
/// </summary>
public sealed class ExternalIdentity
{
    /// <summary>
    /// 获取或设置标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置用户标识。
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置外部提供方，例如 ldap、oidc。
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置外部唯一标识（LDAP 的 objectGUID、OIDC 的 sub）。
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置工号。本期不参与鉴权，为人级排班预留唯一的关联键。
    /// </summary>
    public string? EmployeeNo { get; set; }

    /// <summary>
    /// 获取或设置最近一次同步时间。
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// 表示一个职能域（业务板块）。域决定「能不能进这个板块」，是页面可见性的授予单位。
/// </summary>
public sealed class BusinessDomain
{
    /// <summary>
    /// 获取或设置域标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置域编码。创建后不可改，授权数据与代码都引用它。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置图标名称。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 获取或设置顶栏排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否为系统域。系统域不参与授权矩阵，固定归超级管理员。
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 获取或设置是否启用。停用后域内页面全部不可见，但授权记录保留。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置空域是否对普通用户展示。默认关闭，避免用户点进空白板块。
    /// </summary>
    public bool VisibleWhenEmpty { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置域内页面集合。
    /// </summary>
    public ICollection<DomainPage> Pages { get; set; } = [];
}

/// <summary>
/// 表示域内的一个页面。路由由代码的 @page 决定，本表只记录归属与等级。
/// </summary>
public sealed class DomainPage
{
    /// <summary>
    /// 获取或设置标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置页面标识键。由代码的 NavItem.PageKey 声明，落库后不随路由改动而变化。
    /// </summary>
    public string PageKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属域标识。
    /// </summary>
    public Guid DomainId { get; set; }

    /// <summary>
    /// 获取或设置路由。
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示名称。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属分组标题。
    /// </summary>
    public string? GroupTitle { get; set; }

    /// <summary>
    /// 获取或设置图标名称。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 获取或设置配色修饰类，取值来自设计体系的 platform-accent-* / platform-nav-link-*。
    /// </summary>
    public string? Accent { get; set; }

    /// <summary>
    /// 获取或设置页面等级：1 查看类，2 配置类。
    /// </summary>
    public int AccessLevel { get; set; } = 1;

    /// <summary>
    /// 获取或设置域内排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置归属来源：code 表示取代码默认值，override 表示管理员改过、启动对账不再覆盖。
    /// </summary>
    public string Source { get; set; } = "code";

    /// <summary>
    /// 获取或设置是否为已下线页面（代码中已不存在）。不删除，由管理员决定禁用或清理。
    /// </summary>
    public bool IsOrphaned { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置所属域。
    /// </summary>
    public BusinessDomain? Domain { get; set; }
}

/// <summary>
/// 表示「部门 × 职能域」的授权记录，是授权矩阵的落地表。
/// </summary>
public sealed class DepartmentDomainGrant
{
    /// <summary>
    /// 获取或设置部门标识。
    /// </summary>
    public Guid DepartmentId { get; set; }

    /// <summary>
    /// 获取或设置域标识。
    /// </summary>
    public Guid DomainId { get; set; }

    /// <summary>
    /// 获取或设置授权时间。
    /// </summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>
    /// 获取或设置授权人标识。
    /// </summary>
    public Guid? GrantedBy { get; set; }

    /// <summary>
    /// 获取或设置部门。
    /// </summary>
    public Department? Department { get; set; }

    /// <summary>
    /// 获取或设置职能域。
    /// </summary>
    public BusinessDomain? Domain { get; set; }
}

/// <summary>
/// 表示一个角色。角色带页面等级与权限点，按用户分配；页面等级取用户全部角色的最大值。
/// </summary>
public sealed class Role
{
    /// <summary>
    /// 获取或设置角色标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置角色编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置页面等级：1 只看查看类，2 含配置类，99 全部（超级管理员）。
    /// </summary>
    public int PageLevel { get; set; } = 1;

    /// <summary>
    /// 获取或设置是否为内置角色。内置角色不允许删除。
    /// </summary>
    public bool IsBuiltin { get; set; }

    /// <summary>
    /// 获取或设置是否启用。部门管理员默认停用，需要时改一个开关即可。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置角色的权限点集合。
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

/// <summary>
/// 表示一个权限点。只存 action.*，不为读操作建点。
/// </summary>
public sealed class Permission
{
    /// <summary>
    /// 获取或设置权限点标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置权限点编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属分组标题。
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// 获取或设置排序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// 表示角色与权限点的关联。
/// </summary>
public sealed class RolePermission
{
    /// <summary>
    /// 获取或设置角色标识。
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置权限点标识。
    /// </summary>
    public Guid PermissionId { get; set; }

    /// <summary>
    /// 获取或设置角色。
    /// </summary>
    public Role? Role { get; set; }

    /// <summary>
    /// 获取或设置权限点。
    /// </summary>
    public Permission? Permission { get; set; }
}

/// <summary>
/// 表示用户与角色的关联。权限点取并集，页面等级取最大值。
/// </summary>
public sealed class UserRole
{
    /// <summary>
    /// 获取或设置用户标识。
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置角色标识。
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置用户。
    /// </summary>
    public AppUser? User { get; set; }

    /// <summary>
    /// 获取或设置角色。
    /// </summary>
    public Role? Role { get; set; }
}

/// <summary>
/// 表示一条审计记录。只记录配置类写操作，查询与登录不留痕。
/// </summary>
public sealed class AuditLog
{
    /// <summary>
    /// 获取或设置标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// 获取或设置操作人标识。为空表示无法归到人（只读终端不产生审计）。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 获取或设置操作人账号。冗余存储，账号改名或删除后历史仍可读。
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 获取或设置操作人部门名称。冗余存储。
    /// </summary>
    public string? DepartmentName { get; set; }

    /// <summary>
    /// 获取或设置动作编码，例如 page.access_level.update。
    /// </summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置对象类型。
    /// </summary>
    public string? TargetType { get; set; }

    /// <summary>
    /// 获取或设置对象标识。
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// 获取或设置对象名称。
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// 获取或设置操作结果：success 或 failed。
    /// </summary>
    public string Result { get; set; } = "success";

    /// <summary>
    /// 获取或设置变更详情（{ before, after } 两段 JSON，只保留变化字段）。
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// 获取或设置客户端地址。
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>
    /// 获取或设置客户端标识。
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// 表示一条目录同步待处理项。
///
/// 存在的理由：目录里的部门名与平台部门不是同一套编码，账号名也可能与本地账号撞车。
/// 这两种情况都**不能自动处理**——自动建部门会让组织树随目录脏数据膨胀，
/// 自动并号则可能把域账号挂到本地管理员身上。因此记下来由超管确认。
/// </summary>
public sealed class DirectorySyncIssue
{
    /// <summary>
    /// 待处理类型：目录中的部门未在平台登记。
    /// </summary>
    public const string DepartmentUnmatched = "department-unmatched";

    /// <summary>
    /// 待处理类型：目录账号与一个未绑定目录的本地账号同名。
    /// </summary>
    public const string UserNameConflict = "user-name-conflict";

    /// <summary>
    /// 获取或设置标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置来源提供方，例如 ldap、oidc。
    /// </summary>
    public string Provider { get; set; } = "ldap";

    /// <summary>
    /// 获取或设置待处理类型，取值见 <see cref="DepartmentUnmatched"/> 与 <see cref="UserNameConflict"/>。
    /// </summary>
    public string IssueType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置外部标识：部门外部编码，或冲突时的账号名。
    /// </summary>
    public string ExternalCode { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置样例账号名，方便管理员定位。
    /// </summary>
    public string? SampleUserName { get; set; }

    /// <summary>
    /// 获取或设置累计出现次数。每次同步命中同一项时递增，不重复建行。
    /// </summary>
    public int Occurrences { get; set; } = 1;

    /// <summary>
    /// 获取或设置首次出现时间。
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>
    /// 获取或设置最近一次出现时间。
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// 获取或设置处理完成时间。为空表示仍未处理。
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// 获取或设置处理人标识。
    /// </summary>
    public Guid? ResolvedBy { get; set; }
}

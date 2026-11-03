namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 用户管理页展示的角色信息。权限点编码一并返回，便于页面说明「这个角色能做什么」。
/// </summary>
/// <param name="Id">角色标识。</param>
/// <param name="Code">角色编码。</param>
/// <param name="Name">显示名称。</param>
/// <param name="PageLevel">页面等级：1 只看查看类，2 含配置类，99 超级管理员。</param>
/// <param name="PermissionCodes">角色携带的权限点编码。</param>
public sealed record AdminRoleDto(
    Guid Id,
    string Code,
    string Name,
    int PageLevel,
    IReadOnlyList<string> PermissionCodes);

/// <summary>
/// 用户管理页展示的账号信息。不含密码与失败次数等敏感字段。
/// </summary>
/// <param name="Id">用户标识。</param>
/// <param name="UserName">登录账号。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="DepartmentId">所属部门标识，未归属时为空。</param>
/// <param name="DepartmentName">所属部门名称。</param>
/// <param name="Email">邮箱。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="MustChangePassword">是否仍处于「首次登录强制改密」状态。</param>
/// <param name="IsLockedOut">当前是否处于锁定中（连续登录失败）。</param>
/// <param name="LastLoginAt">最近登录时间。</param>
/// <param name="Roles">已分配角色。</param>
/// <param name="IsSuperAdmin">是否超级管理员。超管账号不可停用、不可改部门、不可删除。</param>
public sealed record AdminUserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    Guid? DepartmentId,
    string? DepartmentName,
    string? Email,
    bool IsEnabled,
    bool MustChangePassword,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<AdminRoleDto> Roles,
    bool IsSuperAdmin);

/// <summary>
/// 新建账号请求。初始密码由管理员设置，账号首次登录强制改密。
/// </summary>
/// <param name="UserName">登录账号，统一转小写。</param>
/// <param name="DisplayName">姓名。</param>
/// <param name="DepartmentId">所属部门；部门管理员创建时忽略此字段，强制本部门。</param>
/// <param name="Email">邮箱，可空。</param>
/// <param name="InitialPassword">初始密码，需满足复杂度要求。</param>
/// <param name="RoleIds">角色标识集合。</param>
public sealed record AdminUserCreateRequest(
    string UserName,
    string DisplayName,
    Guid? DepartmentId,
    string? Email,
    string InitialPassword,
    IReadOnlyList<Guid> RoleIds);

/// <summary>
/// 修改账号基本信息请求。部门变更走独立端点（仅超级管理员）。
/// </summary>
/// <param name="DisplayName">姓名。</param>
/// <param name="Email">邮箱，可空。</param>
/// <param name="IsEnabled">是否启用。超管账号忽略此字段。</param>
public sealed record AdminUserUpdateRequest(string DisplayName, string? Email, bool IsEnabled);

/// <summary>
/// 调整账号所属部门请求。仅超级管理员可用——跨部门调动不能下放给部门管理员。
/// </summary>
/// <param name="DepartmentId">目标部门标识，可空（表示未归属任何部门）。</param>
public sealed record AdminUserDepartmentRequest(Guid? DepartmentId);

/// <summary>
/// 分配角色请求。超级管理员角色不在此端点内分配，只能走「移交超管」。
/// </summary>
/// <param name="RoleIds">角色标识集合，全量覆盖。</param>
public sealed record AdminUserRolesRequest(IReadOnlyList<Guid> RoleIds);

/// <summary>
/// 重置密码请求。重置后账号被置为「首次登录强制改密」。
/// </summary>
/// <param name="NewPassword">新密码，需满足复杂度要求。</param>
public sealed record AdminUserResetPasswordRequest(string NewPassword);

/// <summary>
/// 批量启用/停用请求。
/// </summary>
/// <param name="UserIds">用户标识集合。</param>
/// <param name="IsEnabled">目标启停状态。</param>
public sealed record AdminUserBatchStatusRequest(IReadOnlyList<Guid> UserIds, bool IsEnabled);

/// <summary>
/// 移交超级管理员请求。目标账号必须启用、且不是当前超管本人。
/// </summary>
/// <param name="TargetUserId">接任账号标识。</param>
public sealed record SuperAdminHandoverRequest(Guid TargetUserId);

/// <summary>
/// 表示一条审计记录（查询结果）。
/// </summary>
/// <param name="Id">记录标识。</param>
/// <param name="OccurredAt">发生时间。</param>
/// <param name="UserName">操作人账号。</param>
/// <param name="DepartmentName">操作人部门名称。</param>
/// <param name="ActionCode">动作编码。</param>
/// <param name="TargetType">对象类型。</param>
/// <param name="TargetId">对象标识。</param>
/// <param name="TargetName">对象名称。</param>
/// <param name="Result">结果：success 或 failed。</param>
/// <param name="Detail">变更详情（JSON 原文，页面折叠展示）。</param>
/// <param name="Ip">客户端地址。</param>
public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? UserName,
    string? DepartmentName,
    string ActionCode,
    string? TargetType,
    string? TargetId,
    string? TargetName,
    string Result,
    string? Detail,
    string? Ip);

/// <summary>
/// 审计查询的一页结果。附带库中实际出现过的对象类型，供筛选下拉使用。
/// </summary>
/// <param name="Items">当前页记录。</param>
/// <param name="Total">符合筛选条件的总条数。</param>
/// <param name="Page">当前页码（从 1 开始）。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="TargetTypes">库中出现过的对象类型，用于筛选下拉。</param>
public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogDto> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> TargetTypes);

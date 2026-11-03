using DAP.Core.Shared.Catalog;

namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示域配置中的一个页面（管理视图，含 source 与下线标记）。
/// </summary>
/// <param name="Id">页面标识。</param>
/// <param name="PageKey">页面稳定标识。</param>
/// <param name="Title">页面名称。</param>
/// <param name="Route">路由。</param>
/// <param name="GroupTitle">分组标题，直接挂在域下为空。</param>
/// <param name="DomainCode">当前归属域编码。</param>
/// <param name="AccessLevel">页面等级：1 查看，2 配置。</param>
/// <param name="SortOrder">域内排序。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="IsOrphaned">代码中是否已不存在（下线）。</param>
/// <param name="Source">归属来源：code 代码声明 / override 管理员改过。</param>
public sealed record AdminDomainPageDto(
    Guid Id,
    string PageKey,
    string Title,
    string Route,
    string? GroupTitle,
    string DomainCode,
    int AccessLevel,
    int SortOrder,
    bool IsEnabled,
    bool IsOrphaned,
    string Source);

/// <summary>
/// 表示域配置（管理视图）。
/// </summary>
/// <param name="Id">域标识。</param>
/// <param name="Code">域编码，创建后不可改。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">顶栏排序。</param>
/// <param name="IsSystem">是否系统域（不参与授权矩阵）。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="VisibleWhenEmpty">空域是否对普通用户展示。</param>
/// <param name="Pages">域内页面，按排序排列。</param>
public sealed record AdminDomainDto(
    Guid Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsSystem,
    bool IsEnabled,
    bool VisibleWhenEmpty,
    IReadOnlyList<AdminDomainPageDto> Pages);

/// <summary>
/// 表示修改页面归属配置的请求。字段留空表示不修改。
///
/// 这些字段在 docs/AUTH_DESIGN.md 第 4 节里都登记为「平台可改」：
/// 所属域、显示名称、分组标题、排序、等级、启停。路由与页面标识键不在此列——
/// 它们由代码的 <c>@page</c> 与 <c>NavItem.PageKey</c> 决定，改了会让菜单指向 404。
/// </summary>
/// <param name="AccessLevel">目标页面等级：1 查看 / 2 配置。null 表示不改。</param>
/// <param name="DomainId">目标归属域。null 表示不改。</param>
/// <param name="IsEnabled">是否启用。null 表示不改。</param>
/// <param name="Title">显示名称。null 表示不改。</param>
/// <param name="GroupTitle">分组标题。null 表示不改，空字符串表示改为「直接挂在域下」。</param>
/// <param name="SortOrder">域内排序。null 表示不改。</param>
public sealed record AdminDomainPageUpdateRequest(
    int? AccessLevel,
    Guid? DomainId,
    bool? IsEnabled,
    string? Title = null,
    string? GroupTitle = null,
    int? SortOrder = null);

/// <summary>
/// 表示新建或修改域的请求。
/// </summary>
/// <param name="Code">域编码。只允许小写字母、数字与连字符，创建后不可改。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">顶栏排序。</param>
/// <param name="IsEnabled">是否启用。停用后域内页面全部不可见，授权记录保留。</param>
/// <param name="VisibleWhenEmpty">空域是否对普通用户展示。</param>
/// <param name="IsSystem">是否为系统域。系统域不参与授权矩阵，固定归超级管理员。</param>
public sealed record AdminDomainUpsertRequest(
    string Code,
    string Name,
    int SortOrder,
    bool IsEnabled,
    bool VisibleWhenEmpty,
    bool IsSystem);

/// <summary>
/// 表示部门列表项（管理视图，含已授权域）。
/// </summary>
/// <param name="Id">部门标识。</param>
/// <param name="Code">部门编码。</param>
/// <param name="Name">部门名称。</param>
/// <param name="ExternalCode">外部目录（LDAP/OIDC）对应的编码。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="UserCount">部门内账号数。</param>
/// <param name="GrantedDomainIds">已授权的业务域标识集合。</param>
public sealed record AdminDepartmentDto(
    Guid Id,
    string Code,
    string Name,
    string? ExternalCode,
    bool IsEnabled,
    int UserCount,
    IReadOnlyList<Guid> GrantedDomainIds);

/// <summary>
/// 表示新建或修改部门的请求。
/// </summary>
/// <param name="Code">部门编码，创建后不可改。</param>
/// <param name="Name">部门名称。</param>
/// <param name="ExternalCode">外部目录编码，可为空。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record AdminDepartmentUpsertRequest(string Code, string Name, string? ExternalCode, bool IsEnabled);

/// <summary>
/// 表示设置部门域授权的请求（全量覆盖：列表外的授权会被移除）。
/// </summary>
/// <param name="DomainIds">授予的业务域标识集合。系统域不允许出现在此列表。</param>
public sealed record AdminDepartmentGrantsRequest(IReadOnlyList<Guid> DomainIds);

namespace DAP.Core.Shared.Catalog;

/// <summary>
/// 认证与授权的固定编码。角色、权限点、职能域的编码一旦落库即被授权数据引用，
/// 因此集中声明在此，避免各处拼字符串导致「改一处、漏一处」。
/// </summary>
public static class AuthorizationCatalog
{
    /// <summary>钞纸职能域编码。</summary>
    public const string BanknotePaperDomain = "banknote-paper";

    /// <summary>印钞职能域编码。</summary>
    public const string BanknotePrintingDomain = "banknote-printing";

    /// <summary>能源职能域编码。</summary>
    public const string EnergyDomain = "energy";

    /// <summary>平台管理域编码。系统域，不参与授权矩阵，固定归超级管理员。</summary>
    public const string PlatformDomain = "platform-admin";

    /// <summary>查看类页面等级：只看不改。</summary>
    public const int ViewAccessLevel = 1;

    /// <summary>配置类页面等级：可执行写操作。</summary>
    public const int ConfigureAccessLevel = 2;

    /// <summary>超级管理员的页面等级：短路，不参与任何一层判定。</summary>
    public const int SuperAdminPageLevel = 99;

    /// <summary>超级管理员角色编码。</summary>
    public const string SuperAdminRole = "super-admin";

    /// <summary>部门管理员角色编码：默认停用，需要时改一个开关即可启用。</summary>
    public const string DepartmentAdminRole = "department-admin";

    /// <summary>技术人员角色编码：可见配置类页面并可修改排班。</summary>
    public const string TechnicianRole = "technician";

    /// <summary>普通用户角色编码：只看查看类页面。</summary>
    public const string ViewerRole = "viewer";

    /// <summary>排班配置权限点。</summary>
    public const string ShiftManagePermission = "action.shift.manage";

    /// <summary>用户管理权限点。</summary>
    public const string UserManagePermission = "action.user.manage";

    /// <summary>域与页面管理权限点。</summary>
    public const string DomainManagePermission = "action.domain.manage";

    /// <summary>部门与授权管理权限点。</summary>
    public const string DepartmentManagePermission = "action.department.manage";

    /// <summary>平台配置权限点。</summary>
    public const string SystemManagePermission = "action.system.manage";

    /// <summary>页面归属来源：由代码声明，启动对账时允许覆盖。</summary>
    public const string PageSourceCode = "code";

    /// <summary>页面归属来源：管理员在后台改过，启动对账时不再覆盖。</summary>
    public const string PageSourceOverride = "override";

    /// <summary>
    /// 授权策略名。端点与页面通过 <c>RequireAuthorization(策略名)</c> 引用，
    /// 实际判定由 PlatformAuthorizationPolicyProvider 按 claims 动态生成。
    /// </summary>
    public static class PolicyNames
    {
        /// <summary>域策略前缀，完整形式为 Domain:{code}，判定用户是否可见该域。</summary>
        public const string DomainPrefix = "Domain:";

        /// <summary>配置类策略：要求页面等级 ≥ 2。排班等配置类端点组的读策略。</summary>
        public const string Configure = "Access:Configure";

        /// <summary>平台管理策略：仅超级管理员通过。等价于系统域可见。</summary>
        public const string PlatformAdmin = "PlatformAdmin";

        /// <summary>权限点策略前缀，完整形式即权限点编码（如 action.shift.manage）。</summary>
        public const string PermissionPrefix = "action.";

        /// <summary>
        /// 构造指定职能域的策略名。
        /// </summary>
        /// <param name="domainCode">域编码。</param>
        /// <returns>策略名。</returns>
        public static string Domain(string domainCode) => $"{DomainPrefix}{domainCode}";
    }

    /// <summary>
    /// 全部内置职能域，按顶栏顺序排列。平台管理是系统域，不进授权矩阵。
    /// </summary>
    public static IReadOnlyList<BuiltInDomain> Domains { get; } =
    [
        new(BanknotePaperDomain, "钞纸", 10, false),
        new(BanknotePrintingDomain, "印钞", 20, false),
        new(EnergyDomain, "能源", 30, false),
        new(PlatformDomain, "平台管理", 900, true)
    ];

    /// <summary>
    /// 全部内置角色。部门管理员建了但默认停用——启用开关只在需要临时分担超管职责时打开。
    /// </summary>
    public static IReadOnlyList<BuiltInRole> Roles { get; } =
    [
        new(SuperAdminRole, "超级管理员", SuperAdminPageLevel, true, true, 10, []),
        new(DepartmentAdminRole, "部门管理员", ConfigureAccessLevel, true, false, 20,
            [UserManagePermission, ShiftManagePermission]),
        new(TechnicianRole, "技术人员", ConfigureAccessLevel, true, true, 30, [ShiftManagePermission]),
        new(ViewerRole, "普通用户", ViewAccessLevel, true, true, 40, [])
    ];

    /// <summary>
    /// 全部权限点。只为写操作建点，读操作不建——否则权限点会随页面数量膨胀。
    /// </summary>
    public static IReadOnlyList<BuiltInPermission> Permissions { get; } =
    [
        new(ShiftManagePermission, "排班配置", "班组管理", 10),
        new(UserManagePermission, "用户管理", "权限管理", 20),
        new(DomainManagePermission, "域与页面管理", "权限管理", 30),
        new(DepartmentManagePermission, "部门与授权管理", "权限管理", 40),
        new(SystemManagePermission, "平台配置", "平台运维", 50)
    ];

    /// <summary>
    /// 登录会话使用的自定义 claim 类型。授权判定只认这些 claim，不按请求地址现查。
    /// </summary>
    public static class PlatformClaimTypes
    {
        /// <summary>用户标识。</summary>
        public const string UserId = "dap:user_id";

        /// <summary>登录账号。</summary>
        public const string UserName = "dap:user_name";

        /// <summary>姓名。</summary>
        public const string DisplayName = "dap:display_name";

        /// <summary>部门标识。</summary>
        public const string DepartmentId = "dap:department_id";

        /// <summary>部门名称。</summary>
        public const string DepartmentName = "dap:department_name";

        /// <summary>页面等级，取用户全部角色中的最大值。</summary>
        public const string PageLevel = "dap:page_level";

        /// <summary>可见职能域编码，可能有多个。</summary>
        public const string Domain = "dap:domain";

        /// <summary>权限点编码，可能有多个。</summary>
        public const string Permission = "dap:permission";

        /// <summary>是否超级管理员。存在即为 true，不写 false。</summary>
        public const string SuperAdmin = "dap:super_admin";

        /// <summary>会话签发时间，用于绝对过期判定（Cookie 自身只做滑动过期）。</summary>
        public const string IssuedAt = "dap:issued_at";

        /// <summary>是否必须先修改密码。存在即表示必须改，改密后重新签发会话不再带此 claim。</summary>
        public const string MustChangePassword = "dap:must_change_password";

        /// <summary>
        /// 会话安全戳。与 <c>app_users.security_stamp</c> 比对，不一致即作废会话——
        /// 停用账号、管理员重置密码后，已签发的 Cookie 不再等到绝对过期才失效。
        /// </summary>
        public const string SecurityStamp = "dap:security_stamp";

        /// <summary>车间只读终端标记，P1 的 IP 白名单会话使用。</summary>
        public const string BoardTerminal = "dap:board_terminal";

        /// <summary>车间只读会话的签发 IP。会话绑定签发时的地址，不一致立即作废（docs/AUTH_DESIGN.md 6.6 硬约束 5）。</summary>
        public const string BoardIssuedIp = "dap:board_issued_ip";
    }
}

/// <summary>
/// 内置职能域的种子定义。
/// </summary>
/// <param name="Code">域编码，落库后不可改。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">顶栏排序。</param>
/// <param name="IsSystem">是否为系统域（不参与授权矩阵）。</param>
public sealed record BuiltInDomain(string Code, string Name, int SortOrder, bool IsSystem);

/// <summary>
/// 内置角色的种子定义。
/// </summary>
/// <param name="Code">角色编码。</param>
/// <param name="Name">显示名称。</param>
/// <param name="PageLevel">页面等级。</param>
/// <param name="IsBuiltin">是否为内置角色（不允许删除）。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="SortOrder">排序。</param>
/// <param name="PermissionCodes">角色拥有的权限点编码。</param>
public sealed record BuiltInRole(
    string Code,
    string Name,
    int PageLevel,
    bool IsBuiltin,
    bool IsEnabled,
    int SortOrder,
    IReadOnlyList<string> PermissionCodes);

/// <summary>
/// 内置权限点的种子定义。
/// </summary>
/// <param name="Code">权限点编码。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Group">分组标题。</param>
/// <param name="SortOrder">排序。</param>
public sealed record BuiltInPermission(string Code, string Name, string Group, int SortOrder);

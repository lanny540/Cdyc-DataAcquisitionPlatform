using DAP.Core.Shared.Catalog;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 授权目录的守卫测试。
///
/// 角色、权限点、域编码一旦落库就会被授权数据引用，改编码等于切断既有授权关系。
/// 这些编码目前由代码声明、启动时对账入库，因此在这里固定住形状与引用关系，
/// 避免「加一个角色忘了挂权限点」这种只有到授权页面才发现的错误。
/// </summary>
public class AuthorizationCatalogTests
{
    [Fact]
    public void Domain_Codes_Should_Be_Unique_And_Lowercase_Kebab()
    {
        var codes = AuthorizationCatalog.Domains.Select(domain => domain.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());

        foreach (var code in codes)
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", code);
        }
    }

    /// <summary>
    /// 平台管理是系统域：不参与授权矩阵、固定归超管，因此必须有且只有一个，
    /// 并且排在业务域之后（顶栏最后一项）。
    /// </summary>
    [Fact]
    public void Exactly_One_System_Domain_Should_Exist_And_Sort_Last()
    {
        var systemDomains = AuthorizationCatalog.Domains.Where(domain => domain.IsSystem).ToList();

        Assert.Single(systemDomains);
        Assert.Equal(AuthorizationCatalog.PlatformDomain, systemDomains[0].Code);
        Assert.Equal(
            AuthorizationCatalog.Domains.Max(domain => domain.SortOrder),
            systemDomains[0].SortOrder);
    }

    /// <summary>
    /// 设计预留了印钞与能源两个空域：页面尚未上线，但要能出现在授权矩阵里。
    /// </summary>
    [Fact]
    public void Reserved_Domains_Should_Be_Present()
    {
        var codes = AuthorizationCatalog.Domains.Select(domain => domain.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(AuthorizationCatalog.BanknotePaperDomain, codes);
        Assert.Contains(AuthorizationCatalog.BanknotePrintingDomain, codes);
        Assert.Contains(AuthorizationCatalog.EnergyDomain, codes);
    }

    [Fact]
    public void Role_Codes_Should_Be_Unique()
    {
        var codes = AuthorizationCatalog.Roles.Select(role => role.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(AuthorizationCatalog.SuperAdminRole, codes);
    }

    /// <summary>
    /// 超管全局唯一：目录里只能有一个角色取到短路等级。
    /// 若出现第二个，启动自检会因为「库里超管数量 ≠1」拒绝启动，上线当天才发现就晚了。
    /// </summary>
    [Fact]
    public void Only_SuperAdmin_Role_Should_Use_SuperAdmin_Page_Level()
    {
        var roles = AuthorizationCatalog.Roles
            .Where(role => role.PageLevel >= AuthorizationCatalog.SuperAdminPageLevel)
            .ToList();

        Assert.Single(roles);
        Assert.Equal(AuthorizationCatalog.SuperAdminRole, roles[0].Code);
    }

    /// <summary>
    /// 部门管理员默认停用：设计上「建了但不开」，需要时改一个开关即可启用。
    /// </summary>
    [Fact]
    public void Department_Admin_Should_Be_Disabled_By_Default()
    {
        var role = AuthorizationCatalog.Roles.Single(item => item.Code == AuthorizationCatalog.DepartmentAdminRole);

        Assert.False(role.IsEnabled);
        Assert.True(role.IsBuiltin);
    }

    [Fact]
    public void Page_Levels_Should_Be_View_Or_Configure()
    {
        foreach (var role in AuthorizationCatalog.Roles.Where(role => role.Code != AuthorizationCatalog.SuperAdminRole))
        {
            Assert.True(
                role.PageLevel is AuthorizationCatalog.ViewAccessLevel or AuthorizationCatalog.ConfigureAccessLevel,
                $"角色 {role.Code} 的页面等级 {role.PageLevel} 不在 1/2 之内");
        }
    }

    /// <summary>
    /// 权限点只给写操作建，编码统一 action. 前缀。
    /// 若混入 page.* 或读操作，权限点会随页面数量膨胀。
    /// </summary>
    [Fact]
    public void Permission_Codes_Should_Be_Unique_And_Action_Scoped()
    {
        var codes = AuthorizationCatalog.Permissions.Select(permission => permission.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());

        foreach (var code in codes)
        {
            Assert.StartsWith("action.", code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 角色声明的权限点必须真的存在，否则授权页会显示一个永远匹配不上的开关。
    /// </summary>
    [Fact]
    public void Role_Permissions_Should_Reference_Declared_Permissions()
    {
        var declared = AuthorizationCatalog.Permissions
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = AuthorizationCatalog.Roles
            .SelectMany(role => role.PermissionCodes.Select(code => (role.Code, Code: code)))
            .Where(pair => !declared.Contains(pair.Code))
            .Select(pair => $"{pair.Code}（角色 {pair.Code}）")
            .ToList();

        Assert.True(offenders.Count == 0, "角色引用了未声明的权限点：" + string.Join("、", offenders));
    }

    /// <summary>
    /// 查看类角色不应该带写权限：普通用户能改排班的话，页面等级那一层就白设了。
    /// </summary>
    [Fact]
    public void Viewer_Role_Should_Not_Carry_Write_Permissions()
    {
        var viewer = AuthorizationCatalog.Roles.Single(role => role.Code == AuthorizationCatalog.ViewerRole);

        Assert.Empty(viewer.PermissionCodes);
        Assert.Equal(AuthorizationCatalog.ViewAccessLevel, viewer.PageLevel);
    }

    /// <summary>
    /// 技术人员需要导入排班，因此必须同时拿到配置等级与排班权限点。
    /// </summary>
    [Fact]
    public void Technician_Role_Should_Allow_Shift_Management()
    {
        var technician = AuthorizationCatalog.Roles.Single(role => role.Code == AuthorizationCatalog.TechnicianRole);

        Assert.Equal(AuthorizationCatalog.ConfigureAccessLevel, technician.PageLevel);
        Assert.Contains(AuthorizationCatalog.ShiftManagePermission, technician.PermissionCodes);
    }
}

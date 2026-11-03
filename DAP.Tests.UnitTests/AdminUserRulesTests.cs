using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 用户管理安全规则的单元测试（docs/AUTH_DESIGN.md 9.3）。
///
/// 这几条规则是「部门管理员只能管本部门」的落地点：界面藏下拉项不算数，
/// 服务端判定才是边界。规则抽成纯函数后可以逐条钉死，改坏了测试会直接失败。
/// </summary>
public class AdminUserRulesTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("zhang3")]
    [InlineData("li.si-01")]
    [InlineData("a_b.c-d")]
    [InlineData("123456")]
    public void Valid_User_Names_Should_Pass(string userName)
    {
        Assert.True(AdminUserRules.IsValidUserName(userName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("-abc")]
    [InlineData(".abc")]
    [InlineData("user name")]
    [InlineData("张三")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void Invalid_User_Names_Should_Fail(string userName)
    {
        Assert.False(AdminUserRules.IsValidUserName(userName));
    }

    /// <summary>
    /// 大写字母不合法：登录前统一转小写（NormalizeUserName），
    /// 服务端仍拒绝大写，避免「Admin 与 admin 变成两个账号」。
    /// </summary>
    [Fact]
    public void Uppercase_User_Name_Should_Fail()
    {
        Assert.False(AdminUserRules.IsValidUserName("Admin"));
    }

    [Theory]
    [InlineData(true, "viewer", true)]
    [InlineData(true, "technician", true)]
    [InlineData(true, "department-admin", true)]
    [InlineData(true, "super-admin", false)]
    [InlineData(false, "viewer", true)]
    [InlineData(false, "technician", true)]
    [InlineData(false, "department-admin", false)]
    [InlineData(false, "super-admin", false)]
    public void Role_Assignability_Should_Follow_Actor(bool actorIsSuperAdmin, string roleCode, bool expected)
    {
        Assert.Equal(expected, AdminUserRules.IsAssignableRoleCode(actorIsSuperAdmin, roleCode));
    }

    [Fact]
    public void Super_Admin_Should_Manage_Any_Department()
    {
        var result = AdminUserRules.DescribeScopeViolation(
            actorIsSuperAdmin: true,
            actorDepartmentId: null,
            targetDepartmentId: Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void Department_Admin_Should_Manage_Same_Department_Only()
    {
        var departmentId = Guid.NewGuid();

        Assert.Null(AdminUserRules.DescribeScopeViolation(false, departmentId, departmentId));
        Assert.NotNull(AdminUserRules.DescribeScopeViolation(false, departmentId, Guid.NewGuid()));
    }

    /// <summary>
    /// 目标账号未归属部门时，部门管理员同样不能管理——否则「未归属」成了天然越权入口。
    /// </summary>
    [Fact]
    public void Department_Admin_Should_Not_Manage_User_Without_Department()
    {
        Assert.NotNull(AdminUserRules.DescribeScopeViolation(false, Guid.NewGuid(), null));
    }

    [Fact]
    public void Department_Admin_Without_Department_Should_Be_Rejected()
    {
        var result = AdminUserRules.DescribeScopeViolation(false, null, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Contains("未归属部门", result);
    }
}

using DAP.Presentation.BlazorWeb.Security.Directory;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 目录同步判定的单元测试（docs/AUTH_DESIGN.md 11.3）。
///
/// 这里每一条规则出错都是生产事故：
/// 无变化也写库 → 每小时把所有账号刷一遍；目录字段为空就清空 → 姓名变空白；
/// 停用超管 → 平台锁死；匹配不到部门就清空归属 → 一次目录脏数据把人踢出所有业务域。
/// </summary>
public class DirectorySyncPlannerTests
{
    private static readonly Guid DepartmentA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DepartmentB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static LdapDirectoryUser Record(
        string displayName = "张三",
        string? email = "zhang3@example.com",
        bool isEnabled = true)
    {
        return new LdapDirectoryUser("ext-1", "zhang3", displayName, email, "抄纸一车间", "E1001", isEnabled);
    }

    private static DirectoryAccountSnapshot Local(
        string displayName = "张三",
        string? email = "zhang3@example.com",
        Guid? departmentId = null,
        bool isEnabled = true,
        bool isSuperAdmin = false)
    {
        return new DirectoryAccountSnapshot(
            Guid.NewGuid(), "zhang3", displayName, email, departmentId, isEnabled, isSuperAdmin);
    }

    [Fact]
    public void Missing_Local_Account_Should_Be_Created()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(Record(), null, DepartmentA);

        Assert.Equal(DirectorySyncAction.Create, decision.Action);
        Assert.Equal(DepartmentA, decision.DepartmentId);
        Assert.Equal(["created"], decision.ChangedFields);
    }

    [Fact]
    public void Identical_Record_Should_Not_Write()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(Record(), Local(departmentId: DepartmentA), DepartmentA);

        Assert.Equal(DirectorySyncAction.Unchanged, decision.Action);
        Assert.False(decision.RequiresWrite);
        Assert.Empty(decision.ChangedFields);
    }

    [Fact]
    public void Changed_Display_Name_Should_Be_Updated()
    {
        DirectorySyncDecision decision =
            DirectorySyncPlanner.Decide(Record(displayName: "张三（新）"), Local(departmentId: DepartmentA), DepartmentA);

        Assert.Equal(DirectorySyncAction.Update, decision.Action);
        Assert.Equal("张三（新）", decision.DisplayName);
        Assert.Equal(["display_name"], decision.ChangedFields);
    }

    /// <summary>
    /// 目录里没填姓名时不能把本地姓名清空：LDAP 少填一个属性不该让平台上的名字变成空白。
    /// </summary>
    [Fact]
    public void Blank_Directory_Values_Should_Not_Clear_Local_Values()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(
            new LdapDirectoryUser("ext-1", "zhang3", string.Empty, null, null, null, true),
            Local(departmentId: DepartmentA),
            null);

        Assert.Equal(DirectorySyncAction.Unchanged, decision.Action);
        Assert.Equal("张三", decision.DisplayName);
        Assert.Equal("zhang3@example.com", decision.Email);
        Assert.Equal(DepartmentA, decision.DepartmentId);
    }

    /// <summary>
    /// 部门匹配不到时保留原归属（mappedDepartmentId 为 null）。
    /// </summary>
    [Fact]
    public void Unmatched_Department_Should_Keep_Existing()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(Record(), Local(departmentId: DepartmentA), null);

        Assert.Equal(DirectorySyncAction.Unchanged, decision.Action);
        Assert.Equal(DepartmentA, decision.DepartmentId);
    }

    [Fact]
    public void Changed_Department_Should_Be_Applied()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(Record(), Local(departmentId: DepartmentA), DepartmentB);

        Assert.Equal(DirectorySyncAction.Update, decision.Action);
        Assert.Equal(DepartmentB, decision.DepartmentId);
        Assert.Equal(["department_id"], decision.ChangedFields);
    }

    [Fact]
    public void Directory_Disabled_Account_Should_Be_Disabled()
    {
        DirectorySyncDecision decision =
            DirectorySyncPlanner.Decide(Record(isEnabled: false), Local(departmentId: DepartmentA), DepartmentA);

        Assert.Equal(DirectorySyncAction.Disable, decision.Action);
        Assert.False(decision.IsEnabled);
        Assert.Equal(["is_enabled"], decision.ChangedFields);
    }

    /// <summary>
    /// 超级管理员永远不被目录停用：超管唯一，一旦被停用平台就没人能改授权与角色了。
    /// </summary>
    [Fact]
    public void Super_Admin_Should_Never_Be_Disabled_By_Directory()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(
            Record(isEnabled: false),
            Local(departmentId: DepartmentA, isSuperAdmin: true),
            DepartmentA);

        Assert.Equal(DirectorySyncAction.Unchanged, decision.Action);
        Assert.True(decision.IsEnabled);
        Assert.DoesNotContain("is_enabled", decision.ChangedFields);
    }

    [Fact]
    public void Reappeared_Account_Should_Be_Re_Enabled()
    {
        DirectorySyncDecision decision =
            DirectorySyncPlanner.Decide(Record(), Local(isEnabled: false, departmentId: DepartmentA), DepartmentA);

        Assert.Equal(DirectorySyncAction.Update, decision.Action);
        Assert.True(decision.IsEnabled);
        Assert.Equal(["is_enabled"], decision.ChangedFields);
    }

    [Fact]
    public void Multiple_Changes_Should_Be_Reported_Together()
    {
        DirectorySyncDecision decision = DirectorySyncPlanner.Decide(
            Record(displayName: "张三（新）", email: "new@example.com"),
            Local(displayName: "张三", email: "old@example.com", departmentId: DepartmentA),
            DepartmentB);

        Assert.Equal(DirectorySyncAction.Update, decision.Action);
        Assert.Equal(3, decision.ChangedFields.Count);
        Assert.Contains("display_name", decision.ChangedFields);
        Assert.Contains("email", decision.ChangedFields);
        Assert.Contains("department_id", decision.ChangedFields);
    }

    [Theory]
    [InlineData("zhang3", "zhang3")]
    [InlineData(" Zhang3 ", "zhang3")]
    [InlineData("", "ldap-ext-1")]
    public void Derive_User_Name_Should_Normalize(string raw, string expected)
    {
        LdapDirectoryUser record = new("ext-1", raw, "张三", null, null, null, true);

        Assert.Equal(expected, DirectoryUserFactory.DeriveUserName(record));
    }
}

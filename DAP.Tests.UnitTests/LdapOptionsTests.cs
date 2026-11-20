using DAP.Presentation.BlazorWeb.Security.Directory;

namespace DAP.Tests.UnitTests;

/// <summary>
/// LDAP 选项中未被既有测试覆盖的属性集合构造逻辑。
/// </summary>
public sealed class LdapOptionsTests
{
    [Fact]
    public void BuildAttributeList_ShouldSkipBlanks_AndDeduplicate()
    {
        LdapOptions options = new()
        {
            LoginAttribute = "sAMAccountName",
            UniqueIdAttribute = "sAMAccountName", // 与登录属性重复，应去重
            DisplayNameAttribute = "displayName",
            MailAttribute = "mail",
            DepartmentAttribute = "  ", // 空白应被跳过
            EmployeeNoAttribute = "employeeNumber",
            DisabledFlagAttribute = "userAccountControl"
        };

        string[] attributes = options.BuildAttributeList();

        Assert.Equal(
            ["sAMAccountName", "displayName", "mail", "employeeNumber", "userAccountControl"],
            attributes);
    }

    [Fact]
    public void BuildAttributeList_ShouldHandle_AllBlankAttributes()
    {
        // 属性都有非空默认值，全部显式置空后结果应为空数组。
        LdapOptions options = new()
        {
            LoginAttribute = string.Empty,
            UniqueIdAttribute = string.Empty,
            DisplayNameAttribute = string.Empty,
            MailAttribute = string.Empty,
            DepartmentAttribute = string.Empty,
            EmployeeNoAttribute = string.Empty,
            DisabledFlagAttribute = string.Empty
        };

        Assert.Empty(options.BuildAttributeList());
    }
}

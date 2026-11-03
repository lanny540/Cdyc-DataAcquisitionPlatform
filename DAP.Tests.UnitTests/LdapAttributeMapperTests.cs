using DAP.Presentation.BlazorWeb.Security.Directory;

namespace DAP.Tests.UnitTests;

/// <summary>
/// LDAP 过滤器与属性映射的单元测试（docs/AUTH_DESIGN.md 11.1）。
///
/// 这里钉的是两类事故：过滤器没转义导致的 LDAP 注入（一个 <c>*</c> 就能以别人的身份 bind），
/// 以及属性缺失时映射出的空值把本地数据冲掉。
/// </summary>
public class LdapAttributeMapperTests
{
    [Theory]
    [InlineData("zhang3", "zhang3")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("a(b", @"a\28b")]
    [InlineData("a)b", @"a\29b")]
    [InlineData(@"a\b", @"a\5cb")]
    public void Escape_Should_Follow_Rfc4515(string raw, string expected)
    {
        Assert.Equal(expected, LdapFilter.Escape(raw));
    }

    /// <summary>
    /// 星号必须被转义：<c>(sAMAccountName=*)</c> 会命中目录里的第一个人，
    /// 后续二次 bind 用的就是他输入的密码——这就是一个认证绕过。
    /// </summary>
    [Fact]
    public void Build_User_Filter_Should_Escape_Wildcard()
    {
        var filter = LdapFilter.BuildUserFilter("(sAMAccountName={0})", "*");

        Assert.Equal(@"(sAMAccountName=\2a)", filter);
    }

    [Fact]
    public void Build_User_Filter_Should_Keep_Template_Scaffolding()
    {
        var filter = LdapFilter.BuildUserFilter("(&(objectClass=user)(sAMAccountName={0}))", "zhang3");

        Assert.Equal("(&(objectClass=user)(sAMAccountName=zhang3))", filter);
    }

    [Fact]
    public void Map_Should_Read_All_Configured_Attributes()
    {
        FakeEntry entry = new FakeEntry("CN=张三,OU=抄纸一车间,DC=example,DC=com")
            .With("sAMAccountName", "Zhang3")
            .With("objectGUID", "0a1b2c3d")
            .With("displayName", "张三")
            .With("mail", "zhang3@example.com")
            .With("department", "抄纸一车间")
            .With("employeeID", "E1001");

        LdapDirectoryUser? user = LdapAttributeMapper.Map(entry, new LdapOptions());

        Assert.NotNull(user);
        Assert.Equal("0a1b2c3d", user!.ExternalId);
        Assert.Equal("zhang3", user.UserName);
        Assert.Equal("张三", user.DisplayName);
        Assert.Equal("zhang3@example.com", user.Email);
        Assert.Equal("抄纸一车间", user.DepartmentExternalCode);
        Assert.Equal("E1001", user.EmployeeNo);
        Assert.True(user.IsEnabled);
    }

    [Fact]
    public void Map_Should_Fall_Back_To_Login_Name_When_Unique_Id_Missing()
    {
        FakeEntry entry = new FakeEntry("CN=李四,DC=example,DC=com")
            .With("sAMAccountName", "li4")
            .With("displayName", "李四");

        LdapDirectoryUser? user = LdapAttributeMapper.Map(entry, new LdapOptions());

        Assert.NotNull(user);
        Assert.Equal("li4", user!.ExternalId);
    }

    [Fact]
    public void Map_Should_Fall_Back_To_Login_Name_For_Display_Name()
    {
        FakeEntry entry = new FakeEntry("CN=x,DC=example,DC=com").With("sAMAccountName", "wang5");

        LdapDirectoryUser? user = LdapAttributeMapper.Map(entry, new LdapOptions());

        Assert.Equal("wang5", user!.DisplayName);
    }

    [Fact]
    public void Map_Should_Return_Null_Without_Any_Identity()
    {
        FakeEntry entry = new FakeEntry("CN=x,DC=example,DC=com");

        Assert.Null(LdapAttributeMapper.Map(entry, new LdapOptions()));
    }

    /// <summary>
    /// OpenLDAP 没有 userAccountControl。属性缺失必须视为启用——
    /// 否则第一次同步就会把整个目录的账号全部停用。
    /// </summary>
    [Fact]
    public void Missing_Disabled_Flag_Should_Mean_Enabled()
    {
        FakeEntry entry = new FakeEntry("CN=x,DC=example,DC=com").With("sAMAccountName", "zhao6");

        Assert.True(LdapAttributeMapper.Map(entry, new LdapOptions())!.IsEnabled);
    }

    [Theory]
    [InlineData("512", true)]
    [InlineData("514", false)]
    [InlineData("66048", true)]
    [InlineData("66050", false)]
    public void User_Account_Control_Should_Map_To_Enabled(string raw, bool expected)
    {
        FakeEntry entry = new FakeEntry("CN=x,DC=example,DC=com")
            .With("sAMAccountName", "sun7")
            .With("userAccountControl", raw);

        Assert.Equal(expected, LdapAttributeMapper.Map(entry, new LdapOptions())!.IsEnabled);
    }

    [Fact]
    public void Department_Should_Fall_Back_To_Ou_Level()
    {
        FakeEntry entry = new FakeEntry("CN=张三,OU=抄纸一车间,OU=生产部,DC=example,DC=com")
            .With("sAMAccountName", "zhang3");

        LdapOptions options = new() { DepartmentOuLevel = 1 };

        Assert.Equal("抄纸一车间", LdapAttributeMapper.Map(entry, options)!.DepartmentExternalCode);

        options.DepartmentOuLevel = 2;
        Assert.Equal("生产部", LdapAttributeMapper.Map(entry, options)!.DepartmentExternalCode);
    }

    [Fact]
    public void Department_Attribute_Should_Win_Over_Ou()
    {
        FakeEntry entry = new FakeEntry("CN=张三,OU=车间,DC=example,DC=com")
            .With("sAMAccountName", "zhang3")
            .With("department", "设备动力部");

        Assert.Equal(
            "设备动力部",
            LdapAttributeMapper.Map(entry, new LdapOptions { DepartmentOuLevel = 1 })!.DepartmentExternalCode);
    }

    [Fact]
    public void Split_Dn_Should_Honor_Escaped_Comma()
    {
        IReadOnlyList<string> parts = LdapAttributeMapper.SplitDn(@"CN=张\,三,OU=车间,DC=example,DC=com");

        Assert.Equal(4, parts.Count);
        Assert.Equal(@"CN=张\,三", parts[0]);
    }

    [Fact]
    public void Binary_Unique_Id_Should_Become_Hex_String()
    {
        FakeEntry entry = new FakeEntry("CN=x,DC=example,DC=com")
            .With("sAMAccountName", "qian8")
            .WithBytes("objectGUID", [0xDE, 0xAD, 0xBE, 0xEF]);

        Assert.Equal("deadbeef", LdapAttributeMapper.Map(entry, new LdapOptions())!.ExternalId);
    }

    [Theory]
    [InlineData("  Zhang3  ", "zhang3")]
    [InlineData(null, "")]
    public void Normalize_User_Name_Should_Trim_And_Lower(string? raw, string expected)
    {
        Assert.Equal(expected, LdapAttributeMapper.NormalizeUserName(raw));
    }

    /// <summary>
    /// 内存条目：真实实现包装 SearchResultEntry，测试用这个实现即可覆盖
    /// 「属性缺失」「二进制属性」「多值」等分支，不必起目录服务器。
    /// </summary>
    private sealed class FakeEntry(string distinguishedName) : ILdapEntry
    {
        private readonly Dictionary<string, object> _attributes = new(StringComparer.OrdinalIgnoreCase);

        public string DistinguishedName { get; } = distinguishedName;

        public FakeEntry With(string name, params string[] values)
        {
            _attributes[name] = values;
            return this;
        }

        public FakeEntry WithBytes(string name, byte[] value)
        {
            _attributes[name] = value;
            return this;
        }

        public string? GetString(string attributeName)
        {
            if (!_attributes.TryGetValue(attributeName, out object? value))
            {
                return null;
            }

            return value switch
            {
                string[] { Length: > 0 } values => values[0],
                byte[] bytes => LdapAttributeMapper.ToHexString(bytes),
                _ => null
            };
        }

        public byte[]? GetBytes(string attributeName)
        {
            return _attributes.TryGetValue(attributeName, out object? value) ? value as byte[] : null;
        }

        public IReadOnlyList<string> GetStrings(string attributeName)
        {
            return _attributes.TryGetValue(attributeName, out object? value) && value is string[] values
                ? values
                : [];
        }
    }
}

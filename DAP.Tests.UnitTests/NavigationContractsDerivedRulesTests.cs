using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 导航契约派生规则的边界测试。
///
/// 六个契约文件（UserAdmin / Admin / Directory / Navigation / ManagedDataExecution /
/// ConnectionDiagnostics Contracts）均为纯数据 record，除 Navigation 契约的派生属性外
/// 没有可独立执行的 Validate / 规则方法（编码与删除规则在 AdminDomainRules / AdminUserRules，
/// 已由对应测试覆盖）。这里钉死仅有的两条派生规则边界。
/// </summary>
public class NavigationContractsDerivedRulesTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(99, true)]
    public void 页面等级派生属性应按配置类门槛判定(int accessLevel, bool expected)
    {
        NavigationPageDto page = new(
            "key", "标题", "/route", "icon", "accent", accessLevel);

        Assert.Equal(expected, page.IsConfigurationPage);
    }

    [Fact]
    public void 空域落地页应为空()
    {
        NavigationDomainDto domain = new("code", "名称", false, true, []);

        Assert.Null(domain.FirstPage);
    }

    [Fact]
    public void 落地页应取排序最靠前的直接条目()
    {
        NavigationPageDto quickLink = new("quick", "直接条目", "/quick", "icon", "accent", 1);
        NavigationPageDto grouped = new("grouped", "分组条目", "/grouped", "icon", "accent", 1);

        NavigationDomainDto domain = new("code", "名称", false, false,
        [
            new NavigationGroupDto(string.Empty, [quickLink]),
            new NavigationGroupDto("分组", [grouped])
        ]);

        // 跨域切换时的落地页 = 域内排序最靠前的页面，即第一个分组的第一个条目。
        Assert.Equal(quickLink, domain.FirstPage);
    }
}

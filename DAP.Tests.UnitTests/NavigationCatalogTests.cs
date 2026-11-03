using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Client.Layout;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 导航目录与授权元数据的契约测试。
///
/// page_key 是启动对账的比对键：库里的 domain_pages 靠它判断「这一行还是不是同一个页面」。
/// 一旦重复，两个页面会互相覆盖归属，表现为某个域莫名多出/少掉一个菜单。
/// </summary>
public class NavigationCatalogTests
{
    [Fact]
    public void Page_Keys_Should_Be_Unique_And_Non_Empty()
    {
        var keys = NavigationCatalog.Pages.Select(page => page.Item.PageKey).ToList();

        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Page_Keys_Should_Be_Lowercase_Kebab()
    {
        foreach (var pageKey in NavigationCatalog.Pages.Select(page => page.Item.PageKey))
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", pageKey);
        }
    }

    /// <summary>
    /// 页面声明的域必须是授权目录里真实存在的域。
    /// 写错一个字母，对账时会凭空建出一个孤立域，菜单从此对这个域可见、对真实域不可见。
    /// </summary>
    [Fact]
    public void Page_Domains_Should_Be_Declared()
    {
        var declared = AuthorizationCatalog.Domains
            .Select(domain => domain.Code)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = NavigationCatalog.Pages
            .Where(page => !declared.Contains(page.Item.DomainCode))
            .Select(page => $"{page.Item.Title} → {page.Item.DomainCode}")
            .ToList();

        Assert.True(offenders.Count == 0, "页面引用了未声明的职能域：" + string.Join("、", offenders));
    }

    /// <summary>
    /// 页面等级只有两档：1 查看、2 配置。
    /// 三档以上会让「谁能改」的判断在代码里散成多个分支。
    /// </summary>
    [Fact]
    public void Page_Access_Levels_Should_Be_View_Or_Configure()
    {
        foreach (var page in NavigationCatalog.Pages)
        {
            Assert.True(
                page.Item.AccessLevel is AuthorizationCatalog.ViewAccessLevel or AuthorizationCatalog.ConfigureAccessLevel,
                $"页面 {page.Item.Title} 的等级 {page.Item.AccessLevel} 不是 1 或 2");
        }
    }

    /// <summary>
    /// 排班与采集配置属于写操作页面，必须是配置等级：
    /// 只读用户能打开导入页，等于把写权限开在了界面上。
    /// </summary>
    [Theory]
    [InlineData("/shift-schedules")]
    [InlineData("/shift-calendar")]
    [InlineData("/collection-points")]
    [InlineData("/managed-data-definitions")]
    public void Configuration_Pages_Should_Require_Configure_Level(string href)
    {
        var page = NavigationCatalog.Pages.Single(item => item.Item.Href == href);

        Assert.True(page.Item.IsConfigurationPage, $"页面 {page.Item.Title} 应要求配置等级");
    }

    /// <summary>
    /// 能耗类页面是纯展示，保持查看等级，否则普通用户进不了自家数据看板。
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/energy-history")]
    [InlineData("/energy-comparison")]
    [InlineData("/energy-report")]
    public void Energy_Pages_Should_Require_View_Level_Only(string href)
    {
        var page = NavigationCatalog.Pages.Single(item => item.Item.Href == href);

        Assert.Equal(AuthorizationCatalog.ViewAccessLevel, page.Item.AccessLevel);
    }

    [Fact]
    public void Every_Section_Should_Have_At_Least_One_Page()
    {
        foreach (var section in NavigationCatalog.Sections)
        {
            Assert.NotEmpty(section.AllItems);
            Assert.False(string.IsNullOrWhiteSpace(section.LandingHref));
        }
    }

    /// <summary>
    /// 默认落地必须是能耗仪表板：应用启动即落在「数据指标」，而不是平台管理。
    /// </summary>
    [Fact]
    public void Default_Section_Should_Land_On_Energy_Dashboard()
    {
        Assert.Equal("/", NavigationCatalog.DefaultSection.LandingHref);
    }

    /// <summary>
    /// 反查模块用最长前缀匹配：/managed-data-definitions/{id} 属于「点位数据管理」，
    /// 不能被更短的条目抢走。
    /// </summary>
    [Theory]
    [InlineData("", NavigationCatalog.DataMetrics)]
    [InlineData("energy-dashboard", NavigationCatalog.DataMetrics)]
    [InlineData("energy-history", NavigationCatalog.DataMetrics)]
    [InlineData("shift-schedules", NavigationCatalog.BusinessConfig)]
    [InlineData("managed-data-definitions/4f1c2a9e-0000-0000-0000-000000000000", NavigationCatalog.PlatformAdmin)]
    [InlineData("platform-overview", NavigationCatalog.PlatformAdmin)]
    public void Resolve_Section_Should_Use_Longest_Prefix(string path, string expectedSectionId)
    {
        Assert.Equal(expectedSectionId, NavigationCatalog.ResolveSection(path).Id);
    }
}

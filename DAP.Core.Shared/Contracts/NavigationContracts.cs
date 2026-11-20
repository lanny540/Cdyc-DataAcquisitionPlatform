using DAP.Core.Shared.Catalog;

namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示导航树中的一个页面。数据来自 domain_pages，图标由代码常量补充。
/// </summary>
/// <param name="PageKey">页面稳定标识。</param>
/// <param name="Title">菜单显示名称。</param>
/// <param name="Route">目标路由，根路由为 "/"。</param>
/// <param name="Icon">图标（MudBlazor 图标常量值）。</param>
/// <param name="Accent">导航图标取色类，来自 app.css 已定义的 platform-nav-link--* 修饰类。</param>
/// <param name="AccessLevel">页面等级：1 查看类，2 配置类。</param>
public sealed record NavigationPageDto(
    string PageKey,
    string Title,
    string Route,
    string Icon,
    string Accent,
    int AccessLevel)
{
    /// <summary>是否配置类页面。</summary>
    public bool IsConfigurationPage => AccessLevel >= AuthorizationCatalog.ConfigureAccessLevel;
}

/// <summary>
/// 表示导航树中的一个分组。
/// </summary>
/// <param name="Title">分组标题；空字符串表示直接挂在域下的条目，渲染时不显示分组标题。</param>
/// <param name="Pages">该分组下的页面。</param>
public sealed record NavigationGroupDto(string Title, IReadOnlyList<NavigationPageDto> Pages);

/// <summary>
/// 表示导航树中的一个职能域。顶栏渲染「用户可见域列表」，全部数据由服务端按当前用户过滤。
/// </summary>
/// <param name="Code">域编码。</param>
/// <param name="Name">显示名称。</param>
/// <param name="IsSystem">是否系统域（平台管理，仅超管）。</param>
/// <param name="IsEmpty">域内当前没有可见页面。空域对普通用户隐藏；对超管显示并带「未上线」标记。</param>
/// <param name="Groups">域内分组，直接条目分组排在最前。</param>
public sealed record NavigationDomainDto(
    string Code,
    string Name,
    bool IsSystem,
    bool IsEmpty,
    IReadOnlyList<NavigationGroupDto> Groups)
{
    /// <summary>
    /// 域内排序最靠前的页面，作为跨域切换时的落地页；空域为 null。
    /// </summary>
    public NavigationPageDto? FirstPage => Groups.SelectMany(group => group.Pages).FirstOrDefault();
}

/// <summary>
/// 表示当前用户的完整导航树。
/// </summary>
/// <param name="Domains">可见域列表，按顶栏顺序排列。</param>
public sealed record NavigationDto(IReadOnlyList<NavigationDomainDto> Domains);

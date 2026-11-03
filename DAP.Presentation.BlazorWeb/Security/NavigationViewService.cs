using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Client.Layout;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 按当前会话构建导航树（docs/AUTH_DESIGN.md 第 5 节）。
///
/// 顶栏渲染的是「用户可见域列表」而不是固定模块：域可见性按 claims 过滤、
/// 页面按等级过滤、空域对普通用户隐藏、对超管保留并标记「未上线」。
/// 页面结构来自 domain_pages（管理员可在后台覆盖归属与等级），图标由代码常量补充。
/// </summary>
public sealed class NavigationViewService(IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// 构建当前用户的导航树。
    /// </summary>
    /// <param name="principal">当前会话主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导航树。</returns>
    public async Task<NavigationDto> BuildAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = PlatformClaimsFactory.IsSuperAdmin(principal);
        var pageLevel = PlatformClaimsFactory.ReadPageLevel(principal);

        HashSet<string> visibleDomainCodes = principal
            .FindAll(AuthorizationCatalog.PlatformClaimTypes.Domain)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        // 自己开 scope 取上下文，不复用调用方的 scoped DbContext。
        //
        // 导航由 MainLayout 在渲染时加载，与页面自己的 OnInitializedAsync 并行发起；
        // Blazor 渲染器不会把父子组件的异步初始化串行化，两者共用同一个 DbContext
        // 就会撞上「A second operation was started on this context instance」。
        // 这里只有两条只读查询、不参与任何跨服务事务，独立 scope 是最省心的解法。
        List<BusinessDomain> domains;
        List<DomainPage> pages;

        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            DataAcquisitionPlatformDbContext db =
                scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

            domains = await db.BusinessDomains
                .Where(domain => domain.IsEnabled)
                .OrderBy(domain => domain.SortOrder)
                .ToListAsync(cancellationToken);

            pages = await db.DomainPages
                .Where(page => page.IsEnabled && !page.IsOrphaned)
                .OrderBy(page => page.SortOrder)
                .ToListAsync(cancellationToken);
        }

        // 图标不落库（MudBlazor 图标常量是整段 SVG path），渲染时按 PageKey 从代码常量取。
        Dictionary<string, string> iconByPageKey = NavigationCatalog.Pages
            .ToDictionary(
                descriptor => descriptor.Item.PageKey,
                descriptor => descriptor.Item.Icon,
                StringComparer.Ordinal);

        // 根路由匹配需要精确匹配，见 NavigationCatalog 中 RequiresExactMatch 的说明。
        List<NavigationDomainDto> result = [];

        foreach (var domain in domains)
        {
            // 域可见性：超管看全部（含系统域与空域）；普通用户只看已授权的业务域。
            if (!isSuperAdmin
                && (domain.IsSystem || !visibleDomainCodes.Contains(domain.Code)))
            {
                continue;
            }

            List<DomainPage> domainPages =
                pages.Where(page => page.DomainId == domain.Id).ToList();

            // 页面等级过滤：用户等级只保留不超过自身等级的页面。车间只读终端（等级 1）
            // 因此只见查看类页面，配置类页面连菜单都不出现。
            if (pageLevel < AuthorizationCatalog.SuperAdminPageLevel)
            {
                domainPages = domainPages
                    .Where(page => page.AccessLevel <= pageLevel)
                    .ToList();
            }

            var isEmpty = domainPages.Count == 0;

            // 空域默认对普通用户隐藏（visible_when_empty 可按域打开）；超管保留以便核对配置。
            if (isEmpty && !isSuperAdmin && !domain.VisibleWhenEmpty)
            {
                continue;
            }

            List<NavigationGroupDto> groups = [];

            // 直接挂在域下的条目（GroupTitle 为空）排最前，渲染时不显示分组标题。
            List<NavigationPageDto> quickLinks =
            [
                .. domainPages
                    .Where(page => string.IsNullOrWhiteSpace(page.GroupTitle))
                    .Select(page => ToDto(page, iconByPageKey))
            ];

            if (quickLinks.Count > 0)
            {
                groups.Add(new NavigationGroupDto(string.Empty, quickLinks));
            }

            // 带分组标题的页面，按页面排序中首次出现的顺序分组。
            groups.AddRange(
                domainPages
                    .Where(page => !string.IsNullOrWhiteSpace(page.GroupTitle))
                    .GroupBy(page => page.GroupTitle!, StringComparer.Ordinal)
                    .Select(grouping => new NavigationGroupDto(
                        grouping.Key,
                        [.. grouping.Select(page => ToDto(page, iconByPageKey))])));

            result.Add(new NavigationDomainDto(
                domain.Code,
                domain.Name,
                domain.IsSystem,
                isEmpty,
                groups));
        }

        // 跨域共享页（如「用户与角色」）：不依赖域授权，持有权限点即可见。
        // 非超管用户的本部门授权域是业务域，不会包含平台管理域，因此单独合成一个
        // 「权限管理」域承接这类页面；超管已在平台管理域里看到，无需重复。
        if (!isSuperAdmin)
        {
            List<NavigationPageDto> sharedPages =
            [
                .. NavigationCatalog.SharedPages
                    .Where(item => pageLevel >= item.AccessLevel)
                    .Where(item => !item.RequiresPermission || PlatformClaimsFactory.HasPermission(principal, item.RequiredPermission!))
                    .Select(ToDto)
            ];

            if (sharedPages.Count > 0)
            {
                result.Add(new NavigationDomainDto(
                    SharedDomainCode,
                    "权限管理",
                    false,
                    false,
                    [new NavigationGroupDto(string.Empty, sharedPages)]));
            }
        }

        return new NavigationDto(result);
    }

    /// <summary>
    /// 共享页合成域的编码。不落库、不参与授权矩阵，仅用于顶栏渲染与高亮比对。
    /// </summary>
    public const string SharedDomainCode = "shared-admin";

    private static NavigationPageDto ToDto(NavItem item)
    {
        return new NavigationPageDto(
            item.PageKey,
            item.Title,
            item.Href,
            item.Icon,
            item.Accent,
            item.AccessLevel);
    }

    private static NavigationPageDto ToDto(
        DomainPage page,
        IReadOnlyDictionary<string, string> iconByPageKey)
    {
        var pageKey = page.PageKey ?? string.Empty;
        var icon = iconByPageKey.TryGetValue(pageKey, out var value)
            ? value
            : Icons.Material.Filled.Dashboard;

        return new NavigationPageDto(
            pageKey,
            page.Title,
            page.Route,
            icon,
            page.Accent ?? "platform-nav-link--primary",
            page.AccessLevel);
    }
}

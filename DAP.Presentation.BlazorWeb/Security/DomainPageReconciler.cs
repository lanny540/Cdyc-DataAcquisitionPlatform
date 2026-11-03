using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Client.Layout;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 表示一次页面归属对账的结果。
/// </summary>
/// <param name="Created">新增页面数。</param>
/// <param name="Updated">按代码默认值刷新页面数。</param>
/// <param name="Kept">管理员改过、本次未覆盖的页面数。</param>
/// <param name="Orphaned">标记为已下线的页面数（代码中已不存在）。</param>
/// <param name="Skipped">因所属域不存在而跳过的页面数。</param>
public sealed record DomainPageReconcileResult(int Created, int Updated, int Kept, int Orphaned, int Skipped);

/// <summary>
/// 启动时把代码声明的页面归属对账入库。
///
/// 这一步是整套设计的安全阀：如果页面归属只来自数据库，新页面部署后不会出现在任何菜单里，
/// 而且不报错——这是这类系统最常见的线上故障。
/// </summary>
public static class DomainPageReconciler
{
    /// <summary>
    /// 执行对账。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="timeProvider">时间提供器。</param>
    /// <param name="logger">日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对账结果。</returns>
    public static async Task<DomainPageReconcileResult> ReconcileAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        Dictionary<string, BusinessDomain> domains = await db.BusinessDomains
            .ToDictionaryAsync(domain => domain.Code, StringComparer.Ordinal, cancellationToken);

        Dictionary<string, DomainPage> pages = await db.DomainPages
            .ToDictionaryAsync(page => page.PageKey, StringComparer.Ordinal, cancellationToken);

        HashSet<string> seenPageKeys = new(StringComparer.Ordinal);
        Dictionary<string, int> sortOrderByDomain = new(StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var kept = 0;
        var skipped = 0;

        foreach (NavPageDescriptor descriptor in NavigationCatalog.Pages)
        {
            NavItem item = descriptor.Item;
            seenPageKeys.Add(item.PageKey);

            if (!domains.TryGetValue(item.DomainCode, out BusinessDomain? domain))
            {
                skipped++;
                logger.LogWarning(
                    "页面 {PageKey} 声明的职能域 {DomainCode} 不存在，已跳过对账。请先在平台管理中创建该域。",
                    item.PageKey, item.DomainCode);
                continue;
            }

            var sortOrder = sortOrderByDomain.TryGetValue(item.DomainCode, out var currentOrder)
                ? currentOrder
                : 0;
            sortOrderByDomain[item.DomainCode] = sortOrder + 1;

            var isNew = false;

            if (!pages.TryGetValue(item.PageKey, out DomainPage? page))
            {
                page = new DomainPage
                {
                    Id = Guid.NewGuid(),
                    PageKey = item.PageKey,
                    Source = AuthorizationCatalog.PageSourceCode,
                    IsEnabled = true,
                    CreatedAt = now
                };

                db.DomainPages.Add(page);
                pages[item.PageKey] = page;
                isNew = true;
            }
            else if (string.Equals(page.Source, AuthorizationCatalog.PageSourceOverride, StringComparison.Ordinal))
            {
                // 管理员改过归属：名称/分组/排序/等级/启停不覆盖，但**路由必须跟代码走**。
                //
                // 路由按设计不允许后台修改（docs/AUTH_DESIGN.md 第 4 节的表里「平台可改」为否），
                // 一旦管理员动过这一行的任何一个开关，整行就变成 override；若连路由也一并冻结，
                // 开发者日后改名路由时菜单会永久指向旧地址，表现为点菜单进 404，
                // 而库里看起来「配置齐全」。这里只刷新代码独占的两个字段。
                var routeChanged = !string.Equals(page.Route, item.Href, StringComparison.Ordinal);

                if (routeChanged)
                {
                    logger.LogInformation(
                        "页面 {PageKey} 的路由已由 {OldRoute} 更新为 {NewRoute}（页面配置仍保留后台覆盖）。",
                        item.PageKey, page.Route, item.Href);
                    page.Route = item.Href;
                    page.UpdatedAt = now;
                }

                // 代码里页面回来了就取消下线标记。
                if (page.IsOrphaned)
                {
                    page.IsOrphaned = false;
                    page.UpdatedAt = now;
                }

                kept++;
                continue;
            }

            page.DomainId = domain.Id;
            page.Route = item.Href;
            page.Title = item.Title;
            page.GroupTitle = descriptor.GroupTitle;

            // 不写入图标：MudBlazor 的 Icons.Material.* 是整段 SVG path，
            // 直接落库会撑爆短字符列，而菜单渲染本来就用代码里的常量。
            // 该列保留给后台按「短图标名」覆盖，格式由 P1 的配置页决定。
            page.Accent = item.Accent;
            page.AccessLevel = item.AccessLevel;
            page.SortOrder = sortOrder;
            page.Source = AuthorizationCatalog.PageSourceCode;
            page.IsOrphaned = false;
            page.UpdatedAt = now;

            if (isNew)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        var orphaned = 0;

        foreach (DomainPage page in pages.Values)
        {
            if (seenPageKeys.Contains(page.PageKey) || page.IsOrphaned)
            {
                continue;
            }

            // 代码里已不存在：标记下线而不是删除，后台以「已下线」列出由管理员处理。
            page.IsOrphaned = true;
            page.UpdatedAt = now;
            orphaned++;
            logger.LogWarning("页面 {PageKey}（{Route}）在代码中已不存在，已标记为下线。", page.PageKey, page.Route);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new DomainPageReconcileResult(created, updated, kept, orphaned, skipped);
    }
}

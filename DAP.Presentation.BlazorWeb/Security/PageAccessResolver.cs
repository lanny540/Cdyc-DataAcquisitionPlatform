using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 表示一个已登记页面在当前库配置下的访问要求。
/// </summary>
/// <param name="DomainCode">所属职能域编码（管理员可在后台覆盖归属）。</param>
/// <param name="AccessLevel">页面等级：1 查看类，2 配置类（管理员可调整）。</param>
public sealed record PageAccessRequirement(string DomainCode, int AccessLevel);

/// <summary>
/// 按请求路径解析页面的域归属与等级要求（docs/AUTH_DESIGN.md 7.3）。
///
/// 端点组按域划分，而同一域内既有查看类页面也有配置类页面——只按域判定的策略
/// 拦不住「普通用户直接输入配置页地址」。本解析器供服务端闸门在页面请求时
/// 判定「域可见 + 等级足够」，数据来自 domain_pages（含管理员覆盖），短缓存兜性能。
/// </summary>
public sealed class PageAccessResolver(
    DataAcquisitionPlatformDbContext db,
    IMemoryCache cache)
{
    private const string CacheKey = "domain-pages-access-map";

    /// <summary>
    /// 缓存有效期。页面归属变更频率极低，60 秒内不一致可接受；导航过滤与端点策略
    /// 按各自路径生效，本缓存只影响「直接输入地址」的判定。
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 按路径解析页面访问要求。采用最长前缀匹配：/managed-data-definitions/{id}
    /// 归「点位数据管理」页面而不是被更短的路由抢先命中。未登记路径返回 null（不判）。
    /// </summary>
    /// <param name="path">请求路径，如 /shift-schedules 或 /（空串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问要求；路径未登记时返回 null。</returns>
    public async Task<PageAccessRequirement?> ResolveAsync(string path, CancellationToken cancellationToken = default)
    {
        Dictionary<string, PageAccessRequirement> map = await GetAccessMapAsync(cancellationToken);

        // 归一化：去查询串、去首尾斜杠。
        var trimmed = path.Trim();
        var queryIndex = trimmed.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            trimmed = trimmed[..queryIndex];
        }

        var normalized = trimmed.Trim('/');

        PageAccessRequirement? matched = null;
        var matchedLength = -1;

        foreach (var (route, requirement) in map)
        {
            var isMatch = route.Length == 0
                ? normalized.Length == 0
                : normalized.Equals(route, StringComparison.OrdinalIgnoreCase)
                  || normalized.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase);

            if (!isMatch || route.Length <= matchedLength)
            {
                continue;
            }

            matched = requirement;
            matchedLength = route.Length;
        }

        return matched;
    }

    private async Task<Dictionary<string, PageAccessRequirement>> GetAccessMapAsync(
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<string, PageAccessRequirement>? cached) && cached is not null)
        {
            return cached;
        }

        List<DomainPage> pages = await db.DomainPages
            .Where(page => page.IsEnabled && !page.IsOrphaned && page.Domain!.IsEnabled)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> domainCodes = await db.BusinessDomains
            .Where(domain => domain.IsEnabled)
            .ToDictionaryAsync(domain => domain.Id, domain => domain.Code, cancellationToken);

        var map = new Dictionary<string, PageAccessRequirement>(StringComparer.OrdinalIgnoreCase);

        foreach (DomainPage page in pages)
        {
            if (!domainCodes.TryGetValue(page.DomainId, out var domainCode))
            {
                continue;
            }

            // 根路由存为空串，与 Normalize 之后的比较逻辑一致。
            var route = page.Route.Trim().Trim('/');
            map[route] = new PageAccessRequirement(domainCode, page.AccessLevel);
        }

        cache.Set(CacheKey, map, CacheDuration);
        return map;
    }
}

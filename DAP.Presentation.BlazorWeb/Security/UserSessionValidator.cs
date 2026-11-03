using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 会话有效性校验：把 Cookie 里的安全戳与库里的当前值比对。
///
/// 为什么需要它：授权只认 Cookie 里的 claims，会话一旦签发就与库脱钩。
/// 于是「管理员停用了某账号」「管理员重置了他人的密码」之后，
/// 旧会话仍能用到绝对过期（最长 12 小时）——而这恰恰是两个最需要立刻生效的操作。
///
/// 实现上做了 30 秒缓存：`OnValidatePrincipal` 每个请求都会跑，
/// 每次都查库等于给每个静态资源请求加一次数据库往返。
/// 30 秒的窗口对「停用账号」这类操作完全够用，而且换戳写库后最多 30 秒全站生效。
/// </summary>
public sealed class UserSessionValidator(
    DataAcquisitionPlatformDbContext db,
    IMemoryCache cache)
{
    /// <summary>
    /// 缓存时长。取 30 秒是「撤销及时性」与「不压垮数据库」的折中。
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 判断会话是否仍然有效：用户存在、处于启用状态，且安全戳未变。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="stamp">Cookie 中的安全戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>有效时 true。</returns>
    public async Task<bool> IsSessionValidAsync(
        Guid userId,
        string? stamp,
        CancellationToken cancellationToken = default)
    {
        // 没有安全戳的会话一律不认：这既覆盖「升级前签发的老 Cookie」，
        // 也覆盖任何被裁剪过 claims 的伪造尝试。失败方向必须是关闭，不是放行。
        if (string.IsNullOrWhiteSpace(stamp))
        {
            return false;
        }

        var key = $"dap:session:{userId}";

        if (!cache.TryGetValue(key, out SessionSnapshot? snapshot) || snapshot is null)
        {
            snapshot = await db.AppUsers
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new SessionSnapshot(user.IsEnabled, user.SecurityStamp))
                .FirstOrDefaultAsync(cancellationToken);

            // 用户已删除也缓存：否则每个请求都会回查一次「查无此人」。
            snapshot ??= SessionSnapshot.Missing;

            cache.Set(key, snapshot, CacheDuration);
        }

        return snapshot.IsEnabled
               && string.Equals(snapshot.SecurityStamp?.ToString(), stamp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 换掉安全戳，使该用户所有已签发会话立即失效（最迟一个缓存周期）。
    /// 调用方负责保存实体，这里只改内存中的值。
    /// </summary>
    /// <param name="user">用户实体。</param>
    public void RenewSecurityStamp(AppUser user)
    {
        user.SecurityStamp = Guid.NewGuid();

        // 主动清缓存：否则「停用后立刻检查」会在最坏情况下等满一个周期。
        cache.Remove($"dap:session:{user.Id}");
    }

    private sealed record SessionSnapshot(bool IsEnabled, Guid? SecurityStamp)
    {
        public static SessionSnapshot Missing { get; } = new(false, null);
    }
}

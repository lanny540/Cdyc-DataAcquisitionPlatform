using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 领域服务写操作的统一守卫：先授权、再执行、后留痕（docs/AUTH_DESIGN.md 7.2 / 9.4）。
///
/// HTTP 端点与 Server 电路直调两条路径都经过这里，区别只在操作者从哪里取——
/// 授权判定本身永远只认会话 claims，不存在「这段按 IP 放行、那段按身份放行」的双轨制。
/// </summary>
internal static class ServiceAuditGuard
{
    /// <summary>
    /// 执行一次受保护的写操作：无身份或缺少权限点直接拒绝（拒绝也留痕）；
    /// 执行成功与业务失败都写入审计。
    /// </summary>
    /// <typeparam name="T">操作返回类型。</typeparam>
    /// <param name="scopeFactory">请求范围工厂（装饰器可能是单例，不能直接持有 Scoped 服务）。</param>
    /// <param name="permissionCode">所需权限点编码。</param>
    /// <param name="actionCode">审计动作编码。</param>
    /// <param name="targetType">审计对象类型。</param>
    /// <param name="targetId">审计对象标识。</param>
    /// <param name="targetName">审计对象名称。</param>
    /// <param name="operation">真实业务操作。</param>
    /// <param name="succeededSelector">从返回值判断业务是否成功；null 视为成功。</param>
    /// <param name="detailFactory">从返回值构造审计明细；null 不记明细。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>业务操作返回值。</returns>
    public static async Task<T> ExecuteAsync<T>(
        IServiceScopeFactory scopeFactory,
        string permissionCode,
        string actionCode,
        string targetType,
        string? targetId,
        string? targetName,
        Func<CurrentUser, Task<T>> operation,
        Func<T, bool>? succeededSelector = null,
        Func<T, object?>? detailFactory = null,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        var userContext = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();
        var writer = scope.ServiceProvider.GetRequiredService<AuditLogWriter>();

        CurrentUser? user = await userContext.GetUserAsync(cancellationToken);

        if (user is null)
        {
            // 没有可归责的操作者就不允许写：IP 不是身份（docs/AUTH_DESIGN.md 6.6 硬约束 4）。
            await writer.RecordAsync(null, actionCode, targetType, targetId, targetName, false,
                new {reason = "会话没有可识别的操作者"}, cancellationToken);
            throw new ForbiddenAccessException("页面仍在加载或会话已失效，请刷新页面后重试。");
        }

        // 账号是否仍然启用必须在写操作前回查一次库：
        // 停用与安全戳只对 HTTP 请求生效，而 Blazor Server 的写操作走 SignalR 电路，
        // 电路建立后不再重新认证——不查库的话，被停用的人可以在已打开的页面里继续改数据。
        if (user.UserId is { } userId)
        {
            var db = scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

            var stillEnabled = await db.AppUsers
                .AsNoTracking()
                .AnyAsync(item => item.Id == userId && item.IsEnabled, cancellationToken);

            if (!stillEnabled)
            {
                await writer.RecordAsync(user, actionCode, targetType, targetId, targetName, false,
                    new {reason = "账号已停用或不存在"}, cancellationToken);
                throw new ForbiddenAccessException("账号已停用，请联系管理员。");
            }
        }

        if (!user.HasPermission(permissionCode))
        {
            await writer.RecordAsync(user, actionCode, targetType, targetId, targetName, false,
                new {reason = "缺少权限点", permission = permissionCode}, cancellationToken);
            throw new ForbiddenAccessException("当前账号没有执行该操作的权限，请联系管理员。");
        }

        try
        {
            T result = await operation(user);

            var succeeded = succeededSelector?.Invoke(result) ?? true;

            await writer.RecordAsync(user, actionCode, targetType, targetId, targetName, succeeded,
                detailFactory?.Invoke(result), cancellationToken);

            return result;
        }
        catch (ForbiddenAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 异常细节写审计（仅超管可查），长度截断避免异常堆栈撑爆列。
            var reason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

            await writer.RecordAsync(user, actionCode, targetType, targetId, targetName, false,
                new {reason, errorType = ex.GetType().Name}, cancellationToken);

            throw;
        }
    }
}

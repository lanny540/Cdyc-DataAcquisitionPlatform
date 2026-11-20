using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 目录全量同步的后台任务（docs/AUTH_DESIGN.md 11.1「定时全量，默认每小时」）。
///
/// 设计上有两点刻意为之：
/// 1. **启动后先等一会儿再跑第一轮**：应用启动时数据库迁移、种子与页面对账都在抢连接，
///    这时再去连目录只会让首屏更慢；
/// 2. **失败不退出循环**：目录服务器维护、网络抖动都是常态，挂掉一次就永久停止同步，
///    表现是「新同事一直登不上，且没人知道为什么」。
/// </summary>
public sealed class DirectorySyncScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformAuthOptions> options,
    ILogger<DirectorySyncScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly LdapOptions _ldap = options.Value.Ldap;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_ldap.Enabled)
        {
            logger.LogInformation("目录同步未启用（Auth:Ldap:Enabled=false），后台任务不启动。");
            return;
        }

        if (_ldap.SyncIntervalMinutes <= 0)
        {
            logger.LogInformation("目录同步间隔为 0，只允许手工触发，后台任务不启动。");
            return;
        }

        TimeSpan interval = TimeSpan.FromMinutes(_ldap.SyncIntervalMinutes);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                DirectorySyncService service = scope.ServiceProvider.GetRequiredService<DirectorySyncService>();

                await service.RunAsync(null, "schedule", stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // 单轮失败不影响下一轮：目录不可用是可恢复的。
                logger.LogError(ex, "目录同步任务执行失败，将于 {Interval} 后重试。", interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

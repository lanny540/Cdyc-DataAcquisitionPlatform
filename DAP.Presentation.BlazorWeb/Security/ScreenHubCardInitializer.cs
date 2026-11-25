using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 大屏导航卡种子：空表时补入 <see cref="ScreenHubCatalog.DefaultCards"/> 的三张默认卡。
///
/// 与能源字典初始化器同一策略——只补不覆盖：管理员删掉的卡不会在下次启动复活，
/// 管理员改过的内容也不会被种子值冲掉。失败不阻断启动（根路由页会用前端默认卡兜底渲染）。
/// </summary>
public sealed class ScreenHubCardInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<ScreenHubCardInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            DataAcquisitionPlatformDbContext db = scope.ServiceProvider
                .GetRequiredService<DataAcquisitionPlatformDbContext>();

            if (await db.ScreenHubCards.AnyAsync(cancellationToken))
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (ScreenHubCardDefault defaultCard in ScreenHubCatalog.DefaultCards)
            {
                db.ScreenHubCards.Add(new ScreenHubCard
                {
                    Id = Guid.NewGuid(),
                    Title = defaultCard.Title,
                    Description = defaultCard.Description,
                    Route = defaultCard.Route,
                    IconKey = defaultCard.IconKey,
                    Accent = defaultCard.Accent,
                    SortOrder = defaultCard.SortOrder,
                    IsEnabled = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("已补入 {Count} 张默认大屏导航卡。", ScreenHubCatalog.DefaultCards.Length);
        }
        catch (Exception ex)
        {
            // 根路由页有前端默认卡兜底，种子失败不阻断启动，但必须留日志供排查。
            logger.LogError(ex, "大屏导航卡种子写入失败，根路由将以代码内置的默认卡片渲染。");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

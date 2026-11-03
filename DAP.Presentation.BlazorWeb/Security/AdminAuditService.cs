using System.Globalization;
using System.Text;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 审计查询条件。
/// </summary>
/// <param name="From">起始时间（含），可空。</param>
/// <param name="To">截止时间（不含），可空。</param>
/// <param name="UserName">操作人账号，模糊匹配，可空。</param>
/// <param name="TargetType">对象类型，精确匹配，可空。</param>
/// <param name="Result">结果：success / failed，可空。</param>
/// <param name="Page">页码，从 1 开始。</param>
/// <param name="PageSize">每页条数，服务端上限 200。</param>
public sealed record AuditLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? UserName,
    string? TargetType,
    string? Result,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// 审计日志查询与导出（docs/AUTH_DESIGN.md 9.4）。
///
/// 审计表只读：本服务不提供任何修改或删除入口（保留期清理由后台任务按批次执行，
/// 不经过用户操作）。查询权限归 action.system.manage，属平台运维范畴。
/// </summary>
public sealed class AdminAuditService(DataAcquisitionPlatformDbContext db)
{
    /// <summary>
    /// 每页最大条数。导出走独立方法，不靠调大分页绕开限制。
    /// </summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// 导出上限。审计导出是人工核对用途，超过上限请缩小时间范围，
    /// 一次性导出几十万行既慢又没人会逐行看。
    /// </summary>
    public const int MaxExportRows = 20000;

    /// <summary>
    /// 分页查询审计记录。
    /// </summary>
    /// <param name="filter">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>一页结果。</returns>
    public async Task<AuditLogPageDto> QueryAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        IQueryable<AuditLog> query = ApplyFilter(filter);

        var total = await query.CountAsync(cancellationToken);

        List<AuditLogDto> items = await query
            .OrderByDescending(log => log.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AuditLogDto(
                log.Id,
                log.OccurredAt,
                log.UserName,
                log.DepartmentName,
                log.ActionCode,
                log.TargetType,
                log.TargetId,
                log.TargetName,
                log.Result,
                log.Detail,
                log.Ip))
            .ToListAsync(cancellationToken);

        List<string> targetTypes = await db.AuditLogs
            .Where(log => log.TargetType != null)
            .Select(log => log.TargetType!)
            .Distinct()
            .OrderBy(type => type)
            .ToListAsync(cancellationToken);

        return new AuditLogPageDto(items, total, page, pageSize, targetTypes);
    }

    /// <summary>
    /// 按当前筛选条件导出 CSV。返回内容带 UTF-8 BOM，
    /// 否则 Excel 打开中文会变乱码（这是运维实际会遇到的唯一「导出失败」）。
    /// </summary>
    /// <param name="filter">查询条件（忽略分页）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>CSV 文本，首字符为 BOM。</returns>
    public async Task<string> ExportCsvAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        List<AuditLog> logs = await ApplyFilter(filter)
            .OrderByDescending(log => log.OccurredAt)
            .Take(MaxExportRows)
            .ToListAsync(cancellationToken);

        StringBuilder builder = new();
        builder.Append('\uFEFF');
        builder.AppendLine("时间,操作人,部门,动作,对象类型,对象名称,结果,来源IP,详情");

        foreach (AuditLog log in logs)
        {
            builder.Append(CsvField.Escape(log.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.UserName));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.DepartmentName));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.ActionCode));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.TargetType));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.TargetName));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.Result));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.Ip));
            builder.Append(',');
            builder.Append(CsvField.Escape(log.Detail));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private IQueryable<AuditLog> ApplyFilter(AuditLogFilter filter)
    {
        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();

        // 必须转成 UTC：occurred_at 是 timestamptz，Npgsql 拒绝写入偏移量非零的
        // DateTimeOffset（「只支持 offset 0」）。页面传进来的日期带本地时区（+08:00），
        // 直接拼进 Where 会让整个查询抛异常，表现为「审计记录加载失败」。
        if (filter.From is { } from)
        {
            DateTimeOffset utcFrom = from.ToUniversalTime();
            query = query.Where(log => log.OccurredAt >= utcFrom);
        }

        if (filter.To is { } to)
        {
            DateTimeOffset utcTo = to.ToUniversalTime();
            query = query.Where(log => log.OccurredAt < utcTo);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var userName = filter.UserName.Trim();
            query = query.Where(log => log.UserName != null && EF.Functions.ILike(log.UserName, $"%{userName}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            var targetType = filter.TargetType.Trim();
            query = query.Where(log => log.TargetType == targetType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Result))
        {
            var result = filter.Result.Trim();
            query = query.Where(log => log.Result == result);
        }

        return query;
    }

}

/// <summary>
/// 审计保留期清理（docs/AUTH_DESIGN.md 9.4）：按 <c>Auth:Audit:RetentionDays</c>
/// 分批删除过期记录，避免一条大 DELETE 长时间锁表。
/// 保留天数设为 0 或负数时不清理。
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformAuthOptions> options,
    TimeProvider timeProvider,
    PlatformAdvisoryLock advisoryLock,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    /// <summary>
    /// 单批删除条数。批间无额外等待：配置类操作量级很小，一天最多几千行。
    /// </summary>
    private const int BatchSize = 500;

    private readonly AuditOptions _options = options.Value.Audit;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            logger.LogInformation("审计保留期清理已禁用（Auth:Audit:RetentionDays <= 0）。");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));

        logger.LogInformation(
            "审计保留期清理已启动：保留 {Days} 天，每 {Hours} 小时执行一次。",
            _options.RetentionDays, interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "审计保留期清理失败，将在下一个周期重试。");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        // 多实例下只让一个实例删：两台同时删同一批行会互相抬高空更新与锁等待，
        // 数据本身不会错，但没有必要付这份代价。
        await using IAsyncDisposable? lockHandle =
            await advisoryLock.TryAcquireAsync("audit-retention", cancellationToken);

        if (lockHandle is null)
        {
            logger.LogDebug("审计保留期清理跳过：另一实例正在执行。");
            return;
        }

        DateTimeOffset cutoff = timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        var total = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            DataAcquisitionPlatformDbContext db =
                scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

            List<AuditLog> batch = await db.AuditLogs
                .Where(log => log.OccurredAt < cutoff)
                .OrderBy(log => log.OccurredAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            db.AuditLogs.RemoveRange(batch);
            await db.SaveChangesAsync(cancellationToken);
            total += batch.Count;
        }

        if (total > 0)
        {
            logger.LogInformation(
                "已清理 {Count} 条超过 {Days} 天的审计记录（截止 {Cutoff:yyyy-MM-dd}）。",
                total, _options.RetentionDays, cutoff);
        }
    }
}

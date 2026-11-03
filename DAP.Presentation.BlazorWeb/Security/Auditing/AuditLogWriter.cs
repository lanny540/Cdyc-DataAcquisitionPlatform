using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 审计记录写入管道（docs/AUTH_DESIGN.md 9.4）。
///
/// 只记录配置类写操作，actor 永远是员工个人；失败的操作也记录（result = failed），
/// 否则连续尝试越权的痕迹会消失。detail 只保留发生变化的字段，避免整行快照撑大表。
///
/// 与设计基线的一处实现差异：设计要求「业务写入与审计同事务、审计失败整体回滚」，
/// 但排班服务是内存实现、没有事务可挂；因此 P1 的语义是「同一请求范围内、业务写完成
/// 后立即落审计」。审计写失败不影响业务结果，只记 ERROR 日志提醒管理员补录。
/// </summary>
public sealed class AuditLogWriter(
    DataAcquisitionPlatformDbContext db,
    TimeProvider timeProvider,
    ILogger<AuditLogWriter> logger)
{
    private static readonly JsonSerializerOptions DetailOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// 写入一条审计记录。
    /// </summary>
    /// <param name="actor">操作者；为 null 表示无法归到人（此时操作本身应被拒绝）。</param>
    /// <param name="actionCode">动作编码，如 shift.import。</param>
    /// <param name="targetType">对象类型，如 shift-schedule。</param>
    /// <param name="targetId">对象标识。</param>
    /// <param name="targetName">对象名称。</param>
    /// <param name="succeeded">操作结果。</param>
    /// <param name="detail">明细（序列化为 JSON 存入 detail 列）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的 <see cref="Task"/>。</returns>
    public async Task RecordAsync(
        CurrentUser? actor,
        string actionCode,
        string targetType,
        string? targetId,
        string? targetName,
        bool succeeded,
        object? detail,
        CancellationToken cancellationToken = default)
    {
        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = timeProvider.GetUtcNow(),
                UserId = actor?.UserId,
                UserName = actor?.UserName,
                DepartmentName = actor?.DepartmentName,
                ActionCode = actionCode,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName,
                Result = succeeded ? "success" : "failed",
                Detail = detail is null ? null : JsonSerializer.Serialize(detail, DetailOptions),
                Ip = actor?.Ip,
                UserAgent = actor?.UserAgent
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 审计写失败不阻断业务（见类注释的实现差异说明），但必须留 ERROR 日志。
            logger.LogError(ex, "审计记录写入失败：{ActionCode} / {TargetType} / {TargetName}。",
                actionCode, targetType, targetName);
        }
    }
}

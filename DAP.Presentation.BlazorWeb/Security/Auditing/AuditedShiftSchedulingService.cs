using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 排班服务的授权与审计装饰器（docs/AUTH_DESIGN.md 7.2）。
///
/// 排班是内存单例服务，SSR 页面直调与 HTTP 端点都会走到它——只在端点挂策略
/// 拦不住直调路径，因此授权放在服务入口：导入与清空要求排班配置权限点，
/// 拒绝与执行结果一律留痕。读方法原样转发。
/// </summary>
public sealed class AuditedShiftSchedulingService(
    IShiftSchedulingService inner,
    IServiceScopeFactory scopeFactory) : IShiftSchedulingService
{
    /// <inheritdoc />
    public Task<ShiftScheduleImportResultDto> ImportAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.ShiftManagePermission,
            "shift.import",
            "shift-schedule",
            null,
            fileName,
            _ => inner.ImportAsync(fileName, stream, cancellationToken),
            result => result.Succeeded,
            result => new {fileName, result.Succeeded, importedRows = result.Rows.Count},
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ClearAsync()
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.ShiftManagePermission,
            "shift.clear",
            "shift-schedule",
            null,
            "全部排班数据",
            _ => inner.ClearAsync(),
            cancellationToken: default);
    }

    /// <inheritdoc />
    public Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return inner.GetStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        return inner.GetCalendarAsync(from, to, teamCode, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        return inner.GetTeamScheduleAsync(teamCode, from, to, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(CancellationToken cancellationToken = default)
    {
        return inner.GetAllRowsAsync(cancellationToken);
    }
}

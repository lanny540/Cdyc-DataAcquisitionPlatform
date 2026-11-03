using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供班组排班相关 API 端点映射。
/// </summary>
public static class ShiftSchedulingEndpoints
{
    /// <summary>
    /// 上传体积限制，与 Excel 解析器保持一致，超出直接拒绝而不进入解析流程。
    /// </summary>
    private const long MaxUploadBytes = ShiftScheduleExcelParser.MaxFileSizeBytes;

    /// <summary>
    /// 允许上传的扩展名白名单。
    /// </summary>
    private static readonly string[] AllowedExtensions = [".xlsx", ".xlsm", ".xls"];

    /// <summary>
    /// 映射班组排班相关 API 端点。
    ///
    /// 读策略为「钞纸域 + 配置等级」：排班数据只服务于配置类页面，
    /// 普通用户（等级 1）与车间只读终端均不可见；写操作另需排班配置权限点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapShiftSchedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/shift-schedules")
            .WithTags("ShiftSchedules")
            .RequireBanknotePaperDomain()
            .RequireConfigureAccess();

        group.MapGet(
                "/status",
                async (IShiftSchedulingService service, CancellationToken cancellationToken) =>
                {
                    ShiftScheduleStatusDto status = await service.GetStatusAsync(cancellationToken);
                    return Results.Ok(status);
                })
            .WithName("GetShiftScheduleStatus");

        group.MapGet(
                "/validation",
                async (IShiftSchedulingService service, CancellationToken cancellationToken) =>
                {
                    IReadOnlyCollection<ShiftScheduleRowDto> rows =
                        await service.GetAllRowsAsync(cancellationToken);

                    return Results.Ok(ShiftRotationValidator.Validate(rows));
                })
            .WithName("GetShiftScheduleValidation");

        group.MapPost(
                "/import",
                async (HttpRequest request, IShiftSchedulingService service, CancellationToken cancellationToken) =>
                {
                    if (!request.HasFormContentType)
                    {
                        return Results.BadRequest(new {message = "请以 multipart/form-data 方式上传 Excel 文件。"});
                    }

                    IFormCollection form = await request.ReadFormAsync(cancellationToken);
                    if (form.Files.Count == 0)
                    {
                        return Results.BadRequest(new {message = "未选择要上传的 Excel 文件。"});
                    }

                    IFormFile file = form.Files[0];

                    if (file.Length > MaxUploadBytes)
                    {
                        return Results.BadRequest(new
                        {
                            message = $"文件体积 {file.Length / 1024} KB 超过 {MaxUploadBytes / 1024} KB 上限。"
                        });
                    }

                    var extension = Path.GetExtension(file.FileName);
                    if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(new
                        {
                            message = $"不支持的文件类型 {extension}，请上传 .xlsx 或 .xls 文件。"
                        });
                    }

                    await using Stream stream = file.OpenReadStream();
                    ShiftScheduleImportResultDto result =
                        await service.ImportAsync(file.FileName, stream, cancellationToken);

                    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
                })
            .WithName("ImportShiftSchedule")
            .RequirePermission(AuthorizationCatalog.ShiftManagePermission)
            .DisableAntiforgery();

        group.MapDelete(
                "/",
                async (IShiftSchedulingService service, CancellationToken cancellationToken) =>
                {
                    await service.ClearAsync();
                    return Results.NoContent();
                })
            .WithName("ClearShiftSchedules")
            .RequirePermission(AuthorizationCatalog.ShiftManagePermission);

        group.MapGet(
                "/calendar",
                async (
                    DateOnly? from,
                    DateOnly? to,
                    string? teamCode,
                    IShiftSchedulingService service,
                    CancellationToken cancellationToken) =>
                {
                    DateOnly today = DateOnly.FromDateTime(DateTime.Now);
                    DateOnly rangeStart = from ?? today.AddDays(-30);
                    DateOnly rangeEnd = to ?? today.AddDays(30);

                    // 日历跨度限制为一年，避免传入异常区间导致返回体过大。
                    if (rangeEnd.DayNumber - rangeStart.DayNumber > 366)
                    {
                        return Results.BadRequest(new {message = "查询跨度不能超过 366 天。"});
                    }

                    IReadOnlyCollection<DailyShiftScheduleDto> calendar = await service.GetCalendarAsync(
                        rangeStart, rangeEnd, teamCode, cancellationToken);

                    return Results.Ok(calendar);
                })
            .WithName("GetShiftScheduleCalendar");

        group.MapGet(
                "/teams/{teamCode}",
                async (
                    string teamCode,
                    DateOnly? from,
                    DateOnly? to,
                    IShiftSchedulingService service,
                    CancellationToken cancellationToken) =>
                {
                    DateOnly today = DateOnly.FromDateTime(DateTime.Now);
                    IReadOnlyCollection<DailyShiftScheduleDto> calendar = await service.GetCalendarAsync(
                        from ?? today.AddDays(-30), to ?? today.AddDays(30), teamCode, cancellationToken);

                    ShiftScheduleRowDto[] rows = calendar
                        .SelectMany(item => item.Rows)
                        .ToArray();

                    if (rows.Length == 0)
                    {
                        return Results.NotFound(new {message = "该班组在所选区间内没有排班记录。"});
                    }

                    var dutyShifts = rows.Count(item => !item.IsRestDay);

                    return Results.Ok(new TeamShiftScheduleDto(
                        rows[0].TeamCode,
                        rows[0].TeamName,
                        rows.OrderBy(item => item.WorkDate).ThenBy(item => item.StartTime).ToArray(),
                        dutyShifts,
                        Math.Round(rows.Sum(item => item.DurationHours), 2),
                        rows.Count(item => item.IsRestDay)));
                })
            .WithName("GetTeamShiftSchedule");

        return endpoints;
    }
}

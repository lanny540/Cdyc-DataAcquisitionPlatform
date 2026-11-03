using System.Text.Json;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示系统审计页的交互逻辑（docs/AUTH_DESIGN.md 9.4）。
///
/// 只读页面：不提供任何修改或删除入口。导出走浏览器直接下载 CSV，
/// 不经 WASM 中转——几万行数据读进内存再拼字符串既慢又容易把页面卡住。
/// </summary>
public partial class SystemAudit
{
    private const int PageSize = 20;

    [Inject] private IPlatformApiClient Api { get; set; } = null!;

    private IReadOnlyList<AuditLogDto> _items = [];
    private IReadOnlyList<string> _targetTypes = [];

    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private DateTime? _from = DateTime.Today.AddDays(-7);
    private DateTime? _to = DateTime.Today;
    private string? _userName;
    private string? _targetType;
    private string? _result;

    private int _total;
    private int _page = 1;

    private bool _isDetailOpen;
    private string _detailTitle = string.Empty;
    private string _detailContent = string.Empty;

    private int TotalPages => Math.Max(1, (int) Math.Ceiling(_total / (double) PageSize));

    /// <summary>
    /// 导出地址：与当前筛选条件同源，浏览器直接下载，带 Cookie 判定权限。
    /// </summary>
    private string ExportUrl => Api.BuildAuditExportUrl(From, To, _userName, _targetType, _result);

    private DateTimeOffset? From => _from is { } value ? new DateTimeOffset(value.Date) : null;

    /// <summary>
    /// 截止日期按「当天结束」处理：用户选 11-03 期望包含 11-03 全天，
    /// 而服务端按 &lt; 比较，因此这里加一天。
    /// </summary>
    private DateTimeOffset? To => _to is { } value ? new DateTimeOffset(value.Date.AddDays(1)) : null;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync(1);
    }

    private async Task LoadAsync(int page)
    {
        _isLoading = true;
        _message = null;

        try
        {
            AuditLogPageDto payload = await Api.GetAuditLogsAsync(
                From, To, _userName, _targetType, _result, page, PageSize);

            _items = payload.Items;
            _total = payload.Total;
            _page = payload.Page;
            _targetTypes = payload.TargetTypes;
        }
        catch (Exception ex)
        {
            _message = $"审计记录加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OnPageChanged(int page)
    {
        await LoadAsync(page);
    }

    private async Task ClearFilters()
    {
        _from = null;
        _to = null;
        _userName = null;
        _targetType = null;
        _result = null;
        await LoadAsync(1);
    }

    private void OpenDetail(AuditLogDto log)
    {
        _detailTitle = $"{log.ActionCode} · {log.TargetName ?? log.TargetId ?? "—"} · {log.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        _detailContent = PrettyPrint(log.Detail);
        _isDetailOpen = true;
    }

    /// <summary>
    /// 把审计 detail 的压缩 JSON 展开成可读格式。解析失败时原样返回——
    /// 审计内容不该因为格式化失败而看不见。
    /// </summary>
    private static string PrettyPrint(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "（无变更明细）";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(detail);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions {WriteIndented = true});
        }
        catch (JsonException)
        {
            return detail;
        }
    }
}

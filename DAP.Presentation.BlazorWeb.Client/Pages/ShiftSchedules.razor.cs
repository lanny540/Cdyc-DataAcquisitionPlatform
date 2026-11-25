using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示班组排班管理页的交互逻辑。
/// </summary>
public partial class ShiftSchedules
{
    /// <summary>
    /// 与服务端解析器一致的单文件体积上限。
    /// </summary>
    private const long MaxUploadBytes = 8 * 1024 * 1024;

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    /// <summary>
    /// 文件上传组件引用：MudBlazor 9 中 CustomContent 自定义上传区不再自动响应点击，
    /// 需要持有引用并显式调用 <see cref="MudBlazor.MudFileUpload{T}.OpenFilePickerAsync"/>。
    /// </summary>
    private MudFileUpload<IBrowserFile>? _fileUpload;

    private Task OpenFilePickerAsync()
    {
        return _fileUpload?.OpenFilePickerAsync() ?? Task.CompletedTask;
    }

    private ShiftScheduleStatusDto? _status;
    private ShiftScheduleImportResultDto? _importResult;
    private IReadOnlyCollection<ShiftScheduleRowDto> _rows = [];
    private IBrowserFile? _selectedFile;
    private string? _teamFilter;
    private bool _isLoading = true;
    private bool _isImporting;
    private bool _isClearing;
    private bool _isImportOpen;
    private bool _isClearConfirmOpen;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    /// <summary>
    /// 上传视图中的填写示例（宽表格式），仅用于说明表结构，不参与业务逻辑。
    /// </summary>
    private readonly ScheduleSampleRow[] _sampleRows =
    [
        new("2026-10-11", "3", "2", "1"),
        new("2026-10-12", "3", "2", "1"),
        new("2026-10-13", "4", "3", "2")
    ];

    private sealed record ScheduleSampleRow(string Date, string Night, string Day, string Middle);

    private int DutyDayCount => _rows.Select(item => item.WorkDate).Distinct().Count();

    private IReadOnlyCollection<ShiftScheduleRowDto> _visibleRows =>
        string.IsNullOrWhiteSpace(_teamFilter)
            ? _rows
            : _rows.Where(item => string.Equals(item.TeamCode, _teamFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

        try
        {
            _status = await PlatformApiClient.GetShiftScheduleStatusAsync();

            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            IReadOnlyList<DailyShiftScheduleDto> calendar =
                await PlatformApiClient.GetShiftScheduleCalendarAsync(
                    today.AddDays(-180), today.AddDays(180));

            _rows = calendar.SelectMany(item => item.Rows).ToArray();
        }
        catch (Exception ex)
        {
            _message = $"排班数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OnTeamFilterChangedAsync(string value)
    {
        _teamFilter = value;
        await Task.CompletedTask;
    }

    private void OpenImportDialog()
    {
        _importResult = null;
        _selectedFile = null;
        _isImportOpen = true;
    }

    private void CloseImportDialog()
    {
        _isImportOpen = false;
        _selectedFile = null;
    }

    private void OnFileSelected(IBrowserFile? file)
    {
        _selectedFile = file;
        _importResult = null;
    }

    private void ClearSelectedFile()
    {
        _selectedFile = null;
        _importResult = null;
    }

    private async Task ImportAsync()
    {
        if (_selectedFile is null)
        {
            return;
        }

        _isImporting = true;
        _importResult = null;
        _message = null;

        try
        {
            await using Stream stream = _selectedFile.OpenReadStream(MaxUploadBytes);
            ShiftScheduleImportResultDto result =
                await PlatformApiClient.ImportShiftScheduleAsync(stream, _selectedFile.Name);

            _importResult = result;

            _message = result.Succeeded
                ? $"已导入 {result.Rows.Count} 条排班记录。"
                : $"导入未成功，发现 {result.Validation.Issues.Count(item => item.Severity == "error")} 个解析错误，原有数据未被覆盖。";
            _messageSeverity = result.Succeeded ? Severity.Success : Severity.Error;

            if (result.Succeeded)
            {
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _message = $"导入失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isImporting = false;
        }
    }

    private void OpenClearDialog()
    {
        _isClearConfirmOpen = true;
    }

    private void CloseClearDialog()
    {
        _isClearConfirmOpen = false;
    }

    private async Task ConfirmClearAsync()
    {
        _isClearing = true;

        try
        {
            await PlatformApiClient.ClearShiftSchedulesAsync();
            _message = "排班数据已清空。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"清空失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isClearing = false;
            _isClearConfirmOpen = false;
        }
    }
}

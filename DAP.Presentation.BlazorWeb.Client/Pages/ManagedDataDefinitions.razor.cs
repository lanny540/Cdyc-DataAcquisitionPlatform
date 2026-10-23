using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示后台数据节点配置页面的交互逻辑。
/// 节点配置以选择采集服务器为前提，编辑表单由 Components/ManagedDataDefinitionEditor 承载。
/// </summary>
public partial class ManagedDataDefinitions
{
    private const string AllTypeFilter = "全部";

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private readonly List<ManagedDataDefinitionDto> _definitions = [];

    private Guid? _editingId;
    private string _searchTerm = string.Empty;
    private string _typeFilter = AllTypeFilter;
    private bool _isLoading;
    private bool _isDeleting;
    private bool _isEditorOpen;
    private bool _isDeleteConfirmOpen;
    private Guid? _executingDefinitionId;
    private bool _isCollectExecution;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private ManagedDataDefinitionDto? _pendingDeleteDefinition;
    private ManagedDataExecutionResultDto? _lastExecutionResult;

    private IEnumerable<ManagedDataDefinitionDto> FilteredDefinitions => _definitions
        .Where(item => _typeFilter == AllTypeFilter ||
                       string.Equals(item.AcquisitionType, _typeFilter, StringComparison.Ordinal))
        .Where(item => string.IsNullOrWhiteSpace(_searchTerm) ||
                       item.Code.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Name.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Department.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.ProcessCode.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.DataCategory.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Identifier.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.AcquisitionType)
        .ThenBy(item => item.Code);

    private int EnabledDefinitionCount => _definitions.Count(item => item.IsEnabled);

    private int ModbusDefinitionCount =>
        _definitions.Count(item =>
            string.Equals(item.AcquisitionType, AcquisitionServerCatalog.Modbus, StringComparison.Ordinal));

    private int HistorianDefinitionCount =>
        _definitions.Count(item =>
            string.Equals(item.AcquisitionType, AcquisitionServerCatalog.Historian, StringComparison.Ordinal));

    private Color LatestExecutionColor =>
        _lastExecutionResult is {Success: true} ? Color.Success : Color.Error;

    private IEnumerable<string> TypeFilterOptions => _definitions
        .Select(item => item.AcquisitionType)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item)
        .Prepend(AllTypeFilter);

    private bool IsExecuting => _executingDefinitionId.HasValue;

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
            _definitions.Clear();
            _definitions.AddRange(await PlatformApiClient.GetManagedDataDefinitionsAsync());

            _typeFilter = TypeFilterOptions.Contains(_typeFilter) ? _typeFilter : AllTypeFilter;
        }
        catch (Exception ex)
        {
            _message = $"数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Warning;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>打开共享编辑抽屉并载入指定节点的最新配置。</summary>
    private async Task EditAsync(ManagedDataDefinitionDto definition)
    {
        _editingId = definition.Id;
        _isEditorOpen = true;
        await InvokeAsync(StateHasChanged);
    }

    private void OpenCreateDrawer()
    {
        _editingId = null;
        _message = null;
        _isEditorOpen = true;
    }

    private async Task OnEditorSaved(ManagedDataDefinitionDto saved)
    {
        _message = $"采集节点 {saved.Code} 已保存。";
        _messageSeverity = Severity.Success;
        await LoadAsync();
    }

    private void RequestDelete(ManagedDataDefinitionDto definition)
    {
        _pendingDeleteDefinition = definition;
        _isDeleteConfirmOpen = true;
    }

    private void CancelDelete()
    {
        _pendingDeleteDefinition = null;
        _isDeleteConfirmOpen = false;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteDefinition is null)
        {
            return;
        }

        ManagedDataDefinitionDto definition = _pendingDeleteDefinition;
        _isDeleting = true;

        try
        {
            var deleted = await PlatformApiClient.DeleteManagedDataDefinitionAsync(definition.Id);
            if (!deleted)
            {
                SetMessage($"采集节点 {definition.Code} 不存在或已被删除。", Severity.Warning);
                return;
            }

            if (_editingId == definition.Id)
            {
                _editingId = null;
                _isEditorOpen = false;
            }

            SetMessage($"采集节点 {definition.Code} 已删除。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"采集节点删除失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isDeleting = false;
            _pendingDeleteDefinition = null;
            _isDeleteConfirmOpen = false;
        }
    }

    private async Task DebugReadAsync(ManagedDataDefinitionDto definition)
    {
        await ExecuteDefinitionAsync(definition, false);
    }

    private async Task CollectAsync(ManagedDataDefinitionDto definition)
    {
        await ExecuteDefinitionAsync(definition, true);
    }

    private void OpenDetails(ManagedDataDefinitionDto definition)
    {
        NavigationManager.NavigateTo($"/managed-data-definitions/{definition.Id}");
    }

    private async Task ExecuteDefinitionAsync(ManagedDataDefinitionDto definition, bool isCollect)
    {
        _executingDefinitionId = definition.Id;
        _isCollectExecution = isCollect;
        _message = null;

        try
        {
            _lastExecutionResult = isCollect
                ? await PlatformApiClient.CollectManagedDataDefinitionAsync(definition.Id)
                : await PlatformApiClient.DebugReadManagedDataDefinitionAsync(definition.Id);

            if (isCollect && _lastExecutionResult.Success)
            {
                await LoadAsync();
            }

            SetMessage(
                _lastExecutionResult.Success
                    ? _lastExecutionResult.StatusMessage
                    : $"执行失败：{_lastExecutionResult.ErrorMessage ?? _lastExecutionResult.StatusMessage}",
                _lastExecutionResult.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"执行失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _executingDefinitionId = null;
            _isCollectExecution = false;
        }
    }

    private bool IsExecutingDefinition(Guid id, bool isCollect)
    {
        return _executingDefinitionId == id && _isCollectExecution == isCollect;
    }

    private static string GetIntervalDisplayText(int intervalSeconds)
    {
        return intervalSeconds >= 60 && intervalSeconds % 60 == 0
            ? $"每 {intervalSeconds / 60} 分钟执行 1 次"
            : $"每 {intervalSeconds} 秒执行 1 次";
    }

    private static Color GetTypeChipColor(string acquisitionType)
    {
        return acquisitionType switch
        {
            AcquisitionServerCatalog.Historian => Color.Info,
            AcquisitionServerCatalog.InfluxDb => Color.Secondary,
            AcquisitionServerCatalog.Modbus => Color.Primary,
            AcquisitionServerCatalog.Mqtt => Color.Success,
            AcquisitionServerCatalog.OpcDa => Color.Warning,
            AcquisitionServerCatalog.HttpApi => Color.Tertiary,
            _ => Color.Default
        };
    }

    private void SetMessage(string message, Severity severity)
    {
        _message = message;
        _messageSeverity = severity;
    }
}

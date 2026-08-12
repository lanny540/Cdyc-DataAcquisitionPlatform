using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示采集点管理页面的交互逻辑。
/// </summary>
public partial class CollectionPoints
{
    [Inject]
    private IPlatformApiClient PlatformApiClient { get; set; } = default!;

    private readonly List<CollectionPointDto> _collectionPoints = [];
    private Guid? _editingId;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _protocol = "Modbus TCP";
    private string _endpoint = string.Empty;
    private bool _isEnabled = true;
    private bool _isLoading;
    private bool _isDeleting;
    private bool _isEditorOpen;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private CollectionPointDto? _pendingDeletePoint;
    private Guid _editorRenderKey = Guid.NewGuid();

    private int EnabledCollectionPointCount => _collectionPoints.Count(point => point.IsEnabled);

    private int OnlineCollectionPointCount => _collectionPoints.Count(point => string.Equals(point.CommunicationStatus, "在线", StringComparison.Ordinal));

    private string EditorTitle => _editingId.HasValue ? "编辑采集点" : "新增采集点";

    private string EditorDescription => _editingId.HasValue
        ? "右侧抽屉内直接修改采集点关键配置，保存后立即回写到服务端。"
        : "在抽屉中录入新的采集点信息，创建完成后会同步刷新列表。";

    private string SaveButtonText => _editingId.HasValue ? "保存更改" : "创建采集点";

    /// <inheritdoc />
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
            _collectionPoints.Clear();
            _collectionPoints.AddRange(await PlatformApiClient.GetCollectionPointsAsync());
        }
        catch (Exception ex)
        {
            _message = $"采集点列表加载失败：{ex.Message}";
            _messageSeverity = Severity.Warning;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task EditAsync(CollectionPointDto point)
    {
        _editingId = point.Id;
        _code = point.Code;
        _name = point.Name;
        _protocol = point.Protocol;
        _endpoint = point.Endpoint;
        _isEnabled = point.IsEnabled;
        _message = $"已载入采集点 {point.Code}，可以直接编辑后保存。";
        _messageSeverity = Severity.Info;
        ResetEditorRenderKey();
        _isEditorOpen = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_code) ||
            string.IsNullOrWhiteSpace(_name) ||
            string.IsNullOrWhiteSpace(_protocol) ||
            string.IsNullOrWhiteSpace(_endpoint))
        {
            _message = "编码、名称、协议和端点不能为空。";
            _messageSeverity = Severity.Warning;
            return;
        }

        try
        {
            var request = new CollectionPointUpsertRequest(
                _editingId,
                _code,
                _name,
                _protocol,
                _endpoint,
                _isEnabled,
                "Server");

            await PlatformApiClient.UpsertCollectionPointAsync(request);
            _message = $"采集点 {_code} 已保存到服务端。";
            _messageSeverity = Severity.Success;
            _isEditorOpen = false;
            ResetEditor();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"采集点保存失败：{ex.Message}";
            _messageSeverity = Severity.Error;
        }
    }

    private void RequestDelete(CollectionPointDto point)
    {
        _pendingDeletePoint = point;
    }

    private void CancelDelete()
    {
        if (_isDeleting)
        {
            return;
        }

        _pendingDeletePoint = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeletePoint is null)
        {
            return;
        }

        var point = _pendingDeletePoint;
        _isDeleting = true;

        try
        {
            var deleted = await PlatformApiClient.DeleteCollectionPointAsync(point.Id);
            if (!deleted)
            {
                _message = $"采集点 {point.Code} 不存在或已被删除。";
                _messageSeverity = Severity.Warning;
                return;
            }

            if (_editingId == point.Id)
            {
                ResetEditor();
            }

            _message = $"采集点 {point.Code} 已从服务端删除。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"采集点删除失败：{ex.Message}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isDeleting = false;
            _pendingDeletePoint = null;
        }
    }

    private void ResetEditor()
    {
        _editingId = null;
        _code = string.Empty;
        _name = string.Empty;
        _protocol = "Modbus TCP";
        _endpoint = string.Empty;
        _isEnabled = true;
        ResetEditorRenderKey();
    }

    private void OpenCreateDrawer()
    {
        ResetEditor();
        _message = null;
        _isEditorOpen = true;
    }

    private void CancelEditor()
    {
        ResetEditor();
        _isEditorOpen = false;
    }

    private void CloseEditor()
    {
        _isEditorOpen = false;
    }

    private void ResetEditorRenderKey()
    {
        _editorRenderKey = Guid.NewGuid();
    }
}

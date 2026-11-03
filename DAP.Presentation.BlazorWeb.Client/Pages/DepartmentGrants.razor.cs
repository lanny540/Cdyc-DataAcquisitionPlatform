using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示部门与授权矩阵页的交互逻辑。
///
/// 授权挂在部门上（单值传播），矩阵改动走「勾选 → 行内保存」两步，
/// 避免每次勾选都发一次请求；系统域不进矩阵。
/// </summary>
public partial class DepartmentGrants
{
    [Inject] private IPlatformApiClient Api { get; set; } = null!;

    private IReadOnlyList<AdminDepartmentDto> _departments = [];
    private IReadOnlyList<AdminDomainDto> _domains = [];

    /// <summary>
    /// 业务域（排除系统域）的轻量视图，用于矩阵表头。
    /// </summary>
    private IReadOnlyList<(Guid Id, string Name)> _businessDomains = [];

    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    /// <summary>
    /// 未保存的勾选改动：部门 Id → 勾选中的域 Id 集合。保存后清空。
    /// </summary>
    private readonly Dictionary<Guid, HashSet<Guid>> _pendingGrants = new();

    private bool _isEditorOpen;
    private bool _isCreating = true;
    private bool _isSaving;
    private Guid _editingDepartmentId;
    private string _editorCode = string.Empty;
    private string _editorName = string.Empty;
    private string? _editorExternalCode;
    private bool _editorEnabled = true;

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
            _domains = await Api.GetAdminDomainsAsync();
            _businessDomains = _domains
                .Where(domain => !domain.IsSystem)
                .Select(domain => (domain.Id, domain.Name))
                .ToList();
            _departments = await Api.GetAdminDepartmentsAsync();
            _pendingGrants.Clear();
        }
        catch (Exception ex)
        {
            _message = $"部门与授权数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool IsGranted(AdminDepartmentDto department, (Guid Id, string Name) domain)
    {
        if (_pendingGrants.TryGetValue(department.Id, out var pending))
        {
            return pending.Contains(domain.Id);
        }

        return department.GrantedDomainIds.Contains(domain.Id);
    }

    private void ToggleGrant(AdminDepartmentDto department, (Guid Id, string Name) domain, bool granted)
    {
        if (!_pendingGrants.TryGetValue(department.Id, out var pending))
        {
            pending = [.. department.GrantedDomainIds];
            _pendingGrants[department.Id] = pending;
        }

        if (granted)
        {
            pending.Add(domain.Id);
        }
        else
        {
            pending.Remove(domain.Id);
        }
    }

    /// <summary>
    /// 该部门是否有未保存的勾选改动。没有改动时保存按钮禁用。
    /// </summary>
    private bool? UnsavedGrants(AdminDepartmentDto department)
    {
        // 返回值只用于按钮禁用判定：有差异返回 false（启用），无差异返回 null（禁用）。
        if (!_pendingGrants.TryGetValue(department.Id, out var pending))
        {
            return null;
        }

        var current = department.GrantedDomainIds.ToHashSet();
        return pending.SetEquals(current) ? null : false;
    }

    private async Task SaveGrantsAsync(AdminDepartmentDto department)
    {
        if (!_pendingGrants.TryGetValue(department.Id, out var pending))
        {
            return;
        }

        try
        {
            await Api.UpdateAdminDepartmentGrantsAsync(
                department.Id,
                new AdminDepartmentGrantsRequest([.. pending]));

            _pendingGrants.Remove(department.Id);
            _message = $"已更新「{department.Name}」的域授权，该部门用户重新登录后生效。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = UserFacingErrors.Describe(ex);
            _messageSeverity = Severity.Error;
        }
    }

    private void OpenCreateDialog()
    {
        _isCreating = true;
        _editingDepartmentId = Guid.Empty;
        _editorCode = string.Empty;
        _editorName = string.Empty;
        _editorExternalCode = null;
        _editorEnabled = true;
        _isEditorOpen = true;
    }

    private void OpenEditDialog(AdminDepartmentDto department)
    {
        _isCreating = false;
        _editingDepartmentId = department.Id;
        _editorCode = department.Code;
        _editorName = department.Name;
        _editorExternalCode = department.ExternalCode;
        _editorEnabled = department.IsEnabled;
        _isEditorOpen = true;
    }

    private void CloseEditor()
    {
        _isEditorOpen = false;
    }

    private async Task SaveDepartmentAsync()
    {
        _isSaving = true;

        try
        {
            var request = new AdminDepartmentUpsertRequest(
                _editorCode.Trim(),
                _editorName.Trim(),
                string.IsNullOrWhiteSpace(_editorExternalCode) ? null : _editorExternalCode.Trim(),
                _editorEnabled);

            if (_isCreating)
            {
                await Api.CreateAdminDepartmentAsync(request);
                _message = $"已创建部门「{request.Name}」。";
            }
            else
            {
                await Api.UpdateAdminDepartmentAsync(_editingDepartmentId, request);
                _message = $"已更新部门「{request.Name}」。";
            }

            _messageSeverity = Severity.Success;
            _isEditorOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = UserFacingErrors.Describe(ex);
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isSaving = false;
        }
    }
}

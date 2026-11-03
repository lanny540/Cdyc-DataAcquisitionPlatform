using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示用户与角色页的交互逻辑（docs/AUTH_DESIGN.md 9.3 / P2）。
///
/// 页面按操作者身份裁剪：超管可管全部账号、可分配全部启用角色、可移交超管；
/// 部门管理员只见本部门账号，角色下拉只有「普通用户 / 技术人员」，且看不到部门选择。
/// 这些裁剪只是界面层，服务端在 <c>AdminUserService</c> 里同样校验——只藏下拉项不安全。
/// </summary>
public partial class UserManagement
{
    [Inject] private IPlatformApiClient Api { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private IReadOnlyList<AdminUserDto> _users = [];
    private IReadOnlyList<AdminRoleDto> _roles = [];
    private IReadOnlyList<AdminDepartmentDto> _departments = [];

    private HashSet<AdminUserDto> _selected = [];
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _isSuperAdmin;
    private string? _keyword;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private bool _isEditorOpen;
    private bool _isCreating = true;
    private bool _editingSuperAdmin;
    private Guid _editingUserId;
    private string _editorUserName = string.Empty;
    private string _editorDisplayName = string.Empty;
    private string? _editorEmail;
    private string _editorPassword = string.Empty;
    private Guid? _editorDepartmentId;
    private string? _editorDepartmentName;
    private bool _editorEnabled = true;
    private IReadOnlyCollection<Guid> _editorRoleIds = [];

    private bool _isResetOpen;
    private Guid _resetUserId;
    private string _resetUserName = string.Empty;
    private string _resetPassword = string.Empty;

    private bool _isHandoverOpen;
    private Guid _handoverUserId;

    /// <summary>
    /// 可接任超管的账号：启用、非超管、非当前账号。
    /// </summary>
    private IEnumerable<AdminUserDto> HandoverCandidates => _users
        .Where(user => user.IsEnabled && !user.IsSuperAdmin && user.UserName != _currentUserName);

    private string? _currentUserName;

    protected override async Task OnInitializedAsync()
    {
        await ReadIdentityAsync();
        await LoadAsync();
    }

    private async Task ReadIdentityAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState state = await AuthenticationStateTask;
        ClaimsPrincipal principal = state.User;

        _isSuperAdmin = principal.HasClaim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true");
        _currentUserName = principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.UserName)?.Value;
    }

    private async Task LoadAsync()
    {
        _isLoading = true;

        try
        {
            _users = await Api.GetAdminUsersAsync(_keyword);
            _roles = await Api.GetAdminRolesAsync();

            if (_isSuperAdmin)
            {
                _departments = await Api.GetAdminDepartmentsAsync();
            }

            _selected.Clear();
        }
        catch (Exception ex)
        {
            _message = $"账号数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task ClearFilters()
    {
        _keyword = null;
        await LoadAsync();
    }

    private void OpenCreateDialog()
    {
        _isCreating = true;
        _editingSuperAdmin = false;
        _editingUserId = Guid.Empty;
        _editorUserName = string.Empty;
        _editorDisplayName = string.Empty;
        _editorEmail = null;
        _editorPassword = string.Empty;
        _editorDepartmentId = null;
        _editorDepartmentName = null;
        _editorEnabled = true;
        _editorRoleIds = [];
        _isEditorOpen = true;
    }

    private void OpenEditDialog(AdminUserDto user)
    {
        _isCreating = false;
        _editingSuperAdmin = user.IsSuperAdmin;
        _editingUserId = user.Id;
        _editorUserName = user.UserName;
        _editorDisplayName = user.DisplayName;
        _editorEmail = user.Email;
        _editorPassword = string.Empty;
        _editorDepartmentId = user.DepartmentId;
        _editorDepartmentName = user.DepartmentName;
        _editorEnabled = user.IsEnabled;
        _editorRoleIds = [.. user.Roles.Select(role => role.Id)];
        _isEditorOpen = true;
    }

    private void CloseEditor()
    {
        _isEditorOpen = false;
    }

    private async Task SaveEditorAsync()
    {
        _isSaving = true;

        try
        {
            if (_isCreating)
            {
                await Api.CreateAdminUserAsync(new AdminUserCreateRequest(
                    _editorUserName.Trim(),
                    _editorDisplayName.Trim(),
                    _editorDepartmentId,
                    _editorEmail,
                    _editorPassword,
                    SelectedRoleIds()));

                _message = $"已创建账号「{_editorDisplayName.Trim()}」，初始密码已设置，首次登录强制修改。";
            }
            else
            {
                await Api.UpdateAdminUserAsync(_editingUserId,
                    new AdminUserUpdateRequest(_editorDisplayName.Trim(), _editorEmail, _editorEnabled));

                // 角色单独提交：与基本信息分开，便于审计区分「改了启停」还是「改了角色」。
                await Api.UpdateAdminUserRolesAsync(_editingUserId, new AdminUserRolesRequest(SelectedRoleIds()));

                // 部门变更仅超管可执行；值未变时服务端会直接返回成功。
                if (_isSuperAdmin && !_editingSuperAdmin)
                {
                    await Api.UpdateAdminUserDepartmentAsync(_editingUserId,
                        new AdminUserDepartmentRequest(_editorDepartmentId));
                }

                _message = $"已更新账号「{_editorUserName}」，角色变更在该用户下次登录时生效。";
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

    private void OpenResetPasswordDialog(AdminUserDto user)
    {
        _resetUserId = user.Id;
        _resetUserName = user.UserName;
        _resetPassword = string.Empty;
        _isResetOpen = true;
    }

    private async Task ResetPasswordAsync()
    {
        _isSaving = true;

        try
        {
            await Api.ResetAdminUserPasswordAsync(_resetUserId, new AdminUserResetPasswordRequest(_resetPassword));
            _message = $"已重置「{_resetUserName}」的密码，该账号下次登录必须修改密码。";
            _messageSeverity = Severity.Success;
            _isResetOpen = false;
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

    private async Task SetSelectedEnabledAsync(bool isEnabled)
    {
        if (_selected.Count == 0)
        {
            return;
        }

        try
        {
            await Api.SetAdminUsersEnabledAsync(new AdminUserBatchStatusRequest(
                [.. _selected.Select(user => user.Id)], isEnabled));

            _message = $"已{(isEnabled ? "启用" : "停用")} {_selected.Count} 个账号。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = UserFacingErrors.Describe(ex);
            _messageSeverity = Severity.Error;
        }
    }

    private void OpenHandoverDialog()
    {
        _handoverUserId = Guid.Empty;
        _isHandoverOpen = true;
    }

    private async Task HandoverAsync()
    {
        _isSaving = true;

        try
        {
            await Api.HandoverSuperAdminAsync(new SuperAdminHandoverRequest(_handoverUserId));
            _isHandoverOpen = false;

            // 移交后当前会话的 claims 仍是旧身份，必须立即退出——否则旧会话在
            // 绝对有效期（默认 12 小时）内仍按超管放行。这里用隐藏表单原生提交，
            // 与顶栏登出同一路径：电路里拿不到 HttpContext，签不掉 Cookie。
            await JS.InvokeVoidAsync("eval",
                "document.getElementById('platform-handover-logout')?.submit();");
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

    /// <summary>
    /// 取当前选中的角色标识。MudSelect 的多选集合元素可空，这里统一过滤。
    /// </summary>
    private Guid[] SelectedRoleIds()
    {
        return [.. _editorRoleIds];
    }
}

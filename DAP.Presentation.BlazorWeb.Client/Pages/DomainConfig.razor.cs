using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示域与页面配置页的交互逻辑。
///
/// 页面等级误配（把配置页调成查看类）会造成越权可见且不报错，
/// 因此改档必须二次确认（docs/AUTH_DESIGN.md 第 4 节）。
/// </summary>
public partial class DomainConfig
{
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync(clearMessage: false);
    }

    /// <summary>
    /// 加载域与页面配置。
    ///
    /// <paramref name="clearMessage"/> 为 false 时保留当前提示——变更成功后要「提示 + 刷新列表」，
    /// 若刷新时无条件清空提示，用户永远看不到「已保存」（这也是本页早先的缺陷）。
    /// </summary>
    private async Task LoadAsync(bool clearMessage = true)
    {
        _isLoading = true;

        if (clearMessage)
        {
            _message = null;
        }

        try
        {
            _domains = await Api.GetAdminDomainsAsync();
        }
        catch (Exception ex)
        {
            _message = $"域配置加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 修改页面等级前先弹确认框：等级决定谁能看到这个页面，
    /// 放开（1→2）与收紧（2→1）都让操作者显式确认，避免误触。
    /// </summary>
    private void RequestAccessLevelChangeAsync(AdminDomainPageDto page, int newLevel)
    {
        if (newLevel == page.AccessLevel)
        {
            return;
        }

        var direction = newLevel > page.AccessLevel ? "放开" : "收紧";

        OpenConfirm(
            "确认修改页面等级",
            $"「{page.Title}」的等级将由 {page.AccessLevel} 改为 {newLevel}（{direction}）。" +
            "等级决定谁能看到这个页面，请确认与业务授权一致。",
            async () =>
            {
                await ExecuteUpdateAsync(page, new AdminDomainPageUpdateRequest(newLevel, null, null),
                    $"已更新「{page.Title}」的页面等级。");
            });
    }

    private void RequestToggleAsync(AdminDomainPageDto page, bool enabled)
    {
        OpenConfirm(
            enabled ? "确认启用页面" : "确认停用页面",
            enabled
                ? $"启用后，「{page.Title}」将重新出现在授权用户的导航中。"
                : $"停用后，「{page.Title}」将从所有用户的导航中隐藏，直接输入地址也会被拦到无权限页。",
            async () =>
            {
                await ExecuteUpdateAsync(page, new AdminDomainPageUpdateRequest(null, null, enabled),
                    enabled ? $"已启用「{page.Title}」。" : $"已停用「{page.Title}」。");
            });
    }

    /// <summary>
    /// 恢复代码默认值：source 还原为 code，下一次启动对账按代码刷新。
    /// </summary>
    private void RequestResetAsync(AdminDomainPageDto page)
    {
        OpenConfirm(
            "恢复代码默认值",
            $"「{page.Title}」的等级、归属与启停将还原为代码声明的默认值，在下一次服务重启后生效。",
            async () =>
            {
                try
                {
                    await Api.ResetAdminDomainPageAsync(page.Id);
                    _message = $"已恢复「{page.Title}」的代码默认值。";
                    _messageSeverity = Severity.Success;
                    await LoadAsync(clearMessage: false);
                }
                catch (Exception ex)
                {
                    _message = UserFacingErrors.Describe(ex);
                    _messageSeverity = Severity.Error;
                }
            });
    }

    private async Task ExecuteUpdateAsync(
        AdminDomainPageDto page,
        AdminDomainPageUpdateRequest request,
        string successMessage)
    {
        try
        {
            await Api.UpdateAdminDomainPageAsync(page.Id, request);
            _message = successMessage;
            _messageSeverity = Severity.Success;
            await LoadAsync(clearMessage: false);
        }
        catch (Exception ex)
        {
            _message = UserFacingErrors.Describe(ex);
            _messageSeverity = Severity.Error;
        }
    }

    private void OpenCreateDomain()
    {
        _isCreatingDomain = true;
        _editingDomainId = Guid.Empty;
        _editorDomainCode = string.Empty;
        _editorDomainName = string.Empty;
        _editorDomainSortOrder = _domains.Count == 0
            ? 0
            : _domains.Max(domain => domain.SortOrder) + 1;
        _editorDomainEnabled = true;
        _editorDomainVisibleWhenEmpty = false;
        _editorDomainIsSystem = false;
        _isDomainEditorOpen = true;
    }

    private void OpenDomainEditor(AdminDomainDto domain)
    {
        _isCreatingDomain = false;
        _editingDomainId = domain.Id;
        _editorDomainCode = domain.Code;
        _editorDomainName = domain.Name;
        _editorDomainSortOrder = domain.SortOrder;
        _editorDomainEnabled = domain.IsEnabled;
        _editorDomainVisibleWhenEmpty = domain.VisibleWhenEmpty;
        _editorDomainIsSystem = domain.IsSystem;
        _isDomainEditorOpen = true;
    }

    private void CloseDomainEditor()
    {
        _isDomainEditorOpen = false;
    }

    private async Task SaveDomainAsync()
    {
        _isSaving = true;

        try
        {
            var request = new AdminDomainUpsertRequest(
                _editorDomainCode,
                _editorDomainName,
                _editorDomainSortOrder,
                _editorDomainEnabled,
                _editorDomainVisibleWhenEmpty,
                _editorDomainIsSystem);

            if (_isCreatingDomain)
            {
                await Api.CreateAdminDomainAsync(request);
                _message = $"已创建域「{_editorDomainName.Trim()}」。页面代码声明该编码后会在启动时自动归入。";
            }
            else
            {
                await Api.UpdateAdminDomainAsync(_editingDomainId, request);
                _message = $"已更新域「{_editorDomainName.Trim()}」。";
            }

            _messageSeverity = Severity.Success;
            _isDomainEditorOpen = false;
            await LoadAsync(clearMessage: false);
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

    private void RequestDeleteDomainAsync(AdminDomainDto domain)
    {
        OpenConfirm(
            "确认删除域",
            $"删除「{domain.Name}」（{domain.Code}）后，该域的部门授权一并移除。此操作不可恢复。",
            async () =>
            {
                try
                {
                    await Api.DeleteAdminDomainAsync(domain.Id);
                    _message = $"已删除域「{domain.Name}」。";
                    _messageSeverity = Severity.Success;
                    await LoadAsync(clearMessage: false);
                }
                catch (Exception ex)
                {
                    _message = UserFacingErrors.Describe(ex);
                    _messageSeverity = Severity.Error;
                }
            });
    }

    private void OpenPageEditor(AdminDomainPageDto page)
    {
        _editingPageId = page.Id;
        _editorPageKey = page.PageKey;
        _editorPageRoute = page.Route;
        _editorPageTitle = page.Title;
        _editorPageGroupTitle = page.GroupTitle ?? string.Empty;
        _editorPageSortOrder = page.SortOrder;
        _editorPageDomainId = _domains
            .FirstOrDefault(domain => string.Equals(domain.Code, page.DomainCode, StringComparison.Ordinal))?.Id
            ?? Guid.Empty;
        _isPageEditorOpen = true;
    }

    private void ClosePageEditor()
    {
        _isPageEditorOpen = false;
    }

    private async Task SavePageAsync()
    {
        AdminDomainPageDto? page = _domains
            .SelectMany(domain => domain.Pages)
            .FirstOrDefault(item => item.Id == _editingPageId);

        if (page is null)
        {
            _message = "页面已不存在，请刷新后重试。";
            _messageSeverity = Severity.Error;
            _isPageEditorOpen = false;
            return;
        }

        _isSaving = true;

        try
        {
            // 只提交本对话框负责的字段：等级与启停有各自的即时开关，
            // 一起提交会把「打开对话框后另一个标签页改过的等级」覆盖回去。
            await Api.UpdateAdminDomainPageAsync(page.Id, new AdminDomainPageUpdateRequest(
                AccessLevel: null,
                DomainId: _editorPageDomainId == Guid.Empty ? null : _editorPageDomainId,
                IsEnabled: null,
                Title: _editorPageTitle,
                GroupTitle: _editorPageGroupTitle,
                SortOrder: _editorPageSortOrder));

            _message = $"已更新「{_editorPageTitle.Trim()}」的页面配置。";
            _messageSeverity = Severity.Success;
            _isPageEditorOpen = false;
            await LoadAsync(clearMessage: false);
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

    private void RequestDeletePageAsync(AdminDomainPageDto page)
    {
        OpenConfirm(
            "确认删除页面记录",
            $"「{page.Title}」（{page.Route}）已在代码中下线，删除记录只是清理残留配置，不影响任何在线页面。",
            async () =>
            {
                try
                {
                    await Api.DeleteAdminDomainPageAsync(page.Id);
                    _message = $"已删除下线页面记录「{page.Title}」。";
                    _messageSeverity = Severity.Success;
                    await LoadAsync(clearMessage: false);
                }
                catch (Exception ex)
                {
                    _message = UserFacingErrors.Describe(ex);
                    _messageSeverity = Severity.Error;
                }
            });
    }

    private void OpenConfirm(string title, string text, Func<Task> action)
    {
        _confirmTitle = title;
        _confirmText = text;
        _pendingAction = action;
        _isConfirmOpen = true;
    }

    private void CancelConfirm()
    {
        _isConfirmOpen = false;
        _pendingAction = null;
    }

    private async Task AcceptConfirmAsync()
    {
        _isConfirmOpen = false;
        var action = _pendingAction;
        _pendingAction = null;

        if (action is not null)
        {
            await action();
        }
    }
}

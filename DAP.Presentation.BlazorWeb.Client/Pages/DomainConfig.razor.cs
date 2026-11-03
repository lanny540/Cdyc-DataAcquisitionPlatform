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
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

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
                    await LoadAsync();
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
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = UserFacingErrors.Describe(ex);
            _messageSeverity = Severity.Error;
        }
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

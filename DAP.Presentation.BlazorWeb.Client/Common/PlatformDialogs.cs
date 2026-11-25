using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Common;

/// <summary>
/// 提供平台各页面统一的 <see cref="MudBlazor.DialogOptions"/> 配置，
/// 避免在每个页面重复声明对话框尺寸、位置与关闭行为。
/// 表单新增/编辑已全站改为整页视图（与采集服务器页一致），这里只保留确认对话框。
/// </summary>
public static class PlatformDialogs
{
    /// <summary>
    /// 居中确认对话框：用于删除等不可撤销操作的二次确认。
    /// </summary>
    public static DialogOptions Confirm { get; } = new()
    {
        Position = DialogPosition.Center,
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        NoHeader = true,
        CloseButton = false,
        BackdropClick = true,
        CloseOnEscapeKey = true,
        CloseOnNavigation = true
    };
}

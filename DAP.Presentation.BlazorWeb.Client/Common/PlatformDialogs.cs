using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Common;

/// <summary>
/// 提供平台各页面统一的 <see cref="MudBlazor.DialogOptions"/> 配置，
/// 避免在每个页面重复声明对话框尺寸、位置与关闭行为。
/// </summary>
public static class PlatformDialogs
{
    /// <summary>
    /// 右侧编辑抽屉：承载采集点、采集服务器、采集定义等表单的全屏高度面板。
    /// 编辑态禁止点击遮罩关闭，避免误触丢失未保存内容。
    /// </summary>
    public static DialogOptions EditorSheet { get; } = new()
    {
        Position = DialogPosition.CenterRight,
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        NoHeader = true,
        CloseButton = false,
        BackdropClick = false,
        CloseOnEscapeKey = true,
        CloseOnNavigation = true
    };

    /// <summary>
    /// 只读详情抽屉：展示采集节点配置详情等只读内容，允许点击遮罩关闭。
    /// </summary>
    public static DialogOptions DetailSheet { get; } = new()
    {
        Position = DialogPosition.CenterRight,
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        NoHeader = true,
        CloseButton = true,
        BackdropClick = true,
        CloseOnEscapeKey = true,
        CloseOnNavigation = true
    };

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

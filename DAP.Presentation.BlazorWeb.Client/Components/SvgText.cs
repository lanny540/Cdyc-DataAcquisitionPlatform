using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// SVG 文本元素。
///
/// **为什么需要这个组件**：Razor 把 <c>&lt;text&gt;</c> 当成「原样输出文本」的保留标签，
/// 一旦带属性就直接报 <c>RZ1023</c>，因此 SVG 的 <c>text</c> 只能从 RenderTree 里建。
/// 用法与普通标签一致，内容走 ChildContent。
/// </summary>
public sealed class SvgText : ComponentBase
{
    /// <summary>横坐标。</summary>
    [Parameter]
    public string? X { get; set; }

    /// <summary>纵坐标（文字基线）。</summary>
    [Parameter]
    public string? Y { get; set; }

    /// <summary>对齐方式：<c>start</c> / <c>middle</c> / <c>end</c>。</summary>
    [Parameter]
    public string? Anchor { get; set; }

    /// <summary>样式类。</summary>
    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>文本内容。</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");

        if (!string.IsNullOrEmpty(X)) builder.AddAttribute(1, "x", X);

        if (!string.IsNullOrEmpty(Y)) builder.AddAttribute(2, "y", Y);

        if (!string.IsNullOrEmpty(Anchor)) builder.AddAttribute(3, "text-anchor", Anchor);

        if (!string.IsNullOrEmpty(CssClass)) builder.AddAttribute(4, "class", CssClass);

        builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }
}
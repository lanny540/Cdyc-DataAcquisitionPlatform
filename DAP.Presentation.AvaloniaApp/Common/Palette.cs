using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DAP.Presentation.AvaloniaApp.Common;

/// <summary>
/// 色板取色入口。
///
/// 视图模型需要把「这一项指标用什么颜色」传给图表与卡片，而颜色只能来自
/// <c>DapPalette.axaml</c>。集中在这里按资源键取画刷，避免页面里出现十六进制，
/// 也避免同一套色被复制到多个视图模型。
/// </summary>
public static class Palette
{
    /// <summary>
    /// 按资源键取画刷。资源缺失时返回透明画刷（宁可看不见，也不要偷偷用别的颜色）。
    /// </summary>
    /// <param name="key">资源键，例如 <c>DapInfoBrush</c>。</param>
    /// <returns>画刷。</returns>
    public static IBrush Brush(string key)
    {
        if (Application.Current is { } app
            && app.TryFindResource(key, ThemeVariant.Light, out object? value)
            && value is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }
}

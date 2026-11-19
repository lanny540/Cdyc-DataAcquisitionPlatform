using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAP.Presentation.AvaloniaApp.Controls;

/// <summary>
/// 图表上的一个数据点：横轴标签与数值。
/// </summary>
/// <param name="Label">横轴标签，例如日期或班组名。</param>
/// <param name="Value">数值。</param>
public sealed record ChartSample(string Label, decimal Value);

/// <summary>
/// 桌面端自绘图表基类。
///
/// 不引入第三方图表库：项目只需要折线与柱状两种图，自绘可以完全复用
/// <c>DapPalette.axaml</c> 的色板键，也避免为图表库再引入一套颜色体系。
/// 颜色优先取控件自身的 <see cref="Accent"/> 与画笔属性，未设置时回落到资源键。
/// </summary>
public abstract class PlatformChartBase : Control
{
    /// <summary>
    /// 主色画笔。页面按语义绑定 <c>DapInfoBrush</c> / <c>DapSuccessBrush</c> 等。
    /// </summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<PlatformChartBase, IBrush?>(nameof(Accent));

    /// <summary>
    /// 坐标轴文字画刷，默认取 <c>DapTextSubtleBrush</c>。
    /// </summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<PlatformChartBase, IBrush?>(nameof(LabelBrush));

    /// <summary>
    /// 网格线画刷，默认取 <c>DapBorderBrush</c>。
    /// </summary>
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<PlatformChartBase, IBrush?>(nameof(GridBrush));

    /// <summary>
    /// 主色画笔。
    /// </summary>
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>
    /// 坐标轴文字画刷。
    /// </summary>
    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>
    /// 网格线画刷。
    /// </summary>
    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>
    /// 解析生效的主色：优先属性，其次资源，最后退回次级文字色。
    /// </summary>
    protected IBrush EffectiveAccent => Accent ?? Resolve("DapSecondaryBrush", Brushes.SlateGray);

    /// <summary>
    /// 解析生效的辅助文字色。
    /// </summary>
    protected IBrush EffectiveLabel => LabelBrush ?? Resolve("DapTextSubtleBrush", Brushes.Gray);

    /// <summary>
    /// 解析生效的网格色。
    /// </summary>
    protected IBrush EffectiveGrid => GridBrush ?? Resolve("DapBorderBrush", Brushes.LightGray);

    /// <summary>
    /// 按资源键取画刷，取不到时使用调用方给的兜底色（不会抛异常）。
    /// </summary>
    /// <param name="key">资源键。</param>
    /// <param name="fallback">兜底画刷。</param>
    /// <returns>画刷。</returns>
    protected IBrush Resolve(string key, IBrush fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : fallback;
    }

    /// <summary>
    /// 绘制一段文字。
    /// </summary>
    /// <param name="context">绘图上下文。</param>
    /// <param name="text">文本。</param>
    /// <param name="origin">左上角坐标。</param>
    /// <param name="size">字号。</param>
    /// <param name="brush">画刷。</param>
    /// <param name="weight">字重。</param>
    /// <returns>文本尺寸。</returns>
    protected static Size DrawText(
        DrawingContext context,
        string text,
        Point origin,
        double size,
        IBrush brush,
        FontWeight weight = FontWeight.Normal)
    {
        FormattedText formatted = new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight),
            size,
            brush);

        context.DrawText(formatted, origin);
        return new Size(formatted.Width, formatted.Height);
    }

    /// <summary>
    /// 把最大值抬到 1/2/5 乘 10 的整数次幂，避免纵轴出现 37.4 这种刻度。
    /// </summary>
    /// <param name="max">原始最大值。</param>
    /// <returns>合适的轴上限，最小为 1。</returns>
    protected static double NiceCeiling(double max)
    {
        if (max <= 0 || double.IsNaN(max) || double.IsInfinity(max)) return 1;

        double exponent = Math.Floor(Math.Log10(max));
        double magnitude = Math.Pow(10, exponent);
        double normalized = max / magnitude;

        int step = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return step * magnitude;
    }

    /// <summary>
    /// 数值文本：大数值用千分位，小数值保留两位。
    /// </summary>
    /// <param name="value">数值。</param>
    /// <returns>展示文本。</returns>
    protected static string FormatAxisValue(double value)
    {
        return Math.Abs(value) >= 1000
            ? value.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
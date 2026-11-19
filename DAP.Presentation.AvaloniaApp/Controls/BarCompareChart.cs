using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace DAP.Presentation.AvaloniaApp.Controls;

/// <summary>
/// 班组横向对比柱状图：一个指标一组柱，横轴为班组。
///
/// 与 Web 端的取舍一致：一条序列承载全部班组，而不是每个班组一条序列，
/// 否则所有柱子会叠在同一个位置。
/// </summary>
public sealed class BarCompareChart : PlatformChartBase
{
    /// <summary>
    /// 柱状数据。每项的 <see cref="ChartSample.Label"/> 是班组名。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<ChartSample>> ItemsProperty =
        AvaloniaProperty.Register<BarCompareChart, IReadOnlyList<ChartSample>>(
            nameof(Items),
            Array.Empty<ChartSample>());

    /// <summary>
    /// 纵轴单位，绘制在纵轴上方。
    /// </summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<BarCompareChart, string?>(nameof(Unit));

    static BarCompareChart()
    {
        AffectsRender<BarCompareChart>(ItemsProperty, UnitProperty, AccentProperty, LabelBrushProperty,
            GridBrushProperty);
    }

    /// <summary>
    /// 柱状数据。
    /// </summary>
    public IReadOnlyList<ChartSample> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// 纵轴单位。
    /// </summary>
    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);

        if (bounds.Width < 80 || bounds.Height < 60) return;

        const double leftAxis = 54;
        const double bottomAxis = 28;
        const double topPadding = 22;
        const double rightPadding = 14;

        Rect plot = new(
            leftAxis,
            topPadding,
            Math.Max(1, bounds.Width - leftAxis - rightPadding),
            Math.Max(1, bounds.Height - topPadding - bottomAxis));

        ChartSample[] items = Items?.ToArray() ?? [];

        if (items.Length == 0)
        {
            DrawText(context, "暂无对比数据", new Point(plot.X + 8, plot.Y + 8), 12, EffectiveLabel);
            return;
        }

        double max = NiceCeiling(items.Max(item => (double) item.Value));
        IBrush accent = EffectiveAccent;
        Pen gridPen = new(EffectiveGrid, 1) {DashStyle = new DashStyle([3, 4], 0)};
        Pen axisPen = new(EffectiveGrid, 1);

        const int ticks = 4;

        for (int index = 0; index <= ticks; index++)
        {
            double ratio = index / (double) ticks;
            double y = plot.Bottom - plot.Height * ratio;

            context.DrawLine(index == 0 ? axisPen : gridPen, new Point(plot.Left, y),
                new Point(plot.Right, y));

            string label = FormatAxisValue(max * ratio);
            Size size = Measure(label, 11);

            DrawText(context, label, new Point(plot.Left - size.Width - 8, y - size.Height / 2), 11,
                EffectiveLabel);
        }

        if (!string.IsNullOrWhiteSpace(Unit)) DrawText(context, Unit!, new Point(6, 0), 11, EffectiveLabel);

        double slot = plot.Width / items.Length;
        double barWidth = Math.Min(56, Math.Max(14, slot * 0.5));

        for (int index = 0; index < items.Length; index++)
        {
            double value = (double) items[index].Value;
            double height = Math.Max(value > 0 ? 2 : 0, plot.Height * (value / max));
            double centerX = plot.Left + slot * index + slot / 2;
            Rect bar = new(centerX - barWidth / 2, plot.Bottom - height, barWidth, height);

            context.DrawRectangle(accent, null, bar, 4, 4, default);

            string valueText = FormatAxisValue(value);
            Size valueSize = Measure(valueText, 11);

            DrawText(context, valueText,
                new Point(centerX - valueSize.Width / 2, bar.Y - valueSize.Height - 4), 11, EffectiveLabel,
                FontWeight.SemiBold);

            Size labelSize = Measure(items[index].Label, 11);
            double labelX = Math.Clamp(centerX - labelSize.Width / 2, plot.Left, plot.Right - labelSize.Width);

            DrawText(context, items[index].Label, new Point(labelX, plot.Bottom + 7), 11, EffectiveLabel);
        }
    }

    /// <summary>
    /// 量取文本尺寸，用于对齐刻度与标签。
    /// </summary>
    private static Size Measure(string text, double size)
    {
        FormattedText formatted = new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            size,
            Brushes.Black);

        return new Size(formatted.Width, formatted.Height);
    }
}
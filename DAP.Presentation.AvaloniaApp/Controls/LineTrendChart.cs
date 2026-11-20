using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace DAP.Presentation.AvaloniaApp.Controls;

/// <summary>
/// 趋势折线图：一条序列、面积填充、数据点标记与抽稀后的横轴标签。
///
/// 与 Web 端 MudChart 的取舍一致：标签数量限制在 6 个以内，否则密集的日期会挤成一条黑带。
/// </summary>
public sealed class LineTrendChart : PlatformChartBase
{
    /// <summary>
    /// 数据点集合。每次刷新都应整体替换集合实例，控件据此触发重绘。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<ChartSample>> PointsProperty =
        AvaloniaProperty.Register<LineTrendChart, IReadOnlyList<ChartSample>>(
            nameof(Points),
            Array.Empty<ChartSample>());

    /// <summary>
    /// 纵轴单位，绘制在纵轴上方。
    /// </summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<LineTrendChart, string?>(nameof(Unit));

    static LineTrendChart()
    {
        AffectsRender<LineTrendChart>(PointsProperty, UnitProperty, AccentProperty, LabelBrushProperty,
            GridBrushProperty);
    }

    /// <summary>
    /// 数据点集合。
    /// </summary>
    public IReadOnlyList<ChartSample> Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
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

        if (bounds.Width < 80 || bounds.Height < 60)
        {
            return;
        }

        const double leftAxis = 54;
        const double bottomAxis = 24;
        const double topPadding = 16;
        const double rightPadding = 14;

        Rect plot = new(
            leftAxis,
            topPadding,
            Math.Max(1, bounds.Width - leftAxis - rightPadding),
            Math.Max(1, bounds.Height - topPadding - bottomAxis));

        ChartSample[] points = Points?.ToArray() ?? [];

        if (points.Length == 0)
        {
            DrawText(context, "暂无趋势数据", new Point(plot.X + 8, plot.Y + 8), 12, EffectiveLabel);
            return;
        }

        double max = NiceCeiling(points.Max(point => (double)point.Value));
        IBrush accent = EffectiveAccent;
        Pen gridPen = new(EffectiveGrid, 1) { DashStyle = new DashStyle([3, 4], 0) };
        Pen axisPen = new(EffectiveGrid, 1);

        // 横向网格与纵轴刻度
        const int ticks = 4;

        for (int index = 0; index <= ticks; index++)
        {
            double ratio = index / (double)ticks;
            double y = plot.Bottom - (plot.Height * ratio);

            context.DrawLine(index == 0 ? axisPen : gridPen, new Point(plot.Left, y),
                new Point(plot.Right, y));

            string label = FormatAxisValue(max * ratio);
            Size size = Measure(label, 11);

            DrawText(context, label, new Point(plot.Left - size.Width - 8, y - (size.Height / 2)), 11,
                EffectiveLabel);
        }

        if (!string.IsNullOrWhiteSpace(Unit))
        {
            DrawText(context, Unit!, new Point(6, 0), 11, EffectiveLabel);
        }

        // 数据点坐标：单点居中，多点等距
        double step = points.Length > 1 ? plot.Width / (points.Length - 1) : 0;
        Point[] coordinates = points
            .Select((point, index) => new Point(
                points.Length == 1 ? plot.Center.X : plot.Left + (step * index),
                plot.Bottom - (plot.Height * ((double)point.Value / max))))
            .ToArray();

        // 面积填充
        StreamGeometry area = new();
        using (StreamGeometryContext geometry = area.Open())
        {
            geometry.BeginFigure(new Point(coordinates[0].X, plot.Bottom), true);
            foreach (Point coordinate in coordinates)
            {
                geometry.LineTo(coordinate);
            }

            geometry.LineTo(new Point(coordinates[^1].X, plot.Bottom));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(CreateAreaBrush(accent), null, area);

        // 折线
        StreamGeometry line = new();
        using (StreamGeometryContext geometry = line.Open())
        {
            geometry.BeginFigure(coordinates[0], false);
            foreach (Point coordinate in coordinates.Skip(1))
            {
                geometry.LineTo(coordinate);
            }

            geometry.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(accent, 2.4), line);

        // 数据点
        IBrush surface = Resolve("DapSurfaceBrush", Brushes.White);

        foreach (Point coordinate in coordinates)
        {
            context.DrawEllipse(surface, new Pen(accent, 2), coordinate, 3.2, 3.2);
        }

        // 横轴标签抽稀：最多 6 个
        int labelStep = Math.Max(1, (int)Math.Ceiling(points.Length / 6.0));

        for (int index = 0; index < points.Length; index += labelStep)
        {
            Size size = Measure(points[index].Label, 11);
            double x = Math.Clamp(coordinates[index].X - (size.Width / 2), plot.Left, plot.Right - size.Width);

            DrawText(context, points[index].Label, new Point(x, plot.Bottom + 6), 11, EffectiveLabel);
        }
    }

    /// <summary>
    /// 用主色生成 14% 透明度的面积填充，保持与折线同源取色。
    /// </summary>
    private static IBrush CreateAreaBrush(IBrush accent)
    {
        return accent is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color, 0.14)
            : Brushes.Transparent;
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

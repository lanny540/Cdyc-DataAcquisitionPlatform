using System.Globalization;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// 园区总负荷曲线。
///
/// **为什么自绘而不用 MudChart**：这里要同时画「实时负荷」与「申报需量」两条线，
/// 而 <c>app.css</c> 把 <c>path.mud-chart-line</c> 的描边统一覆盖成了卡片强调色，
/// 同一张图里的多条序列会变成同一个颜色，需量线就消失了。
/// 需量是园区能源唯一「可行动」的指标——超一次罚的是整月基本电费，
/// 它必须和负荷曲线画在一起、并且一眼可辨。
/// </summary>
public partial class EnergyLoadChart
{
    private const double CanvasWidth = 720;
    private const double CanvasHeight = 240;
    private const double Left = 58;
    private const double Right = 704;
    private const double Top = 18;
    private const double Bottom = 210;

    /// <summary>
    /// 获取或设置负荷数据点，按小时升序。
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<ParkEnergyLoadPointDto> Points { get; set; } = [];

    /// <summary>
    /// 获取或设置申报需量，单位与 <see cref="Unit"/> 一致。
    /// </summary>
    [Parameter]
    public decimal? DeclaredDemand { get; set; }

    /// <summary>
    /// 获取或设置纵轴单位。
    /// </summary>
    [Parameter]
    public string Unit { get; set; } = "kW";

    private double PlotWidth => Right - Left;

    private double PlotHeight => Bottom - Top;

    /// <summary>
    /// 纵轴上限。取「峰值与需量的较大者」再留 8% 余量，
    /// 否则需量线会贴在图表顶边上看不出还有多少余量。
    /// </summary>
    private double YMax
    {
        get
        {
            if (Points.Count == 0)
            {
                return 1000;
            }

            double peak = Points.Max(point => (double)point.LoadKw);
            double top = Math.Max(peak, (double)(DeclaredDemand ?? 0m));

            return Math.Ceiling(top * 1.08 / 500) * 500;
        }
    }

    /// <summary>
    /// 峰值点。
    /// </summary>
    private ParkEnergyLoadPointDto? Peak =>
        Points.Count == 0 ? null : Points.MaxBy(point => point.LoadKw);

    private double DemandUsage
    {
        get
        {
            ParkEnergyLoadPointDto? peak = Peak;

            if (peak is null || DeclaredDemand is not > 0)
            {
                return 0;
            }

            return (double)(peak.LoadKw / DeclaredDemand.Value);
        }
    }

    private IReadOnlyList<(double Value, double Y)> YTicks
    {
        get
        {
            var ticks = new List<(double, double)>(5);

            for (var index = 0; index <= 4; index++)
            {
                double value = YMax * index / 4;
                ticks.Add((value, YOf(value)));
            }

            return ticks;
        }
    }

    private IReadOnlyList<(string Label, double X)> XTicks { get; } =
    [
        ("00:00", Left),
        ("06:00", Left + ((Right - Left) * 6 / 24)),
        ("12:00", Left + ((Right - Left) * 12 / 24)),
        ("18:00", Left + ((Right - Left) * 18 / 24)),
        ("24:00", Right)
    ];

    private double XOf(int index)
    {
        // 按 24 小时等分，而不是按点数等分：最后一个点落在 23/24 处，
        // 右侧留出一小时的空白，X 轴标签才能与整点对齐。
        return Left + (PlotWidth * index / 24);
    }

    private double YOf(double value) => Bottom - (PlotHeight * value / YMax);

    private string LinePoints =>
        string.Join(
            ' ',
            Points.Select((point, index) =>
                $"{F(XOf(point.Hour))},{F(YOf((double)point.LoadKw))}"));

    private string AreaPoints
    {
        get
        {
            if (Points.Count == 0)
            {
                return string.Empty;
            }

            double firstX = XOf(Points[0].Hour);
            double lastX = XOf(Points[^1].Hour);

            return $"{F(firstX)},{F(Bottom)} {LinePoints} {F(lastX)},{F(Bottom)}";
        }
    }

    /// <summary>
    /// 格式化坐标值。必须固定用不变文化：中文区域虽然也是点号小数，
    /// 但一旦部署到小数点用逗号的区域，SVG 属性会被解析成多个坐标，图直接画歪。
    /// </summary>
    private static string F(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private double DemandY => YOf((double)(DeclaredDemand ?? 0m));

    private string FormatValue(double value) => value.ToString("N0");

    private string FormatUsage(double usage) => $"{usage * 100:0.#}%";
}

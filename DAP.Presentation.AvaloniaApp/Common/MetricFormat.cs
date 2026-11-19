using System.Globalization;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Controls;

namespace DAP.Presentation.AvaloniaApp.Common;

/// <summary>
/// 能耗页面的数值与口径文案格式化。
///
/// 与 Web 端保持一致：电力数值量级远高于原辅材料，超过一千时用千分位整数，
/// 否则保留两位小数，避免长串小数挤占卡片。
/// </summary>
public static class MetricFormat
{
    /// <summary>
    /// 指标数值。
    /// </summary>
    /// <param name="value">数值。</param>
    /// <returns>展示文本。</returns>
    public static string Value(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 综合能耗等汇总数值，保留一位小数。
    /// </summary>
    /// <param name="value">数值。</param>
    /// <returns>展示文本。</returns>
    public static string Total(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N1", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 可空数值，空值显示为 <c>--</c>。
    /// </summary>
    /// <param name="value">数值。</param>
    /// <returns>展示文本。</returns>
    public static string Value(decimal? value)
    {
        return value.HasValue ? Value(value.Value) : "--";
    }

    /// <summary>
    /// 日期区间。
    /// </summary>
    /// <param name="from">起始日。</param>
    /// <param name="to">结束日。</param>
    /// <returns>展示文本。</returns>
    public static string Range(DateOnly from, DateOnly to)
    {
        return $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}";
    }

    /// <summary>
    /// 统计周期：单日只显示日期，跨日区间显示起止。
    /// </summary>
    /// <param name="start">区间起点。</param>
    /// <param name="end">区间终点。</param>
    /// <returns>展示文本。</returns>
    public static string Bucket(DateOnly start, DateOnly end)
    {
        return start == end ? start.ToString("yyyy-MM-dd") : $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
    }

    /// <summary>
    /// 统计粒度名称。
    /// </summary>
    /// <param name="granularity">粒度，取值 day / week / month。</param>
    /// <returns>中文名称。</returns>
    public static string GranularityLabel(string granularity)
    {
        return granularity switch
        {
            "week" => "统计周",
            "month" => "统计月",
            _ => "自然日"
        };
    }

    /// <summary>
    /// 趋势方向文案。消耗上升是负面信号，下降是正面信号。
    /// </summary>
    /// <param name="direction">方向，取值 up / down / flat。</param>
    /// <returns>中文文案。</returns>
    public static string TrendLabel(string direction)
    {
        return direction switch
        {
            "up" => "后半程上升",
            "down" => "后半程下降",
            _ => "走势平稳"
        };
    }

    /// <summary>
    /// 把趋势点转换成图表数据。
    /// </summary>
    /// <param name="trend">趋势。</param>
    /// <returns>图表数据。</returns>
    public static IReadOnlyList<ChartSample> Samples(EnergyTrendDto trend)
    {
        return trend.Points
            .Select(point => new ChartSample(point.Label, point.Value))
            .ToArray();
    }
}
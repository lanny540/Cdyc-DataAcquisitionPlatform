using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 园区能源总览的示例数据。
///
/// **只在没有实测数据、且调用方显式要求时才返回。** 返回值的数据状态是
/// <see cref="ParkEnergyDataStatus.Sample"/>，页面据此加醒目标注。
///
/// 数字之间保持可验算：电 286.4 万 kWh × 0.1229 kgce/kWh ≈ 352 tce，
/// 三项一次能源合计等于 472 tce，部门占比之和为 100%，切到年度同样成立。
/// 一组自相矛盾的示例数会让人怀疑口径本身，而不是怀疑数据。
/// </summary>
public static class ParkEnergySampleProvider
{
    /// <summary>
    /// 构造一份示例快照。
    /// </summary>
    /// <param name="period">统计期间，取值为 month 或 year。</param>
    /// <param name="range">期间范围，用于填充起止日。</param>
    /// <param name="offset">园区时区偏移。</param>
    /// <returns>示例快照，数据状态为 sample。</returns>
    public static ParkEnergyOverviewDto Create(string period, ParkEnergyPeriod range, TimeSpan offset)
    {
        bool isYear = string.Equals(period, "year", StringComparison.Ordinal);

        IReadOnlyList<ParkEnergyKpiDto> kpis = isYear
            ?
            [
                new ParkEnergyKpiDto("totalTce", "综合能耗", "5,478", "tce", "↑ 1.6% 同比", false, "电、水、蒸汽折标煤合计，只汇一次能源"),
                new ParkEnergyKpiDto("elec", "总用电量", "3,322", "万 kWh", "↑ 2.9% 同比", false, "折标 4,082 tce，占 74.5%"),
                new ParkEnergyKpiDto("water", "用水量", "37.3", "万 t", "↓ 0.8% 同比", true, "折标 96 tce，占 1.8%"),
                new ParkEnergyKpiDto("steam", "蒸汽量", "13,000", "t", "↑ 1.1% 同比", false, "折标 1,300 tce，占 23.7%")
            ]
            :
            [
                new ParkEnergyKpiDto("totalTce", "综合能耗", "472", "tce", "↑ 2.1% 环比", false, "电、水、蒸汽折标煤合计，只汇一次能源"),
                new ParkEnergyKpiDto("elec", "总用电量", "286.4 万", "kWh", "↑ 3.4% 环比", false, "折标 352 tce，占 74.6%"),
                new ParkEnergyKpiDto("water", "用水量", "3.21", "万 t", "↓ 1.2% 环比", true, "折标 8.3 tce，占 1.8%"),
                new ParkEnergyKpiDto("steam", "蒸汽量", "1,120", "t", "↑ 0.6% 环比", false, "折标 112 tce，占 23.7%")
            ];

        IReadOnlyList<ParkEnergyShareDto> primary = isYear
            ?
            [
                new ParkEnergyShareDto("电", "4,082 tce", 0.745m, "74.5%"),
                new ParkEnergyShareDto("蒸汽", "1,300 tce", 0.237m, "23.7%"),
                new ParkEnergyShareDto("水", "96 tce", 0.018m, "1.8%")
            ]
            :
            [
                new ParkEnergyShareDto("电", "352 tce", 0.746m, "74.6%"),
                new ParkEnergyShareDto("蒸汽", "112 tce", 0.237m, "23.7%"),
                new ParkEnergyShareDto("水", "8.3 tce", 0.018m, "1.8%")
            ];

        IReadOnlyList<ParkEnergyShareDto> secondary = isYear
            ?
            [
                new ParkEnergyShareDto("空调制冷量", "176 万 kWh", null, "系数待配置"),
                new ParkEnergyShareDto("压缩空气", "104 万 kWh", null, "系数待配置")
            ]
            :
            [
                new ParkEnergyShareDto("空调制冷量", "14.9 万 kWh", null, "系数待配置"),
                new ParkEnergyShareDto("压缩空气", "9.0 万 kWh", null, "系数待配置")
            ];

        IReadOnlyList<ParkEnergyOrgShareDto> orgs = isYear
            ?
            [
                new ParkEnergyOrgShareDto("钞纸车间", "2,308 tce", 0.421m, 2.8m),
                new ParkEnergyOrgShareDto("印钞车间", "1,841 tce", 0.336m, 4.1m),
                new ParkEnergyOrgShareDto("动力车间", "723 tce", 0.132m, -1.2m),
                new ParkEnergyOrgShareDto("行政后勤", "351 tce", 0.064m, 0.9m),
                new ParkEnergyOrgShareDto("公共设施", "255 tce", 0.047m, -0.4m)
            ]
            :
            [
                new ParkEnergyOrgShareDto("钞纸车间", "199 tce", 0.421m, 2.8m),
                new ParkEnergyOrgShareDto("印钞车间", "159 tce", 0.336m, 4.1m),
                new ParkEnergyOrgShareDto("动力车间", "62 tce", 0.132m, -1.2m),
                new ParkEnergyOrgShareDto("行政后勤", "30 tce", 0.064m, 0.9m),
                new ParkEnergyOrgShareDto("公共设施", "22 tce", 0.047m, -0.4m)
            ];

        return new ParkEnergyOverviewDto(
            period,
            isYear ? $"{range.Start:yyyy} 年" : $"{range.Start:yyyy 年 M 月}",
            range.Start,
            range.EndInclusive,
            DateTimeOffset.Now.ToOffset(offset),
            // 示例数据不假装自己是刚采到的：给一个固定时刻，避免「数据截至」随时间跳动。
            new DateTimeOffset(2026, 11, 14, 18, 0, 0, offset),
            ParkEnergyDataStatus.Sample,
            ["当前为示例数据，用于版式预览，不代表任何真实计量值。"],
            kpis,
            [
                new ParkEnergyLoadPointDto(0, 2150m),
                new ParkEnergyLoadPointDto(1, 2020m),
                new ParkEnergyLoadPointDto(2, 1960m),
                new ParkEnergyLoadPointDto(3, 1930m),
                new ParkEnergyLoadPointDto(4, 2010m),
                new ParkEnergyLoadPointDto(5, 2280m),
                new ParkEnergyLoadPointDto(6, 2650m),
                new ParkEnergyLoadPointDto(7, 3120m),
                new ParkEnergyLoadPointDto(8, 3860m),
                new ParkEnergyLoadPointDto(9, 4120m),
                new ParkEnergyLoadPointDto(10, 4050m),
                new ParkEnergyLoadPointDto(11, 3980m),
                new ParkEnergyLoadPointDto(12, 3720m),
                new ParkEnergyLoadPointDto(13, 3950m),
                new ParkEnergyLoadPointDto(14, 4280m),
                new ParkEnergyLoadPointDto(15, 4180m),
                new ParkEnergyLoadPointDto(16, 3960m),
                new ParkEnergyLoadPointDto(17, 3420m),
                new ParkEnergyLoadPointDto(18, 3050m),
                new ParkEnergyLoadPointDto(19, 2880m),
                new ParkEnergyLoadPointDto(20, 2640m),
                new ParkEnergyLoadPointDto(21, 2420m),
                new ParkEnergyLoadPointDto(22, 2280m),
                new ParkEnergyLoadPointDto(23, 2200m)
            ],
            4500m,
            primary,
            secondary,
            orgs);
    }
}

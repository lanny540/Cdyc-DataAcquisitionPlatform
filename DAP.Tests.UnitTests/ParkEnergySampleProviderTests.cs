using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 园区能源示例数据的契约测试。
///
/// 示例数据是纯静态快照，这里钉住的是它的「自洽性」：
/// dataStatus 必须是 sample（页面据此加醒目标注）、期间边界必须原样回填、
/// 月 / 年两套数字不能串（环比 / 同比的口径文案不能对调），
/// 以及占比之和约等于 100%——一组自相矛盾的示例数会让人怀疑口径本身。
/// </summary>
public sealed class ParkEnergySampleProviderTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    private static ParkEnergyPeriod MonthRange() => new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 19));

    private static ParkEnergyPeriod YearRange() => new(new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 19));

    [Fact]
    public void Create_Month_Should_Mark_Sample_Status_And_Echo_Period()
    {
        ParkEnergyOverviewDto sample = ParkEnergySampleProvider.Create("month", MonthRange(), Offset);

        Assert.Equal(ParkEnergyDataStatus.Sample, sample.DataStatus);
        Assert.Equal("month", sample.Period);
        Assert.Equal(new DateOnly(2026, 11, 1), sample.PeriodStart);
        Assert.Equal(new DateOnly(2026, 11, 19), sample.PeriodEnd);
        Assert.Equal("2026 年 11 月", sample.PeriodLabel);

        // 示例数据必须自带提示，页面据此与实测数据区分样式。
        Assert.Contains(sample.Warnings, warning => warning.Contains("示例数据"));
    }

    [Fact]
    public void Create_Month_Should_Use_Monthly_Figures_With_Ring_Delta()
    {
        ParkEnergyOverviewDto sample = ParkEnergySampleProvider.Create("month", MonthRange(), Offset);

        Assert.Equal(4, sample.Kpis.Count);
        Assert.All(sample.Kpis, kpi => Assert.Contains("环比", kpi.Delta));
        Assert.Contains(sample.Kpis, kpi => kpi.Key == "totalTce" && kpi.Value == "472");
        Assert.Contains(sample.Kpis, kpi => kpi.Key == "elec" && kpi.Label == "总用电量");

        // 数据截至是固定时刻：不能随生成时间跳动，否则像刚采到的。
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 18, 0, 0, Offset), sample.DataThrough);
    }

    [Fact]
    public void Create_Year_Should_Use_Yearly_Figures_With_Year_Delta()
    {
        ParkEnergyOverviewDto sample = ParkEnergySampleProvider.Create("year", YearRange(), Offset);

        Assert.Equal("year", sample.Period);
        Assert.Equal("2026 年", sample.PeriodLabel);
        Assert.Equal(4, sample.Kpis.Count);
        Assert.All(sample.Kpis, kpi => Assert.Contains("同比", kpi.Delta));
        Assert.Contains(sample.Kpis, kpi => kpi.Key == "totalTce" && kpi.Value == "5,478");
        Assert.Contains(sample.PrimaryShares, share => share.DisplayValue == "4,082 tce");
    }

    [Fact]
    public void Create_Should_Always_Provide_24_Load_Points_And_Demand()
    {
        foreach (ParkEnergyOverviewDto sample in new[]
                 {
                     ParkEnergySampleProvider.Create("month", MonthRange(), Offset),
                     ParkEnergySampleProvider.Create("year", YearRange(), Offset),
                 })
        {
            Assert.Equal(24, sample.LoadPoints.Count);
            Assert.Equal(Enumerable.Range(0, 24), sample.LoadPoints.Select(point => point.Hour));
            Assert.All(sample.LoadPoints, point => Assert.True(point.LoadKw > 0));
            Assert.Equal(4500m, sample.DeclaredDemandKw);
        }
    }

    [Fact]
    public void Create_Should_Keep_Share_Sums_Close_To_One()
    {
        ParkEnergyOverviewDto month = ParkEnergySampleProvider.Create("month", MonthRange(), Offset);
        ParkEnergyOverviewDto year = ParkEnergySampleProvider.Create("year", YearRange(), Offset);

        foreach (ParkEnergyOverviewDto sample in new[] {month, year})
        {
            decimal primarySum = sample.PrimaryShares.Sum(share => share.Share ?? 0m);
            Assert.InRange(primarySum, 0.99m, 1.01m);

            decimal orgSum = sample.OrgRows.Sum(row => row.Share);
            Assert.InRange(orgSum, 0.99m, 1.01m);
        }
    }

    [Fact]
    public void Create_SecondaryShares_Should_Have_No_Percentage()
    {
        ParkEnergyOverviewDto sample = ParkEnergySampleProvider.Create("month", MonthRange(), Offset);

        // 折算电耗系数未配置：不给占比，否则读者以为这类能源不耗电。
        Assert.Equal(2, sample.SecondaryShares.Count);
        Assert.All(sample.SecondaryShares, share => Assert.Null(share.Share));
        Assert.All(sample.SecondaryShares, share => Assert.Contains("系数", share.ShareText));
    }

    [Fact]
    public void Create_GeneratedAt_Should_Carry_Park_Offset()
    {
        TimeSpan exoticOffset = TimeSpan.FromHours(9);

        ParkEnergyOverviewDto sample = ParkEnergySampleProvider.Create("month", MonthRange(), exoticOffset);

        Assert.Equal(exoticOffset, sample.GeneratedAt.Offset);
        Assert.Equal(exoticOffset, sample.DataThrough!.Value.Offset);
    }
}

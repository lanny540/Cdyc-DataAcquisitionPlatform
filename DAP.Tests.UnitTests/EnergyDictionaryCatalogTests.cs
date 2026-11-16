using DAP.Core.Shared.Catalog;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 能源字典的契约测试。
///
/// 这些断言守的是两条容易在后续维护中被悄悄改坏的决策：
/// 一是「哪些介质参与综合能耗求和」，二是「用能用途分成几类」。
/// 前者改错会让总量虚高但页面看起来完全正常，后者改错会让某个责任主体没有账可查。
/// </summary>
public class EnergyDictionaryCatalogTests
{
    /// <summary>
    /// 介质应为五种，且编码不重复。
    /// </summary>
    [Fact]
    public void Media_Should_Contain_Five_Unique_Codes()
    {
        Assert.Equal(5, EnergyDictionaryCatalog.Media.Count);
        Assert.Equal(5, EnergyDictionaryCatalog.Media.Select(item => item.Code).Distinct().Count());
    }

    /// <summary>
    /// 只有电、水、蒸汽是一次能源。
    ///
    /// 压缩空气与空调制冷量的能量本来就来自电或蒸汽，若一并折标求和，
    /// 这部分会被计算两次——总量虚高，而报表上完全看不出异常。
    /// </summary>
    [Fact]
    public void Only_Primary_Energy_Should_Join_Total_Consumption()
    {
        string[] primary = EnergyDictionaryCatalog.Media
            .Where(item => item.IsPrimaryEnergy)
            .Select(item => item.Code)
            .ToArray();

        Assert.Equal(["ELEC", "WATER", "STEAM"], primary);
    }

    /// <summary>
    /// 二次能源不应带折标煤系数：带了就会被误加进综合能耗。
    /// </summary>
    [Fact]
    public void Secondary_Energy_Should_Not_Have_Coal_Factor()
    {
        Assert.All(
            EnergyDictionaryCatalog.Media.Where(item => !item.IsPrimaryEnergy),
            item => Assert.Null(item.CoalFactor));
    }

    /// <summary>
    /// 蒸汽的折标系数取决于焓值，必须留空由现场配置。
    /// 给一个默认数会让报表显得精确，但那是错的。
    /// </summary>
    [Fact]
    public void Steam_Should_Not_Carry_Default_Coal_Factor()
    {
        EnergyMediumSeed steam = EnergyDictionaryCatalog.Media.Single(item => item.Code == "STEAM");

        Assert.Null(steam.CoalFactor);
    }

    /// <summary>
    /// 用能用途应为五类，覆盖五个责任主体。
    /// </summary>
    [Fact]
    public void Usage_Types_Should_Cover_Five_Responsible_Parties()
    {
        string[] codes = EnergyDictionaryCatalog.UsageTypes.Select(item => item.Code).ToArray();

        Assert.Equal(["PROD", "AUX", "OFFICE", "SECURITY", "PUBLIC"], codes);
    }

    /// <summary>
    /// 照明属于设备系统，不属于用能用途。
    /// 若哪天有人把 LIGHTING 加进用途，这条会拦住。
    /// </summary>
    [Fact]
    public void Lighting_Should_Be_A_System_Not_A_Usage()
    {
        Assert.Contains(EnergyDictionaryCatalog.Systems, item => item.Code == "LIGHTING");
        Assert.DoesNotContain(EnergyDictionaryCatalog.UsageTypes, item => item.Code == "LIGHTING");
    }

    /// <summary>
    /// 同一组字典内的排序值不得重复，否则下拉框顺序会随数据库返回顺序漂移。
    /// </summary>
    [Fact]
    public void Sort_Orders_Should_Be_Unique_Within_Each_Catalog()
    {
        Assert.Equal(
            EnergyDictionaryCatalog.Media.Count,
            EnergyDictionaryCatalog.Media.Select(item => item.SortOrder).Distinct().Count());

        Assert.Equal(
            EnergyDictionaryCatalog.UsageTypes.Count,
            EnergyDictionaryCatalog.UsageTypes.Select(item => item.SortOrder).Distinct().Count());

        Assert.Equal(
            EnergyDictionaryCatalog.Systems.Count,
            EnergyDictionaryCatalog.Systems.Select(item => item.SortOrder).Distinct().Count());
    }
}

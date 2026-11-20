using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 园区能源契约的边界测试。
///
/// 契约文件本身主要是数据结构声明（校验逻辑在服务层，见
/// <c>ParkEnergyQueryServiceTests</c>），这里钉住的是页面与接口共同依赖的
/// 语义约定：数据状态三态常量的取值（前端按字符串分流渲染）、
/// 请求 / 响应记录的值语义（缓存与比对依赖它）、以及批量写入结果的计数语义。
/// </summary>
public sealed class ParkEnergyContractsTests
{
    [Fact]
    public void DataStatus_Constants_Should_Match_Documented_Values()
    {
        // 页面按这三个字符串切换渲染分支：改动任何一个都会让旧页面显示错乱。
        Assert.Equal("live", ParkEnergyDataStatus.Live);
        Assert.Equal("empty", ParkEnergyDataStatus.Empty);
        Assert.Equal("sample", ParkEnergyDataStatus.Sample);
    }

    [Fact]
    public void MeterUpsertRequest_Should_Be_Value_Equivalent()
    {
        Guid mediumId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();
        Guid usageId = Guid.NewGuid();
        Guid systemId = Guid.NewGuid();

        EnergyMeterUpsertRequest first = new(
            null, "EM-01", "电表", mediumId, orgId, usageId, systemId, null, true, 1m, true, null);
        EnergyMeterUpsertRequest second = new(
            null, "EM-01", "电表", mediumId, orgId, usageId, systemId, null, true, 1m, true, null);

        // 记录的值语义：审计、缓存、对比都依赖它而不是引用相等。
        Assert.Equal(first, second);
        Assert.Equal(first, second with { });
        Assert.NotEqual(first, second with { Code = "EM-02" });

        // 可空标识链：Id 为空表示新建，ParentMeterId 为空表示无上级。
        Assert.Null(first.Id);
        Assert.Null(first.ParentMeterId);
    }

    [Fact]
    public void OrgUnitUpsertRequest_Should_Default_To_Create_When_Id_Null()
    {
        EnergyOrgUnitUpsertRequest request = new(null, "DEPT-01", "车间", "department", null);

        Assert.Null(request.Id);
        // 层级是字符串常量而不是枚举：页面传错值时由服务层翻译成中文报错。
        Assert.Equal("department", request.Level);
    }

    [Fact]
    public void IngestResult_Should_Carry_Counts_And_Messages()
    {
        IngestEnergyReadingsResultDto result = new(3, 2, 1, new[] { "计量点编码 X 不存在，已跳过。" });

        Assert.Equal(3, result.Accepted);
        Assert.Equal(2, result.Updated);
        Assert.Equal(1, result.Rejected);
        Assert.Single(result.Messages);

        // 空请求的约定：不报错，返回 0 条。
        IngestEnergyReadingsResultDto empty = new(0, 0, 0, Array.Empty<string>());
        Assert.Empty(empty.Messages);
    }

    [Fact]
    public void IngestReadingItem_Should_Allow_Null_Source()
    {
        IngestEnergyReadingItem item = new("EM-01", new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.FromHours(8)),
            100m, null);

        // Source 为空时由服务端默认为 collection：外部推送方不需要每次都带。
        Assert.Null(item.Source);
        Assert.Equal(100m, item.Value);
    }

    [Fact]
    public void MediumFactorRequest_Should_Allow_Clearing_With_Null()
    {
        // 传空表示清除系数：蒸汽按焓值计价时不同批次热值不同，固定值反而是错的。
        EnergyMediumFactorRequest clearing = new(null);
        EnergyMediumFactorRequest setting = new(92m);

        Assert.Null(clearing.CoalFactor);
        Assert.Equal(92m, setting.CoalFactor);
        Assert.NotEqual(clearing, setting);
    }

    [Fact]
    public void OverviewDtos_Should_Expose_Shapes_Pages_Consume()
    {
        ParkEnergyLoadPointDto point = new(8, 2150m);
        Assert.Equal(8, point.Hour);
        Assert.Equal(2150m, point.LoadKw);

        ParkEnergyShareDto share = new("电", "352 tce", 0.746m, "74.6%");
        Assert.Equal("电", share.Name);
        Assert.Equal(0.746m, share.Share);

        ParkEnergyOrgShareDto orgRow = new("钞纸车间", "199 tce", 0.421m, 2.8m);
        Assert.Equal(2.8m, orgRow.DeltaPercent);
    }
}

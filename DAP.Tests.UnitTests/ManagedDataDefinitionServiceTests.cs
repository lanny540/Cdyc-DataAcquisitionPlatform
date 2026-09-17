using DAP.Core.Shared.Contracts;
using DAP.Tests.UnitTests.TestDoubles;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证后台数据定义相关核心行为。
/// </summary>
public class ManagedDataDefinitionServiceTests
{
    [Fact]
    public async Task UpsertManagedDataDefinitionAsync_ShouldCreateNewDefinition()
    {
        var service = new InMemoryDataAcquisitionPlatformService();

        ManagedDataDefinitionDto result = await service.UpsertManagedDataDefinitionAsync(
            new ManagedDataDefinitionUpsertRequest(
                null,
                "HIS-001",
                "蒸汽流量标签",
                "Historian API",
                "http://10.9.3.54:8080/historian-rest-api",
                "tag001",
                "BBB 部门",
                "BBB223",
                "蒸汽数据",
                "t/h",
                "用于报表统计。",
                """{"tagName":"tag001"}""",
                """{"能源介质":"蒸汽"}""",
                60,
                true));

        IReadOnlyCollection<ManagedDataDefinitionDto> definitions = await service.GetManagedDataDefinitionsAsync();

        Assert.Contains(definitions, item => item.Id == result.Id && item.Identifier == "tag001");
    }

    [Fact]
    public async Task UpsertManagedDataDefinitionAsync_ShouldRenameExistingCollectionPoint()
    {
        var service = new InMemoryDataAcquisitionPlatformService();

        ManagedDataDefinitionDto savedDefinition = await service.UpsertManagedDataDefinitionAsync(
            new ManagedDataDefinitionUpsertRequest(
                null,
                "HIS-OLD",
                "历史标签旧编码",
                "Historian API",
                "http://10.9.3.54:8080/historian-rest-api",
                "tag900",
                "AAA 部门",
                "AAA100",
                "电力数据",
                "kWh",
                string.Empty,
                """{"tagName":"tag900"}""",
                "{}",
                60,
                true));

        await service.UpsertManagedDataDefinitionAsync(
            new ManagedDataDefinitionUpsertRequest(
                savedDefinition.Id,
                "HIS-NEW",
                "历史标签新编码",
                "Historian API",
                "http://10.9.3.54:8080/historian-rest-api",
                "tag900",
                "AAA 部门",
                "AAA100",
                "电力数据",
                "kWh",
                string.Empty,
                """{"tagName":"tag900"}""",
                "{}",
                60,
                true));

        IReadOnlyCollection<CollectionPointDto> points = await service.GetCollectionPointsAsync();

        Assert.DoesNotContain(points, item => item.Code == "HIS-OLD");
        Assert.Contains(points, item => item.Code == "HIS-NEW" && item.Source == "Server");
    }

    [Fact]
    public async Task DeleteManagedDataDefinitionAsync_ShouldRemoveSyncedCollectionPoint()
    {
        var service = new InMemoryDataAcquisitionPlatformService();

        ManagedDataDefinitionDto savedDefinition = await service.UpsertManagedDataDefinitionAsync(
            new ManagedDataDefinitionUpsertRequest(
                null,
                "HIS-DEL",
                "待删除历史标签",
                "Historian API",
                "http://10.9.3.54:8080/historian-rest-api",
                "tag910",
                "BBB 部门",
                "BBB910",
                "蒸汽数据",
                "t/h",
                string.Empty,
                """{"tagName":"tag910"}""",
                "{}",
                60,
                true));

        var deleted = await service.DeleteManagedDataDefinitionAsync(savedDefinition.Id);
        IReadOnlyCollection<CollectionPointDto> points = await service.GetCollectionPointsAsync();

        Assert.True(deleted);
        Assert.DoesNotContain(points, item => item.Code == "HIS-DEL");
    }
}

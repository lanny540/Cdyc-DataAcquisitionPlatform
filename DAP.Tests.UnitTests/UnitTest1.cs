using DAP.Core.Shared.Contracts;
using DAP.Tests.UnitTests.TestDoubles;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证数据采集平台核心业务骨架的关键行为。
/// </summary>
public class UnitTest1
{
    [Fact]
    public async Task UpsertCollectionPointAsync_ShouldCreateNewPoint()
    {
        var service = new InMemoryDataAcquisitionPlatformService();

        var result = await service.UpsertCollectionPointAsync(new CollectionPointUpsertRequest(
            null,
            "MB-99",
            "新增测试点",
            "Modbus TCP",
            "192.168.1.99:502",
            true,
            "Server"));

        var points = await service.GetCollectionPointsAsync();

        Assert.Contains(points, item => item.Code == result.Code && item.Name == "新增测试点");
    }

    [Fact]
    public async Task SyncLocalCollectionPointsAsync_ShouldMarkPointsAsLocalSource()
    {
        var service = new InMemoryDataAcquisitionPlatformService();
        var localPoint = new LocalCollectionPointDto(
            Guid.NewGuid(),
            "SYNC-01",
            "客户端同步点",
            "MQTT",
            "mqtt://broker.local/sync/01",
            true,
            "待同步",
            DateTimeOffset.UtcNow);

        var response = await service.SyncLocalCollectionPointsAsync(new SyncCollectionPointsRequest([localPoint]));
        var points = await service.GetCollectionPointsAsync();

        Assert.Equal(1, response.CreatedCount);
        Assert.Contains(points, item => item.Code == "SYNC-01" && item.Source == "Local");
    }

    [Fact]
    public async Task DeleteCollectionPointAsync_ShouldRemoveExistingPoint()
    {
        var service = new InMemoryDataAcquisitionPlatformService();
        var savedPoint = await service.UpsertCollectionPointAsync(new CollectionPointUpsertRequest(
            null,
            "DELETE-01",
            "待删除点位",
            "Modbus TCP",
            "192.168.1.201:502",
            true,
            "Server"));

        var deleted = await service.DeleteCollectionPointAsync(savedPoint.Id);
        var points = await service.GetCollectionPointsAsync();

        Assert.True(deleted);
        Assert.DoesNotContain(points, item => item.Id == savedPoint.Id);
    }
}

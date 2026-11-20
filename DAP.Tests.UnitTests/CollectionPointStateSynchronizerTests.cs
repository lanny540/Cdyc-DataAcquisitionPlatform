using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="CollectionPointStateSynchronizer"/> 的采集点状态同步测试。
///
/// 这是采集执行的统一写入口：把定义上的名称/协议/地址/启停同步到对应采集点，
/// 不涉及授权判定。关键规则：停用的点位状态固定为「停用」——写「离线」会让
/// 仪表板把「人工关闭」算成「故障」。
/// </summary>
public class CollectionPointStateSynchronizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>用真实 EF 仓储 + SQLite 内存库构造被测服务，贴近生产路径。</summary>
    private static CollectionPointStateSynchronizer CreateService(SqliteTestDatabase database)
    {
        DataAcquisitionPlatformDbContext db = database.CreateContext();
        return new CollectionPointStateSynchronizer(new PostgreSqlCollectionPointRepository(db), db);
    }

    private static CollectionPoint? FindPoint(SqliteTestDatabase database, string code)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        return db.CollectionPoints.AsNoTracking().FirstOrDefault(item => item.Code == code);
    }

    [Fact]
    public async Task 同步不存在的编码_创建采集点并落库()
    {
        using SqliteTestDatabase database = new();

        CollectionPoint point = await CreateService(database).SyncAsync(
            "CP-99", "新点位", "Modbus TCP", "192.168.10.99:502",
            true, "在线", null, Now);

        Assert.NotEqual(Guid.Empty, point.Id);
        Assert.Equal("CP-99", point.Code);
        Assert.Equal("新点位", point.Name);
        Assert.Equal("Modbus TCP", point.Protocol);
        Assert.Equal("192.168.10.99:502", point.Endpoint);
        Assert.True(point.IsEnabled);
        Assert.Equal("在线", point.CommunicationStatus);
        Assert.Equal("Server", point.Source);
        Assert.Equal(Now, point.UpdatedAt);

        // 另开上下文验证真实落库。
        CollectionPoint? persisted = FindPoint(database, "CP-99");
        Assert.NotNull(persisted);
        Assert.Equal("新点位", persisted.Name);
    }

    [Fact]
    public async Task 同步已存在的编码_更新展示字段与状态()
    {
        using SqliteTestDatabase database = new();
        Guid pointId = Guid.NewGuid();

        using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            seed.CollectionPoints.Add(new CollectionPoint
            {
                Id = pointId,
                Code = "CP-01",
                Name = "旧名称",
                Protocol = "OPC UA",
                Endpoint = "opc.tcp://old",
                IsEnabled = true,
                CommunicationStatus = "离线",
                Source = "Local",
                UpdatedAt = Now.AddHours(-1)
            });
            seed.SaveChanges();
        }

        await CreateService(database).SyncAsync(
            "CP-01", "新名称", "Modbus TCP", "192.168.10.21:502",
            true, "在线", null, Now);

        CollectionPoint? persisted = FindPoint(database, "CP-01");
        Assert.NotNull(persisted);
        Assert.Equal(pointId, persisted.Id);
        Assert.Equal("新名称", persisted.Name);
        Assert.Equal("Modbus TCP", persisted.Protocol);
        Assert.Equal("192.168.10.21:502", persisted.Endpoint);
        Assert.Equal("在线", persisted.CommunicationStatus);
        // 配置来源被归一为 Server。
        Assert.Equal("Server", persisted.Source);
        Assert.Equal(Now, persisted.UpdatedAt);
    }

    [Fact]
    public async Task 停用的点位状态固定为停用_错误信息被清空()
    {
        using SqliteTestDatabase database = new();
        Guid pointId = Guid.NewGuid();

        using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            seed.CollectionPoints.Add(new CollectionPoint
            {
                Id = pointId,
                Code = "CP-02",
                Name = "二号点",
                Protocol = "Modbus TCP",
                Endpoint = "192.168.10.22:502",
                IsEnabled = true,
                CommunicationStatus = "离线",
                LastError = "连接超时",
                Source = "Server",
                UpdatedAt = Now.AddHours(-1)
            });
            seed.SaveChanges();
        }

        // 即使调用方传入「离线」与错误信息：人工停用不能被仪表板算成故障。
        CollectionPoint point = await CreateService(database).SyncAsync(
            "CP-02", "二号点", "Modbus TCP", "192.168.10.22:502",
            false, "离线", "连接超时", Now);

        Assert.False(point.IsEnabled);
        Assert.Equal("停用", point.CommunicationStatus);
        Assert.Null(point.LastError);
    }

    [Fact]
    public async Task 启用的点位保留通信状态与错误信息()
    {
        using SqliteTestDatabase database = new();

        CollectionPoint point = await CreateService(database).SyncAsync(
            "CP-03", "三号点", "OPC UA", "opc.tcp://server",
            true, "离线", "读取失败", Now);

        Assert.True(point.IsEnabled);
        Assert.Equal("离线", point.CommunicationStatus);
        Assert.Equal("读取失败", point.LastError);
    }
}

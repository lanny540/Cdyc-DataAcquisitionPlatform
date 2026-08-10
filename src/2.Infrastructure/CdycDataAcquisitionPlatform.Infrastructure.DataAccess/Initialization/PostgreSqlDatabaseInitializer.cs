using CdycDataAcquisitionPlatform.Core.Domain.Entities;
using CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Initialization;

/// <summary>
/// 提供 PostgreSQL 数据库初始化能力。
/// </summary>
public sealed class PostgreSqlDatabaseInitializer
{
    private readonly string _connectionString;
    private readonly DataAcquisitionPlatformDbContext _dbContext;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlDatabaseInitializer"/> 实例。
    /// </summary>
    public PostgreSqlDatabaseInitializer(
        string connectionString,
        DataAcquisitionPlatformDbContext dbContext)
    {
        _connectionString = connectionString;
        _dbContext = dbContext;
    }

    /// <summary>
    /// 确保数据库、表结构和初始化数据已就绪。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await SeedAsync(cancellationToken);
    }

    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(_connectionString);
        var targetDatabase = connectionBuilder.Database;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new InvalidOperationException("PostgreSQL 连接字符串未指定数据库名称。");
        }

        var adminConnectionBuilder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres"
        };

        await using var adminConnection = new NpgsqlConnection(adminConnectionBuilder.ConnectionString);
        await adminConnection.OpenAsync(cancellationToken);

        const string existsSql =
            """
            SELECT 1
            FROM pg_database
            WHERE datname = @databaseName;
            """;

        await using var existsCommand = new NpgsqlCommand(existsSql, adminConnection);
        existsCommand.Parameters.AddWithValue("databaseName", targetDatabase);
        var exists = await existsCommand.ExecuteScalarAsync(cancellationToken) is not null;
        if (exists)
        {
            return;
        }

        var quotedDatabaseName = "\"" + targetDatabase.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await using var createDatabaseCommand = new NpgsqlCommand($"CREATE DATABASE {quotedDatabaseName};", adminConnection);
        await createDatabaseCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seedPoints = CreateSeedPoints();

        foreach (var seedPoint in seedPoints)
        {
            var existingPoint = await _dbContext.CollectionPoints
                .FirstOrDefaultAsync(item => item.Code == seedPoint.Code, cancellationToken);

            if (existingPoint is null)
            {
                await _dbContext.CollectionPoints.AddAsync(seedPoint, cancellationToken);
            }
            else
            {
                existingPoint.Name = seedPoint.Name;
                existingPoint.Protocol = seedPoint.Protocol;
                existingPoint.Endpoint = seedPoint.Endpoint;
                existingPoint.IsEnabled = seedPoint.IsEnabled;
                existingPoint.CommunicationStatus = seedPoint.CommunicationStatus;
                existingPoint.Source = seedPoint.Source;
                existingPoint.LastError = seedPoint.LastError;
                existingPoint.UpdatedAt = seedPoint.UpdatedAt;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (await _dbContext.CollectionDataRecords.AnyAsync(cancellationToken))
        {
            return;
        }

        var pointsByCode = await _dbContext.CollectionPoints
            .ToDictionaryAsync(item => item.Code, cancellationToken);

        await _dbContext.CollectionDataRecords.AddRangeAsync(
        [
            CreateSeedRecord(Guid.Parse("67da3e77-d8b7-4427-b6fd-dcb0184db001"), pointsByCode["MB-01"], "温度", 83.6000m, "°C", 3),
            CreateSeedRecord(Guid.Parse("67da3e77-d8b7-4427-b6fd-dcb0184db002"), pointsByCode["MB-01"], "温度", 82.9000m, "°C", 8),
            CreateSeedRecord(Guid.Parse("67da3e77-d8b7-4427-b6fd-dcb0184db003"), pointsByCode["MQ-02"], "湿度", 46.2000m, "%", 4),
            CreateSeedRecord(Guid.Parse("67da3e77-d8b7-4427-b6fd-dcb0184db004"), pointsByCode["MQ-02"], "湿度", 45.6000m, "%", 11),
            CreateSeedRecord(Guid.Parse("67da3e77-d8b7-4427-b6fd-dcb0184db005"), pointsByCode["OPC-03"], "压力", 0.7100m, "MPa", 37)
        ], cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CollectionPoint[] CreateSeedPoints()
    {
        return
        [
            new CollectionPoint
            {
                Id = Guid.Parse("2e1d6be0-191d-439f-a72e-ef4b2ff4d201"),
                Code = "MB-01",
                Name = "锅炉一号温度点",
                Protocol = "Modbus TCP",
                Endpoint = "192.168.10.21:502",
                IsEnabled = true,
                CommunicationStatus = "在线",
                Source = "Server",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-12)
            },
            new CollectionPoint
            {
                Id = Guid.Parse("65f6b7a0-08be-4ddc-97d2-b048e9984114"),
                Code = "MQ-02",
                Name = "车间湿度传感器",
                Protocol = "MQTT",
                Endpoint = "mqtt://broker.local/factory/humidity",
                IsEnabled = true,
                CommunicationStatus = "在线",
                Source = "Server",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-8)
            },
            new CollectionPoint
            {
                Id = Guid.Parse("0ef83938-d4ce-4f21-8c66-59e7ee77ef8a"),
                Code = "OPC-03",
                Name = "空压站压力点",
                Protocol = "OPC DA",
                Endpoint = "opcda://compressor/station-03",
                IsEnabled = true,
                CommunicationStatus = "离线",
                Source = "Server",
                LastError = "最近一次轮询超时。",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-34)
            }
        ];
    }

    private static CollectionDataRecord CreateSeedRecord(
        Guid id,
        CollectionPoint point,
        string metricName,
        decimal value,
        string unit,
        int minutesAgo)
    {
        return new CollectionDataRecord
        {
            Id = id,
            CollectionPointId = point.Id,
            MetricName = metricName,
            Value = value,
            Unit = unit,
            CollectedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)
        };
    }
}

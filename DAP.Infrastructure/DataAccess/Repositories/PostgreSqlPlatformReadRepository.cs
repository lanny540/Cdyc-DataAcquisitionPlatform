using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Queries;
using Dapper;
using Npgsql;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 提供基于 Dapper 的平台只读查询仓储。
/// </summary>
public sealed class PostgreSqlPlatformReadRepository : IPlatformReadRepository
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlPlatformReadRepository"/> 实例。
    /// </summary>
    /// <param name="dataSource">PostgreSQL 数据源。</param>
    public PostgreSqlPlatformReadRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<CollectionPointReadModel> items = await connection.QueryAsync<CollectionPointReadModel>(
            new CommandDefinition(
                PlatformReadSql.CollectionPoints,
                cancellationToken: cancellationToken));

        return items.Select(MapCollectionPoint).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<CollectionDataRecordReadModel> items = await connection.QueryAsync<CollectionDataRecordReadModel>(
            new CommandDefinition(
                PlatformReadSql.CollectionData,
                new {Limit = Math.Max(1, limit)},
                cancellationToken: cancellationToken));

        return items.Select(MapCollectionDataRecord).ToArray();
    }

    /// <inheritdoc />
    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var summary = await connection.QuerySingleAsync<DashboardSummaryModel>(new CommandDefinition(
            PlatformReadSql.DashboardSummary,
            cancellationToken: cancellationToken));

        IReadOnlyCollection<CollectionDataRecordDto>
            latestRecords = await GetCollectionDataAsync(10, cancellationToken);
        IReadOnlyCollection<CollectionPointDto> collectionPoints = await GetCollectionPointsAsync(cancellationToken);

        return new DashboardOverviewDto(
            summary.TotalCollectionPoints,
            summary.OnlineCollectionPoints,
            summary.OfflineCollectionPoints,
            summary.LocalSourcedCollectionPoints,
            latestRecords,
            collectionPoints,
            CreateMockSprintLanes(),
            CreateMockMembers());
    }

    private static IReadOnlyCollection<SprintLaneDto> CreateMockSprintLanes()
    {
        return new[]
        {
            new SprintLaneDto(
                "TO DO",
                "待确认 04",
                new[]
                {
                    new SprintTaskCardDto(
                        "统一边缘网关采样节拍与缓存写入策略",
                        "统一边缘网关的轮询周期、缓存窗口和异常回退策略，减少波峰时段的数据抖动。",
                        "To do", "Planning", "#E9EEFF", "#4F46E5", "DAP-101", "3 项依赖", "#4F46E5", 24, "LY", "Lyn",
                        "边缘采集方案评估",
                        new[]
                        {
                            new InspectorMetricDto("刷新周期", "30 min", "#4F46E5"),
                            new InspectorMetricDto("依赖模块", "03", "#4F46E5"),
                            new InspectorMetricDto("阻塞项", "02", "#EF5A6F")
                        },
                        new[]
                        {
                            new ActivityLogDto("09:05", "完成采样频率调整方案草图", "#4F46E5"),
                            new ActivityLogDto("09:42", "补充缓存落盘异常处理边界", "#14B8A6"),
                            new ActivityLogDto("10:16", "等待平台接口约束确认", "#F59E0B")
                        }),
                    new SprintTaskCardDto(
                        "清理历史采集模板并补齐站点映射字段",
                        "整理旧模板中的无效字段，补齐采集点与站点映射的缺失信息，为后续迁移做准备。",
                        "To do", "Backlog", "#EEF2FF", "#475569", "DAP-117", "5 个字段", "#94A3B8", 18, "WX", "Wes",
                        "基础数据治理",
                        new[]
                        {
                            new InspectorMetricDto("待清理模板", "12", "#94A3B8"),
                            new InspectorMetricDto("站点映射", "05", "#14B8A6"),
                            new InspectorMetricDto("风险项", "01", "#EF5A6F")
                        },
                        new[]
                        {
                            new ActivityLogDto("08:52", "完成旧模板字段盘点", "#94A3B8"),
                            new ActivityLogDto("09:37", "补录 5 个站点映射字段", "#14B8A6"),
                            new ActivityLogDto("10:20", "待业务确认废弃模板清单", "#F59E0B")
                        })
                }),
            new SprintLaneDto(
                "IN PROGRESS",
                "执行中 05",
                new[]
                {
                    new SprintTaskCardDto(
                        "将客户端仪表盘切换为设计工作台式布局",
                        "对照参考图重做主工作台，改造导航、卡片层级和右侧 inspector，让信息结构更清晰。",
                        "In progress", "Design", "#E7FAF7", "#0F766E", "DAP-142", "68% 完成", "#14B8A6", 68, "RB",
                        "Robin", "体验设计与交互联调",
                        new[]
                        {
                            new InspectorMetricDto("刷新周期", "15 min", "#4F46E5"),
                            new InspectorMetricDto("同步批次", "03", "#14B8A6"),
                            new InspectorMetricDto("阻塞项", "01", "#EF5A6F")
                        },
                        new[]
                        {
                            new ActivityLogDto("09:18", "完成主窗口三栏壳层重构", "#4F46E5"),
                            new ActivityLogDto("10:02", "为主页补充 mock 任务卡片和详情侧栏", "#14B8A6"),
                            new ActivityLogDto("10:41", "联动主题切换，校准明暗色板", "#F59E0B")
                        }),
                    new SprintTaskCardDto(
                        "修正断线重连后的本地同步状态展示",
                        "让断线恢复后的本地同步状态与服务端快照重新对齐，避免界面长时间显示旧状态。",
                        "In progress", "Refactor", "#FFF4DB", "#B45309", "DAP-138", "2 个分支", "#F59E0B", 52, "ST",
                        "Stewie", "同步状态修正",
                        new[]
                        {
                            new InspectorMetricDto("待对齐状态", "07", "#F59E0B"),
                            new InspectorMetricDto("回归用例", "04", "#14B8A6"),
                            new InspectorMetricDto("异常节点", "02", "#EF5A6F")
                        },
                        new[]
                        {
                            new ActivityLogDto("09:28", "定位断线恢复后的状态残留问题", "#F59E0B"),
                            new ActivityLogDto("09:55", "补充本地状态覆盖逻辑", "#14B8A6"),
                            new ActivityLogDto("10:32", "准备回归断线恢复链路", "#4F46E5")
                        }),
                    new SprintTaskCardDto(
                        "下发 Modbus TCP 采集模板到新工位",
                        "将新建工位所需的 Modbus TCP 模板参数打包下发，并验证首轮采集是否稳定。",
                        "In progress", "Deploy", "#FFE8EC", "#BE123C", "DAP-145", "1 台设备", "#EF5A6F", 41, "QH", "Qin",
                        "工位上线准备",
                        new[]
                        {
                            new InspectorMetricDto("目标工位", "01", "#EF5A6F"),
                            new InspectorMetricDto("已校验寄存器", "08", "#14B8A6"),
                            new InspectorMetricDto("告警项", "01", "#F59E0B")
                        },
                        new[]
                        {
                            new ActivityLogDto("08:47", "完成模板参数包导出", "#EF5A6F"),
                            new ActivityLogDto("09:40", "校验寄存器映射前 8 项", "#14B8A6"),
                            new ActivityLogDto("10:26", "等待现场设备重启确认", "#F59E0B")
                        })
                }),
            new SprintLaneDto(
                "REVIEW",
                "待验收 03",
                new[]
                {
                    new SprintTaskCardDto(
                        "补齐服务端总览接口的空值与降级处理",
                        "完善服务端总览接口的空值保护和异常降级，保证桌面端在网络抖动时仍能稳定显示。",
                        "Review", "API", "#E8F1FF", "#1D4ED8", "DAP-133", "待联调", "#3B82F6", 88, "AN", "Anne", "接口稳定性校验",
                        new[]
                        {
                            new InspectorMetricDto("异常分支", "06", "#3B82F6"),
                            new InspectorMetricDto("降级场景", "03", "#14B8A6"),
                            new InspectorMetricDto("待确认", "01", "#F59E0B")
                        },
                        new[]
                        {
                            new ActivityLogDto("09:11", "补齐 LatestRecords 空集合降级", "#3B82F6"),
                            new ActivityLogDto("09:58", "增加接口失败提示文案", "#14B8A6"),
                            new ActivityLogDto("10:44", "等待桌面端联调验证", "#F59E0B")
                        }),
                    new SprintTaskCardDto(
                        "验证本地 SQLite 同步结果与服务端快照一致性",
                        "对比本地 SQLite 与服务端采集点快照，校验同步后字段与状态是否完全一致。",
                        "Review", "QA", "#F3E8FF", "#7E22CE", "DAP-136", "6 条用例", "#8B5CF6", 79, "MK", "Mika",
                        "同步一致性验证",
                        new[]
                        {
                            new InspectorMetricDto("回归用例", "06", "#8B5CF6"),
                            new InspectorMetricDto("通过率", "83%", "#14B8A6"),
                            new InspectorMetricDto("待复核", "01", "#F59E0B")
                        },
                        new[]
                        {
                            new ActivityLogDto("08:58", "完成本地与服务端字段映射核对", "#8B5CF6"),
                            new ActivityLogDto("09:49", "发现 1 条同步状态延迟", "#F59E0B"),
                            new ActivityLogDto("10:35", "回归验证 5 条用例通过", "#14B8A6")
                        })
                })
        };
    }

    private static IReadOnlyCollection<MemberAvatarDto> CreateMockMembers()
    {
        return new[]
        {
            new MemberAvatarDto("LY", "#4F46E5"),
            new MemberAvatarDto("RB", "#14B8A6"),
            new MemberAvatarDto("ST", "#F59E0B"),
            new MemberAvatarDto("MK", "#8B5CF6")
        };
    }

    private sealed record DashboardSummaryModel(
        int TotalCollectionPoints,
        int OnlineCollectionPoints,
        int OfflineCollectionPoints,
        int LocalSourcedCollectionPoints);

    private sealed record CollectionPointReadModel(
        Guid Id,
        string Code,
        string Name,
        string Protocol,
        string Endpoint,
        bool IsEnabled,
        string CommunicationStatus,
        string Source,
        string? LastError,
        DateTime UpdatedAt);

    private sealed record CollectionDataRecordReadModel(
        Guid Id,
        Guid CollectionPointId,
        string CollectionPointCode,
        string CollectionPointName,
        string MetricName,
        decimal Value,
        string Unit,
        DateTime CollectedAt);

    private static CollectionPointDto MapCollectionPoint(CollectionPointReadModel item)
    {
        return new CollectionPointDto(
            item.Id,
            item.Code,
            item.Name,
            item.Protocol,
            item.Endpoint,
            item.IsEnabled,
            item.CommunicationStatus,
            item.Source,
            item.LastError,
            new DateTimeOffset(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc)));
    }

    private static CollectionDataRecordDto MapCollectionDataRecord(CollectionDataRecordReadModel item)
    {
        return new CollectionDataRecordDto(
            item.Id,
            item.CollectionPointId,
            item.CollectionPointCode,
            item.CollectionPointName,
            item.MetricName,
            item.Value,
            item.Unit,
            new DateTimeOffset(DateTime.SpecifyKind(item.CollectedAt, DateTimeKind.Utc)));
    }
}

using System.Globalization;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DAP.Tests.UnitTests;

/// <summary>
/// AdminAuditService 的单元测试。
///
/// 覆盖的核心规则：
/// - 时间筛选转 UTC（From 含边界、To 不含边界）；
/// - TargetType / Result 精确匹配，分页参数钳制（页码 ≥ 1，每页 1–200）；
/// - 按发生时间倒序返回，附带去重后的对象类型下拉列表；
/// - CSV 导出带 UTF-8 BOM，字段按 RFC 4180 转义（含公式注入防护）。
///
/// SQLite 适配：EF Core SQLite 不支持 DateTimeOffset 列的排序与范围比较
/// （OccurredAt 恰好是本服务全部查询的排序键），因此本文件使用私有模型定制器，
/// 在建模后为全部 DateTimeOffset 属性挂「UtcTicks ⇄ DateTimeOffset」值转换器，
/// 使排序与比较按长整型翻译。绝对时间语义与生产的 timestamptz 一致。
/// </summary>
public sealed class AdminAuditServiceTests
{
    private static readonly DateTimeOffset T1 = new(2026, 11, 19, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 11, 19, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T4 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T5 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static void Seed(AuditSqliteDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();

        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = T1,
                UserName = "user-a",
                DepartmentName = "一部",
                ActionCode = "user.create",
                TargetType = "user",
                TargetId = "id-1",
                TargetName = "name-a",
                Result = "success",
                Detail = "before=1,after=2",
                Ip = "10.1.2.3"
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = T2,
                UserName = "user-a",
                DepartmentName = "一部",
                ActionCode = "user.update",
                TargetType = "domain",
                TargetId = "id-2",
                TargetName = null,
                Result = "failed",
                Detail = null,
                Ip = null
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = T3,
                UserName = "user-b",
                DepartmentName = null,
                ActionCode = "shift.import",
                TargetType = "user",
                TargetId = "id-3",
                TargetName = "name-c",
                Result = "success",
                Detail = null,
                Ip = null
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = T4,
                UserName = null,
                DepartmentName = null,
                ActionCode = "domain_page.update",
                TargetType = null,
                TargetId = null,
                TargetName = null,
                Result = "success",
                Detail = null,
                Ip = null
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = T5,
                UserName = "user-c",
                DepartmentName = "二部",
                ActionCode = "shift.import",
                TargetType = "shift-schedule",
                TargetId = "id-5",
                TargetName = "name-e",
                Result = "success",
                Detail = "=cmd|'/c calc'!A0",
                Ip = "10.1.2.4"
            });

        db.SaveChanges();
    }

    private static AdminAuditService CreateService(DataAcquisitionPlatformDbContext db)
    {
        return new AdminAuditService(db);
    }

    [Fact]
    public async Task QueryAsync_Should_Return_All_Ordered_By_Time_Desc()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(null, null, null, null, null));

        Assert.Equal(5, page.Total);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(T5, page.Items[0].OccurredAt);
        Assert.Equal(T1, page.Items[^1].OccurredAt);
        Assert.Equal(1, page.Page);
        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task QueryAsync_Time_Range_From_Inclusive_To_Exclusive()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        // 页面传进来的日期带本地时区（+08:00），服务端必须转成 UTC 再比较。
        DateTimeOffset from = new(2026, 11, 19, 15, 0, 0, TimeSpan.FromHours(8)); // 07:00 UTC
        DateTimeOffset to = new(2026, 11, 19, 18, 0, 0, TimeSpan.FromHours(8)); // 10:00 UTC

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(from, to, null, null, null));

        Assert.Equal(3, page.Total);
        Assert.Equal([T4, T3, T2], page.Items.Select(item => item.OccurredAt).ToArray());
    }

    [Fact]
    public async Task QueryAsync_Result_Filter_Should_Match_Exactly()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(null, null, null, null, "failed"));

        Assert.Equal(1, page.Total);
        Assert.Equal("user.update", page.Items.Single().ActionCode);
    }

    [Fact]
    public async Task QueryAsync_TargetType_Filter_Should_Match_Exactly()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(null, null, null, "user", null));

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, item => Assert.Equal("user", item.TargetType));
    }

    [Fact]
    public async Task QueryAsync_Should_Paginate()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(null, null, null, null, null, 2, 2));

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal([T3, T2], page.Items.Select(item => item.OccurredAt).ToArray());
    }

    [Fact]
    public async Task QueryAsync_Should_Clamp_Page_And_PageSize()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminAuditService service = CreateService(db);

        AuditLogPageDto page = await service.QueryAsync(
            new AuditLogFilter(null, null, null, null, null, 0, 0));
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);

        // 每页上限 200：导出走独立方法，不靠调大分页绕开限制。
        AuditLogPageDto clamped = await service.QueryAsync(
            new AuditLogFilter(null, null, null, null, null, 1, 500));
        Assert.Equal(AdminAuditService.MaxPageSize, clamped.PageSize);
        Assert.Equal(200, clamped.PageSize);
    }

    [Fact]
    public async Task QueryAsync_TargetTypes_Should_Be_Distinct_Sorted_And_Non_Null()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        AuditLogPageDto page = await CreateService(db).QueryAsync(
            new AuditLogFilter(null, null, null, null, null));

        Assert.Equal(["domain", "shift-schedule", "user"], page.TargetTypes);
    }

    [Fact]
    public async Task ExportCsvAsync_Should_Start_With_Bom_And_Header()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        string csv = await CreateService(db).ExportCsvAsync(
            new AuditLogFilter(null, null, null, null, null));

        // UTF-8 BOM：否则 Excel 打开中文会乱码。
        Assert.StartsWith("\uFEFF", csv);
        Assert.StartsWith("\uFEFF时间,操作人,部门,动作,对象类型,对象名称,结果,来源IP,详情", csv);
        // 5 条数据 + 表头。
        Assert.Equal(6, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task ExportCsvAsync_Should_Map_Fields_And_Escape_Detail()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        string csv = await CreateService(db).ExportCsvAsync(
            new AuditLogFilter(T1.AddMinutes(-1), T1.AddMinutes(1), null, null, null));

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        // 时间按本地时区展示（与生产一致），其余字段一一对应。
        string expectedTime = T1.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string expectedRow =
            $"{expectedTime},user-a,一部,user.create,user,name-a,success,10.1.2.3,\"before=1,after=2\"";
        Assert.Equal(expectedRow, lines[1].TrimEnd('\r'));
    }

    [Fact]
    public async Task ExportCsvAsync_Should_Prefix_Formula_Like_Detail()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        string csv = await CreateService(db).ExportCsvAsync(
            new AuditLogFilter(T5.AddMinutes(-1), T5.AddMinutes(1), null, null, null));

        // 以 = 开头的值加单引号前缀，阻止 Excel 公式执行。
        Assert.Contains("'=cmd|'/c calc'!A0", csv);
    }

    [Fact]
    public async Task ExportCsvAsync_Should_Respect_Filter()
    {
        using AuditSqliteDatabase database = new();
        Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        DateTimeOffset from = new(2026, 11, 19, 16, 0, 0, TimeSpan.FromHours(8)); // 08:00 UTC
        string csv = await CreateService(db).ExportCsvAsync(
            new AuditLogFilter(from, null, null, null, null));

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // 表头 + T3/T4/T5 对应的 3 行
    }

    /// <summary>
    /// 基于 <see cref="SqliteTestDatabase"/> 的审计专用数据库：
    /// 共享同一内存库与建好的表，但上下文模型额外挂 DateTimeOffset 值转换器。
    /// </summary>
    private sealed class AuditSqliteDatabase : IDisposable
    {
        private readonly SqliteTestDatabase _inner;
        private readonly DataAcquisitionPlatformDbContext _connectionHolder;
        private readonly SqliteConnection _connection;

        public AuditSqliteDatabase()
        {
            _inner = new SqliteTestDatabase();
            _connectionHolder = _inner.CreateContext();
            _connection = (SqliteConnection)_connectionHolder.Database.GetDbConnection();
        }

        public DataAcquisitionPlatformDbContext CreateContext()
        {
            DbContextOptionsBuilder<DataAcquisitionPlatformDbContext> builder = new();
            builder
                .UseSqlite(_connection)
                .ReplaceService<IModelCustomizer, DateTimeOffsetTicksModelCustomizer>();

            return new DataAcquisitionPlatformDbContext(builder.Options);
        }

        public void Dispose()
        {
            _connectionHolder.Dispose();
            _inner.Dispose();
        }
    }

    /// <summary>
    /// 在共享骨架的归一化（去 PG 默认值 / btrim 改写 / jsonb 退化 TEXT）基础上，
    /// 为全部 DateTimeOffset 属性挂 UtcTicks 转换器，使排序与比较可在 SQLite 翻译。
    /// </summary>
    private sealed class DateTimeOffsetTicksModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (IReadOnlyCheckConstraint checkConstraint in entityType.GetCheckConstraints().ToList())
                {
                    string? rewritten = checkConstraint.Sql?.Replace("btrim(", "trim(", StringComparison.Ordinal);
                    if (rewritten is null || rewritten == checkConstraint.Sql)
                    {
                        continue;
                    }

                    entityType.RemoveCheckConstraint(checkConstraint.Name!);
                    entityType.AddCheckConstraint(checkConstraint.Name!, rewritten);
                }

                foreach (IMutableProperty property in entityType.GetProperties())
                {
                    property.SetDefaultValueSql(null);

                    if (property.GetColumnType() == "jsonb")
                    {
                        property.SetColumnType("TEXT");
                    }

                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(
                            value => value.UtcTicks,
                            ticks => new DateTimeOffset(ticks, TimeSpan.Zero)));
                    }
                }
            }
        }
    }
}

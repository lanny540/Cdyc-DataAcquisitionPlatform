using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DAP.Tests.UnitTests.TestDoubles;

/// <summary>
/// 基于 SQLite 内存库的测试数据库。
///
/// 大量安全与管理服务（用户、角色、域、目录同步、园区能源等）直接依赖
/// <see cref="DataAcquisitionPlatformDbContext"/>，此前因需要真实 PostgreSQL 而无法纳入单元测试。
/// 本工厂用同一个常驻连接承载 SQLite 内存库，让这些服务在测试中真实执行 EF 查询与事务，
/// 数据仅在测试内可见、随连接释放销毁。
///
/// 与生产 PostgreSQL 模型的差异由 <see cref="SqliteTestModelCustomizer"/> 统一归一化：
/// 去掉 PG 专属默认值表达式（gen_random_uuid / now() / '::jsonb'），jsonb 列退化为 TEXT。
/// 归一化只影响测试库建表，不改动生产模型与迁移。
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// 创建内存库并建表。连接在工厂生命周期内保持打开，多个 DbContext 共享同一份数据。
    /// </summary>
    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using DataAcquisitionPlatformDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// 创建指向同一内存库的新 DbContext。每个服务/断言各自开上下文，贴近真实请求边界，
    /// 同时天然验证「写入后另开上下文可读」。
    /// </summary>
    public DataAcquisitionPlatformDbContext CreateContext()
    {
        DbContextOptionsBuilder<DataAcquisitionPlatformDbContext> builder = new();
        builder
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>();

        return new DataAcquisitionPlatformDbContext(builder.Options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

/// <summary>
/// 在真实模型构建完成后把 PostgreSQL 专属设置归一化为 SQLite 可执行的形式。
/// </summary>
internal sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // 先照常执行 DataAcquisitionPlatformDbContext.OnModelCreating（含全部实体配置）。
        base.Customize(modelBuilder, context);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            // CHECK 约束里的 btrim() 是 PostgreSQL 函数，SQLite 同义函数是 trim()。
            foreach (IReadOnlyCheckConstraint checkConstraint in entityType.GetCheckConstraints().ToList())
            {
                string? rewritten = checkConstraint.Sql?.Replace("btrim(", "trim(", StringComparison.Ordinal);
                if (rewritten is null || rewritten == checkConstraint.Sql) continue;

                entityType.RemoveCheckConstraint(checkConstraint.Name!);
                entityType.AddCheckConstraint(checkConstraint.Name!, rewritten);
            }

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                // 'gen_random_uuid()' / 'now()' / '\'{}\'::jsonb' 在 SQLite 中是语法错误或未知函数。
                // 去掉默认值后，测试数据由用例显式提供，行为完全确定。
                property.SetDefaultValueSql(null);

                // jsonb 是 PG 专属列类型；JSON 在测试中按普通文本读写。
                if (property.GetColumnType() == "jsonb")
                    property.SetColumnType("TEXT");
            }
        }
    }
}

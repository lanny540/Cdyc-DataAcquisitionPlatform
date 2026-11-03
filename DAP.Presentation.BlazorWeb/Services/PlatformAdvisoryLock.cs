using System.Text;
using Npgsql;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 跨实例的互斥锁，基于 PostgreSQL 会话级咨询锁（advisory lock）。
///
/// 后台任务（目录同步、审计清理）都是「每个实例各跑一份」的 <see cref="BackgroundService"/>。
/// 部署两台实例时，两台会在同一分钟一起做全量同步：唯一索引冲突、审计记录翻倍、
/// updated_at 反复抖动；审计清理则是两台同时删同一批行。用数据库自己的锁最省事——
/// 不需要额外的中间件，进程崩溃时连接断开、锁自动释放，不会留下死锁。
/// </summary>
public sealed class PlatformAdvisoryLock(
    PostgreSqlConnectionSettings settings,
    ILogger<PlatformAdvisoryLock> logger)
{
    /// <summary>
    /// 锁键前缀。同一套数据库可能被多个应用共用，加前缀避免与别人的 advisory lock 撞号。
    /// </summary>
    private const string KeyPrefix = "dap";

    /// <summary>
    /// 尝试获取命名锁。拿不到返回 null（表示别的实例正在做同一件事），调用方跳过本轮即可。
    /// </summary>
    /// <param name="name">锁名，如 directory-sync、audit-retention。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回释放句柄；失败返回 null。</returns>
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var key = ComputeKey(name);

        NpgsqlConnection connection = new(settings.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand command =
                new("select pg_try_advisory_lock(@key)", connection);

            command.Parameters.AddWithValue("key", key);

            var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;

            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Handle(connection, key, name, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// 把锁名散列成 advisory lock 的 64 位键。用 FNV-1a：稳定、无依赖，
    /// 不用 <see cref="string.GetHashCode()"/>——它每个进程带随机种子，重启后锁名会变。
    /// </summary>
    /// <param name="name">锁名。</param>
    /// <returns>64 位键。</returns>
    public static long ComputeKey(string name)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;

        foreach (var item in Encoding.UTF8.GetBytes($"{KeyPrefix}:{name}"))
        {
            hash ^= item;
            hash *= prime;
        }

        return unchecked((long)hash);
    }

    private sealed class Handle(
        NpgsqlConnection connection,
        long key,
        string name,
        ILogger logger) : IAsyncDisposable
    {
        private bool _released;

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            try
            {
                await using NpgsqlCommand command =
                    new("select pg_advisory_unlock(@key)", connection);

                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            catch (Exception exception)
            {
                // 解锁失败不影响正确性：连接关闭时会话级锁自动释放。
                logger.LogDebug(exception, "释放咨询锁 {LockName} 失败，连接关闭后将自动释放。", name);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}

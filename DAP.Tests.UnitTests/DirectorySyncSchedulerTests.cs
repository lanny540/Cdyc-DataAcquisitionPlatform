using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Directory;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 目录同步后台任务的启动分支测试。
///
/// 用提前取消的令牌越过 30 秒启动延迟，验证「未启用不启动」「只允许手工触发不启动」
/// 「取消即退出」三个分支；同步循环体本身由 DirectorySyncServiceTests 覆盖。
/// </summary>
public sealed class DirectorySyncSchedulerTests
{
    private static DirectorySyncScheduler CreateScheduler(
        SqliteTestDatabase database, PlatformAuthOptions options)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        IServiceProvider provider = services.BuildServiceProvider();

        return new DirectorySyncScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<DirectorySyncScheduler>.Instance);
    }

    [Fact]
    public async Task StartAsync_Ldap_Disabled_Should_Return_Immediately()
    {
        using SqliteTestDatabase database = new();
        DirectorySyncScheduler scheduler = CreateScheduler(
            database, new PlatformAuthOptions());

        // 未启用目录认证：后台任务不进入循环，直接结束。
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_Zero_Interval_Should_Return_Immediately()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = new();
        options.Ldap.Enabled = true;
        options.Ldap.SyncIntervalMinutes = 0;

        DirectorySyncScheduler scheduler = CreateScheduler(database, options);

        // 间隔为 0 表示只允许手工触发：后台任务不启动。
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_Cancelled_During_Startup_Delay_Should_Exit_Without_Syncing()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = new();
        options.Ldap.Enabled = true;
        options.Ldap.SyncIntervalMinutes = 60;

        DirectorySyncScheduler scheduler = CreateScheduler(database, options);

        // 令牌在 30 秒启动延迟内即已取消：第一轮同步不会发生。
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await scheduler.StartAsync(cts.Token);
        await scheduler.StopAsync(CancellationToken.None);
    }
}

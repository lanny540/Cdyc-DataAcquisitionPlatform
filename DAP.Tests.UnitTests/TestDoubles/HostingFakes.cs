using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DAP.Tests.UnitTests.TestDoubles;

/// <summary>
/// 测试用宿主环境假件集合。
///
/// 能源字典初始化器、开发数据初始化器、认证初始化器与导航视图服务等依赖
/// IServiceScopeFactory / IConfiguration / IHostEnvironment，这些假件让它们
/// 在单元测试中真实执行，而不需要拉起整个 Web 宿主。
/// </summary>
internal static class HostingFakes
{
    /// <summary>
    /// 创建指向 SQLite 内存库作用域工厂。DbContext 每次解析都新开上下文，
    /// 与真实请求边界一致；数据共享同一个 <see cref="SqliteTestDatabase"/>。
    /// </summary>
    public static IServiceScopeFactory CreateScopeFactory(SqliteTestDatabase database)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        IServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// 创建内存配置。
    /// </summary>
    public static IConfiguration CreateConfiguration(params (string Key, string? Value)[] pairs)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
    }

    /// <summary>
    /// 开发环境假件（EnvironmentName = Development）。
    /// </summary>
    public static IHostEnvironment CreateDevelopmentEnvironment()
    {
        return new TestHostEnvironment
        {
            EnvironmentName = Environments.Development, ApplicationName = "DAP.Tests.UnitTests"
        };
    }

    /// <summary>
    /// 生产环境假件（EnvironmentName = Production）。
    /// </summary>
    public static IHostEnvironment CreateProductionEnvironment()
    {
        return new TestHostEnvironment
        {
            EnvironmentName = Environments.Production, ApplicationName = "DAP.Tests.UnitTests"
        };
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = NullFileProvider.Instance;

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public string EnvironmentName { get; set; } = Environments.Development;
    }

    private sealed class NullFileProvider : IFileProvider
    {
        public static readonly NullFileProvider Instance = new();

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return NotFoundDirectoryContents.Singleton;
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return NullChangeToken.Singleton;
        }
    }
}

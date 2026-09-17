using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DAP.Infrastructure.DataAccess.Persistence;

/// <summary>
/// 为 EF Core 设计时命令提供 <see cref="DataAcquisitionPlatformDbContext"/> 创建能力。
/// </summary>
public sealed class DataAcquisitionPlatformDbContextFactory
    : IDesignTimeDbContextFactory<DataAcquisitionPlatformDbContext>
{
    /// <inheritdoc />
    public DataAcquisitionPlatformDbContext CreateDbContext(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var basePath = ResolveConfigurationBasePath();

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", false)
            .AddJsonFile($"appsettings.{environmentName}.json", true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSql")
                               ?? throw new InvalidOperationException("未找到 ConnectionStrings:PostgreSql 配置。");

        var optionsBuilder = new DbContextOptionsBuilder<DataAcquisitionPlatformDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DataAcquisitionPlatformDbContext(optionsBuilder.Options);
    }

    private static string ResolveConfigurationBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        DirectoryInfo? directory = new(currentDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "DAP.Presentation.BlazorWeb", "appsettings.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)
                       ?? throw new InvalidOperationException("无法解析 BlazorWeb 配置目录。");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "未找到 DAP.Presentation.BlazorWeb/appsettings.json，无法为 EF Core 设计时命令加载连接配置。");
    }
}

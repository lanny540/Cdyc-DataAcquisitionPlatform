using Microsoft.AspNetCore.DataProtection;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 数据保护（Cookie 加密、防伪令牌）密钥环的位置。注册为单例，仅用于启动日志，
/// 让运维在日志里就能看到「密钥存在哪」，而不是到了掉线时才去猜。
/// </summary>
/// <param name="DirectoryPath">密钥目录绝对路径。</param>
public sealed record DataProtectionKeyLocation(string DirectoryPath);

/// <summary>
/// 密钥环注册。默认放在内容根目录下的 <c>data-protection-keys</c>，
/// 可用 <c>Auth:DataProtection:KeyPath</c> 改到持久卷。
///
/// 为什么必须落盘：密钥环决定 Cookie 能否解密。放在默认位置（运行账户的 profile 目录）时，
/// 重新部署、换容器、换服务账户都会丢密钥——所有已签发的会话立即作废，
/// 现场表现是「所有人同时被踢下线」，而这个现象很像网络故障，极难定位。
/// 多实例部署下更严重：每个实例各有一把钥匙，用户会在实例之间来回掉登录。
/// </summary>
public static class PlatformDataProtectionExtensions
{
    /// <summary>
    /// 配置项名称：密钥目录。
    /// </summary>
    public const string KeyPathConfigurationKey = "Auth:DataProtection:KeyPath";

    /// <summary>
    /// 注册持久化的数据保护密钥环。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="environment">宿主环境，用于解析相对路径。</param>
    /// <returns>当前服务集合。</returns>
    public static IServiceCollection AddPlatformDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var configured = configuration[KeyPathConfigurationKey];

        var directoryPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data-protection-keys")
            : Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configured),
                environment.ContentRootPath);

        try
        {
            // 必须写全 System.IO：本命名空间下有 Directory 子命名空间（Security.Directory），
            // 直接写 Directory.CreateDirectory 会被解析成命名空间。
            System.IO.Directory.CreateDirectory(directoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 宁可启动失败，也不要退回默认位置：静默退回去，密钥就落在临时目录里，
            // 下次部署照样全员掉线，而且日志里没有任何线索。
            throw new InvalidOperationException(
                $"数据保护密钥目录不可写：{directoryPath}。"
                + "请检查目录权限，或把 Auth:DataProtection:KeyPath 指向可写且持久化的路径。",
                exception);
        }

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(directoryPath))
            // 应用名决定密钥的用途隔离：不显式设置时取内容根目录名，
            // 改名或换部署路径都会导致旧密钥「找不到」，等同丢密钥。
            .SetApplicationName("DAP");

        services.AddSingleton(new DataProtectionKeyLocation(directoryPath));

        // 采集服务器凭据（ClientSecret 等）复用同一把密钥加密后落库。
        services.AddSingleton<PlatformSecretProtector>();

        return services;
    }
}

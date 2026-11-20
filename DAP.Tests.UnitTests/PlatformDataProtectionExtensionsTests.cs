using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.PlatformDataProtectionExtensions"/> 密钥环注册测试。
///
/// 密钥环决定 Cookie 能否解密：位置可解析、目录创建、应用名固定、
/// 目录不可写时宁可启动失败也不静默退回默认位置。
/// </summary>
public class PlatformDataProtectionExtensionsTests
{
    /// <summary>最小 Web 宿主环境假件（内容根指向临时目录）。</summary>
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DAP.Tests.UnitTests";

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void 未配置路径_密钥环落内容根下默认目录()
    {
        using TempDirectory contentRoot = new();
        TestWebHostEnvironment environment = new() { ContentRootPath = contentRoot.Path };
        IConfiguration configuration = HostingFakes.CreateConfiguration();

        ServiceCollection services = new();
        services.AddPlatformDataProtection(configuration, environment);
        using ServiceProvider provider = services.BuildServiceProvider();

        DataProtectionKeyLocation location = provider.GetRequiredService<DataProtectionKeyLocation>();
        string expected = Path.Combine(contentRoot.Path, "data-protection-keys");

        Assert.Equal(expected, location.DirectoryPath);
        Assert.True(Directory.Exists(expected), "密钥目录应被创建");
        // 采集服务器凭据加密复用同一把密钥。
        Assert.NotNull(provider.GetRequiredService<PlatformSecretProtector>());
    }

    [Fact]
    public void 配置相对路径_按内容根解析()
    {
        using TempDirectory contentRoot = new();
        TestWebHostEnvironment environment = new() { ContentRootPath = contentRoot.Path };
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:DataProtection:KeyPath", "persistent-keys"));

        ServiceCollection services = new();
        services.AddPlatformDataProtection(configuration, environment);
        using ServiceProvider provider = services.BuildServiceProvider();

        DataProtectionKeyLocation location = provider.GetRequiredService<DataProtectionKeyLocation>();

        Assert.Equal(
            Path.Combine(contentRoot.Path, "persistent-keys"),
            location.DirectoryPath);
        Assert.True(Directory.Exists(location.DirectoryPath));
    }

    [Fact]
    public void 密钥目录不可写_宁可启动失败_抛出带路径的异常()
    {
        using TempDirectory contentRoot = new();
        // 在内容根下放一个文件，再让密钥目录指向该文件之下：CreateDirectory 必然失败。
        string blockingFile = Path.Combine(contentRoot.Path, "blocker");
        File.WriteAllText(blockingFile, "x");

        TestWebHostEnvironment environment = new() { ContentRootPath = contentRoot.Path };
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:DataProtection:KeyPath", Path.Combine(blockingFile, "keys")));

        ServiceCollection services = new();

        // 静默退回默认位置会让密钥落在临时目录里，下次部署全员掉线且无日志线索。
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                services.AddPlatformDataProtection(configuration, environment));

        Assert.Contains("数据保护密钥目录不可写", exception.Message);
    }

    [Fact]
    public void 密钥环_跨提供者实例可复用_密钥可解密()
    {
        using TempDirectory contentRoot = new();
        TestWebHostEnvironment environment = new() { ContentRootPath = contentRoot.Path };
        IConfiguration configuration = HostingFakes.CreateConfiguration();

        ServiceCollection services = new();
        services.AddPlatformDataProtection(configuration, environment);
        using ServiceProvider first = services.BuildServiceProvider();

        // 换一个根提供者（模拟重新部署）：密钥仍在磁盘，同一应用名应能解开旧密文。
        ServiceCollection secondServices = new();
        secondServices.AddPlatformDataProtection(configuration, environment);
        using ServiceProvider second = secondServices.BuildServiceProvider();

        PlatformSecretProtector protectorOne = first.GetRequiredService<PlatformSecretProtector>();
        PlatformSecretProtector protectorTwo = second.GetRequiredService<PlatformSecretProtector>();

        string cipher = protectorOne.Protect("ClientSecret-42");
        Assert.Equal("ClientSecret-42", protectorTwo.Unprotect(cipher));
    }

    /// <summary>测试内临时目录，用完即删。</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dap-keys-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // 清理失败不影响测试结果。
            }
        }
    }
}

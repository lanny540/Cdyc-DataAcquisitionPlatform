using System.Text.RegularExpressions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 应用版本号守卫测试：顶栏版本说明必须跟随程序集版本自动显示。
///
/// 约定（AGENTS.md「提交前更新文档（强制）」）：全解决方案唯一的版本源是
/// 根目录 <c>Directory.Build.props</c>，其中 Version / AssemblyVersion / FileVersion
/// 三个属性取值一致，与最新提交的代码日期同步（格式 yyyy.M.d）；
/// Web 顶栏徽章与桌面侧栏底部的版本文字都读程序集版本，不硬编码。
/// 这些约定若只写在文档里很容易被无意破坏，因此用测试固化。
/// </summary>
public partial class AppVersionGuardTests
{
    /// <summary>
    /// 全解决方案唯一的版本定义文件。版本属性散落到 csproj 会与集中定义打架
    /// （csproj 优先级更高，导致程序集版本与产品版本不一致），因此在此断言唯一。
    /// </summary>
    private const string VersionPropsFile = "Directory.Build.props";

    /// <summary>
    /// Directory.Build.props 必须是唯一版本源：三个版本属性齐全、取值一致且为日期制
    /// （yyyy.M.d，各段均小于 65535，合法的程序集版本），否则顶栏会退回 v1.0.0
    /// 或出现版本不一致。
    /// </summary>
    [Fact]
    public void Version_Props_Should_Be_The_Single_Date_Source()
    {
        string text = ReadRepoFile(VersionPropsFile);

        string? version = SingleMatch(text, "Version");
        string? assemblyVersion = SingleMatch(text, "AssemblyVersion");
        string? fileVersion = SingleMatch(text, "FileVersion");

        Assert.True(version is not null && assemblyVersion is not null && fileVersion is not null,
            $"{VersionPropsFile} 必须同时定义 Version / AssemblyVersion / FileVersion 三个属性");

        Assert.True(version == assemblyVersion && version == fileVersion,
            $"{VersionPropsFile} 三个版本属性取值不一致：" +
            $"Version={version}，AssemblyVersion={assemblyVersion}，FileVersion={fileVersion}");

        Assert.True(DateVersionRegex().IsMatch(version!),
            $"{VersionPropsFile} 的版本号 {version} 不是日期制（应为 yyyy.M.d，" +
            "与最新提交的代码日期同步）");
    }

    /// <summary>
    /// 版本号只允许在 Directory.Build.props 定义：任何 csproj 里再写 &lt;Version&gt;
    /// 会覆盖集中定义，导致程序集版本与产品版本不一致（ InformationalVersion 与
    /// AssemblyVersion 各走各的值），因此一旦出现即判违规。
    /// </summary>
    [Fact]
    public void Csproj_Files_Should_Not_Redeclare_Version()
    {
        List<string> offenders = new();

        foreach (string csproj in Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories))
        {
            // obj/bin 里的临时工程快照不属于源码
            string normalized = csproj.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.Ordinal) ||
                normalized.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            if (VersionPropertyRegex().IsMatch(File.ReadAllText(csproj)))
            {
                offenders.Add(Relative(csproj));
            }
        }

        Assert.True(offenders.Count == 0,
            "版本号只能在 Directory.Build.props 定义，以下 csproj 不得再声明 <Version>：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 版本显示代码必须读程序集版本，禁止把版本字符串写死在界面里：
    /// 版本号唯一落点是 Directory.Build.props，界面文字自动跟随。
    /// </summary>
    [Fact]
    public void Version_Text_Should_Read_Assembly_Version()
    {
        string layout = ReadRepoFile("DAP.Presentation.BlazorWeb.Client/Layout/MainLayout.razor");

        Assert.True(layout.Contains("GetName().Version", StringComparison.Ordinal),
            "MainLayout.razor 的 AppVersion 必须从程序集版本读取，不得硬编码版本字符串");

        string viewModel = ReadRepoFile(
            "DAP.Presentation.AvaloniaApp/ViewModels/MainWindowViewModel.cs");

        Assert.True(viewModel.Contains("GetName().Version", StringComparison.Ordinal),
            "MainWindowViewModel.VersionText 必须从程序集版本读取，不得硬编码版本字符串");
    }

    /// <summary>
    /// 从测试程序集向上定位仓库根（以 DAP.slnx 为标记）。
    /// </summary>
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DAP.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        string full = Path.Combine(RepoRoot(), relativePath);

        Assert.True(File.Exists(full), $"找不到文件：{relativePath}");
        return File.ReadAllText(full);
    }

    /// <summary>把绝对路径转为相对仓库根的展示路径。</summary>
    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepoRoot(), path);
    }

    /// <summary>匹配 csproj / props 里的 Version 属性（含属性值捕获）。</summary>
    [GeneratedRegex(@"<Version>([^<]+)</Version>")]

    private partial Regex VersionPropertyRegex();

    /// <summary>按属性名在 Directory.Build.props 里取唯一一处定义，重复或缺失都返回 null。</summary>
    private static string? SingleMatch(string text, string propertyName)
    {
        MatchCollection matches = Regex.Matches(text, @$"<{propertyName}>([^<]+)</{propertyName}>");

        return matches.Count == 1 ? matches[0].Groups[1].Value : null;
    }

    /// <summary>日期制版本号：yyyy.M.d（月与日允许 1~2 位，与 2026.11.26 同形）。</summary>
    [GeneratedRegex(@"^20\d{2}\.(?:1[0-2]|[1-9])\.(?:3[01]|[12]\d|[1-9])$")]
    private partial Regex DateVersionRegex();
}

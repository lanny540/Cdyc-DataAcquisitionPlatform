using System.Text.RegularExpressions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 设计规范守卫测试：确保 UI 代码遵守 docs/DESIGN_SYSTEM.md 的强制约束。
///
/// 这些约束若只写在文档里很容易被无意破坏，因此用测试固化：
/// 违反设计规范的代码会让构建失败，而不是等到评审时才发现。
/// </summary>
public partial class DesignSystemGuardTests
{
    /// <summary>
    /// 业务页面中禁止硬编码十六进制色值，必须走CSS 变量或 MudBlazor 语义色。
    ///
    /// 色板的唯一落点是 Layout/PlatformTheme.cs（.cs 文件，不在本守卫的扫描范围内），
    /// 因此这里不做文件豁免：只要 .razor 里出现色值就判违规。
    /// 其一致性由 AppCss_And_MudTheme_Should_Share_Same_Color 保证。
    /// </summary>
    [Fact]
    public void Razor_Pages_Should_Not_Hardcode_Hex_Colors()
    {
        List<string> offenders = new();

        foreach (string file in RazorFiles())
        {
            string[] lines = ReadLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!HexColorRegex().IsMatch(line))
                {
                    continue;
                }

                // 注释与文档行不算违规
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("<!--") || trimmed.StartsWith("@*") || trimmed.StartsWith("//"))
                {
                    continue;
                }

                offenders.Add($"{Relative(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "页面中禁止硬编码十六进制色值，请使用 CSS 变量或 MudBlazor 语义色：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Web 端色板必须与 MudTheme 保持同步，否则组件默认色会与自定义样式冲突。
    /// </summary>
    [Theory]
    [InlineData("#0b4fc4")]
    [InlineData("#33475c")]
    [InlineData("#7b2f9e")]
    [InlineData("#0d7a3c")]
    [InlineData("#b15600")]
    [InlineData("#b32318")]
    [InlineData("#0f6fbd")]
    public void AppCss_And_MudTheme_Should_Share_Same_Color(string hex)
    {
        string appCss = ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css");

        // 色板的 MudTheme 对接文件。原先写在 MainLayout.razor 里，
        // 认证页也要用同一份色板，已抽到 PlatformTheme.cs 供两个布局共用。
        string platformTheme = ReadRepoFile("DAP.Presentation.BlazorWeb.Client/Layout/PlatformTheme.cs");

        Assert.Contains(hex, appCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hex, platformTheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 桌面端色板必须与 Web 端一致：同一角色色在两个端使用相同 Base 值。
    /// </summary>
    [Theory]
    [InlineData("DapPrimary", "0B4FC4")]
    [InlineData("DapSecondary", "33475C")]
    [InlineData("DapTertiary", "7B2F9E")]
    [InlineData("DapSuccess", "0D7A3C")]
    [InlineData("DapWarning", "B15600")]
    [InlineData("DapError", "B32318")]
    [InlineData("DapInfo", "0F6FBD")]
    public void Avalonia_Palette_Should_Match_Web_Color(string resourceKey, string webHex)
    {
        string palette = ReadRepoFile("DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml");

        // 色值可能写在 Color 或 SolidColorBrush 上，两者都必须一致
        string normalized = palette.Replace(" ", string.Empty).ToUpperInvariant();
        Assert.Contains($"#{webHex}", normalized, StringComparison.Ordinal);
        Assert.Contains($"X:KEY=\"{resourceKey}\"", palette, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 色板可读性守卫。
    ///
    /// 之前出现过「整站看上去很素」的问题，根因不在颜色不够艳，
    /// 而是底色层与表面色的对比度只有 1.05~1.09 倍，肉眼几乎分辨不出，
    /// 于是所有卡片实际都呈现为同一个颜色。这里把底线固化为断言：
    ///
    /// 1. **白字在 base 上 ≥ 4.5:1**（WCAG AA 正文级）——主按钮、徽章才读得清；
    /// 2. **tint 与卡片表面 ≥ 1.20:1**——区块底纹必须真的能被看见，否则等于没上色；
    /// 3. **soft 与卡片表面 ≥ 1.25:1**——统计卡底色用 soft 档，比区块更跳脱，
    ///    若退回接近表面色，页面上最醒目的数字区域又会失去色彩。
    ///
    /// 参照表面从 app.css 的 <c>--platform-surface</c> 读取而不是硬编码白色：
    /// 全站已切换为深色主题（深色卡片表面 #17212c），tint/soft 是「比表面亮一档」的
    /// 深色容器，对比方向与浅色主题相反，但「底纹必须可辨」的底线不变。
    ///
    /// 同时校验 strong 色在各自底色上 ≥ 4.5:1，保证卡片里的数值不因底色调整而看不清。
    /// 深色主题下 strong 是亮色强调文字，浅色主题下是加深档——职责相同、方向相反。
    /// </summary>
    [Theory]
    [InlineData("primary")]
    [InlineData("secondary")]
    [InlineData("tertiary")]
    [InlineData("success")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("info")]
    public void Palette_Should_Meet_Contrast_Baselines(string role)
    {
        string appCss = ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css");

        string? baseColor = ReadCssVariable(appCss, $"--md-{role}");
        string? strongColor = ReadCssVariable(appCss, $"--md-{role}-strong");
        string? softColor = ReadCssVariable(appCss, $"--md-{role}-soft");
        string? tintColor = ReadCssVariable(appCss, $"--md-{role}-tint");
        string? surfaceColor = ReadCssVariable(appCss, "--platform-surface");

        Assert.NotNull(baseColor);
        Assert.NotNull(strongColor);
        Assert.NotNull(softColor);
        Assert.NotNull(tintColor);
        Assert.NotNull(surfaceColor);

        double whiteOnBase = ContrastRatio(baseColor!, "#ffffff");
        Assert.True(whiteOnBase >= 4.5,
            $"{role} 的 base 色 {baseColor} 上放白字对比度仅 {whiteOnBase:F2}:1，" +
            "低于 WCAG AA 的 4.5:1，主按钮与徽章文字会看不清。");

        double tintOnSurface = ContrastRatio(tintColor!, surfaceColor!);
        Assert.True(tintOnSurface >= 1.20,
            $"{role} 的 tint 色 {tintColor} 与卡片表面 {surfaceColor} 的对比度仅 {tintOnSurface:F2}:1，" +
            "底纹肉眼无法分辨，整站会显得没有颜色。");

        double softOnSurface = ContrastRatio(softColor!, surfaceColor!);
        Assert.True(softOnSurface >= 1.25,
            $"{role} 的 soft 色 {softColor} 与卡片表面 {surfaceColor} 的对比度仅 {softOnSurface:F2}:1，" +
            "统计卡底色会退化成表面色，页面最醒目的数字区域将失去色彩。");

        double strongOnTint = ContrastRatio(strongColor!, tintColor!);
        Assert.True(strongOnTint >= 4.5,
            $"{role} 的 strong 色 {strongColor} 在 tint 底 {tintColor} 上对比度仅 " +
            $"{strongOnTint:F2}:1，卡片数值会看不清。");

        double strongOnSoft = ContrastRatio(strongColor!, softColor!);
        Assert.True(strongOnSoft >= 4.5,
            $"{role} 的 strong 色 {strongColor} 在统计卡底色 {softColor} 上对比度仅 " +
            $"{strongOnSoft:F2}:1，卡片数值会看不清。");
    }

    /// <summary>
    /// 设计规范文档中的色板表必须与 app.css 的权威变量一致。
    ///
    /// 文档是给人看的，代码是给机器跑的——两者一旦漂移，
    /// 后来者按文档改代码就会改出错值，而且很难发现。
    /// </summary>
    [Fact]
    public void DesignSystemDoc_Should_Match_AppCss_Palette()
    {
        string doc = ReadRepoFile("docs/DESIGN_SYSTEM.md");
        string appCss = ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css");

        List<string> offenders = new();
        string[] roles = new[] { "primary", "secondary", "tertiary", "success", "warning", "error", "info" };

        foreach (string role in roles)
        foreach (string level in new[] { "base", "soft", "tint" })
        {
            string variable = level == "base" ? $"--md-{role}" : $"--md-{role}-{level}";

            string? actual = ReadCssVariable(appCss, variable);
            if (actual is null)
            {
                offenders.Add($"{variable}: app.css 中未定义");
                continue;
            }

            // 文档表格里该角色对应行中应出现这个色值
            if (!doc.Contains(actual, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(
                    $"{variable} = {actual} 在 docs/DESIGN_SYSTEM.md 的色板表中找不到，" +
                    "文档与代码已漂移");
            }
        }

        Assert.True(offenders.Count == 0,
            "设计规范文档的色板必须与 app.css 权威变量一致：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// AGENTS.md 的色表必须与 app.css 的权威变量一致。
    ///
    /// AGENTS.md 声明「与代码同等优先」，AI 与新人都会直接照它改代码；
    /// 而它不在 DESIGN_SYSTEM.md 的守卫覆盖范围内，色板加深后其 Base 色表
    /// 曾长期停留在旧值（primary 还是 #1A5FB4），照它取色就会改回旧色。
    /// </summary>
    [Fact]
    public void AgentsDoc_Should_Match_AppCss_Palette()
    {
        string agents = ReadRepoFile("AGENTS.md");
        string appCss = ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css");

        List<string> offenders = new();
        string[] roles = new[] { "primary", "secondary", "tertiary", "success", "warning", "error", "info" };

        foreach (string role in roles)
        {
            string? actual = ReadCssVariable(appCss, $"--md-{role}");
            if (actual is null)
            {
                offenders.Add($"--md-{role}: app.css 中未定义");
                continue;
            }

            if (!agents.Contains(actual, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(
                    $"--md-{role} = {actual} 在 AGENTS.md 的色表中找不到，" +
                    "请同步更新该表的 Base 色");
            }
        }

        Assert.True(offenders.Count == 0,
            "AGENTS.md 的色表必须与 app.css 权威变量一致：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 图表必须开启 MatchBoundsToSize 并配置 ChartOptions。
    ///
    /// **为什么强制**：MudChart 的 SVG 默认用固定 viewBox（700×350，比例 2:1）并按比例居中，
    /// 容器宽高比与之不同时，SVG 内部会留出大片空白（实测绘图区仅占 79% 宽度，
    /// 左右各空 64px），用户看到的图表「没充满卡片」。
    /// `MatchBoundsToSize="true"` 让 SVG 按容器尺寸生成 viewBox，绘图区可提升到 90% 以上。
    ///
    /// **ChartOptions 同样不能省**：默认样式下数据点没有标记、线条偏细、坐标轴无标题，
    /// 用户只能盯着细线猜数值。配置项统一放在 `PlatformChartDefaults`，
    /// 避免三处图表页各自裸用 MudChart。
    /// </summary>
    [Fact]
    public void Charts_Should_Fill_Container_And_Configure_Options()
    {
        List<string> offenders = new();

        foreach (string file in RazorFiles())
        {
            string text = ReadRepoFile(file);

            if (!text.Contains("<MudChart", StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.Contains("MatchBoundsToSize=\"true\"", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: MudChart 缺少 MatchBoundsToSize=\"true\"，" +
                              "图表无法铺满卡片，四周会留出大片空白");
            }

            if (!text.Contains("ChartOptions=", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: MudChart 缺少 ChartOptions，" +
                              "数据点标记、轴线、轴标题与悬停提示会全部退化为默认样式");
            }
        }

        Assert.True(offenders.Count == 0,
            "图表必须铺满容器并显式配置呈现选项：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 图表容器必须允许坐标轴文字溢出。
    ///
    /// MudChart 把 Y 轴刻度值以 text-anchor:end 绘制在 viewBox 边界上，
    /// X 轴标签与轴标题同样在边界之外。SVG 与容器默认都是 overflow:hidden，
    /// 这些文字会被整个裁掉——刻度值消失后用户无法判断数值量级。
    /// </summary>
    [Fact]
    public void Chart_Container_Should_Not_Clip_Axis_Labels()
    {
        string css = ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css");

        // 容器与 SVG 都要放开裁剪
        Assert.Contains(".platform-chart-panel", css, StringComparison.Ordinal);
        Assert.Contains(".platform-chart-frame .mud-chart svg", css, StringComparison.Ordinal);

        // 容器需为坐标轴文字留出空间（左侧刻度值 + 底部标签与轴标题）
        Match frameBlock = Regex.Match(css,
            @"\.platform-chart-frame\s*\{(?<body>[^}]*)\}",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.True(frameBlock.Success, "app.css 中缺少 .platform-chart-frame 规则");
        Assert.Contains("padding", frameBlock.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 兜底 catch 禁止把 ex.Message 拼进用户可见文案。
    ///
    /// **为什么强制**：网络类异常的原始消息是英文框架文案
    /// （如 "No such host is known. (host:443)"、"The operation was canceled."），
    /// 还可能顺带泄漏内网地址。正确做法是按异常类型分支取中文文案
    /// （Web/桌面端均有 UserFacingErrors 助手），原始细节只进日志。
    ///
    /// 例外：InvalidOperationException 的消息本身已是中文业务文案，
    /// UserFacingErrors 内部有放行；测试层面无法区分语义，
    /// 因此仅拦 `{ex.Message}` 插值与 `ErrorMessage = ex.Message` 赋值两种形态。
    /// </summary>
    [Fact]
    public void Catch_Blocks_Should_Not_Leak_Raw_Exception_Message()
    {
        List<string> offenders = new();
        // 待扫描的源码目录：Web 客户端、Web 服务端与端点、桌面端 ViewModels
        string[] scanDirs = new[]
        {
            "DAP.Presentation.BlazorWeb.Client", "DAP.Presentation.BlazorWeb/Services",
            "DAP.Presentation.BlazorWeb/Endpoints", "DAP.Presentation.AvaloniaApp/ViewModels"
        };

        foreach (string dir in scanDirs)
        {
            string fullDir = Path.Combine(RepoRoot(), dir);
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            IEnumerable<string> sourceFiles = Directory
                .EnumerateFiles(fullDir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(fullDir, "*.razor", SearchOption.AllDirectories));

            foreach (string file in sourceFiles)
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string[] lines = ReadLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///"))
                    {
                        continue;
                    }

                    if (trimmed.Contains("{ex.Message}", StringComparison.Ordinal)
                        || trimmed.Contains("ErrorMessage = ex.Message", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Relative(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "兜底 catch 禁止透传 ex.Message（英文框架文案与内网地址会直接暴露给用户），" +
            "请改用 UserFacingErrors.Describe(ex) 或按异常类型分支给中文结论：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 后台节点删除必须保留定义归档、采集点与历史记录，所有读查询统一排除归档行。
    /// </summary>
    [Fact]
    public void Managed_Data_Definition_Delete_Should_Preserve_History()
    {
        string service = ReadRepoFile(
            "DAP.Infrastructure/DataAccess/Services/DataAcquisitionPlatformService.cs");
        string configuration = ReadRepoFile(
            "DAP.Infrastructure/DataAccess/Persistence/Configurations/ManagedDataDefinitionConfiguration.cs");
        string sql = ReadRepoFile("DAP.Infrastructure/DataAccess/Queries/PlatformReadSql.cs");

        Assert.Contains("definition.DeletedAt = deletedAt;", service, StringComparison.Ordinal);
        Assert.Contains("DisableCollectionPointForManagedDefinitionAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveCollectionPointForManagedDefinitionAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("_managedDataDefinitionRepository.Remove", service, StringComparison.Ordinal);
        Assert.Contains("builder.HasQueryFilter(item => item.DeletedAt == null);", configuration,
            StringComparison.Ordinal);
        Assert.Contains(".HasFilter(\"deleted_at IS NULL\")", configuration, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("d.deleted_at IS NULL", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// 查看级排班与班组接口不得继承配置等级；配置与写端点的收窄必须保留。
    /// </summary>
    [Fact]
    public void ReadOnly_Scheduling_APIs_Should_Be_Separated_From_Configuration_APIs()
    {
        string shifts = ReadRepoFile("DAP.Presentation.BlazorWeb/Endpoints/ShiftSchedulingEndpoints.cs");
        string energy = ReadRepoFile("DAP.Presentation.BlazorWeb/Endpoints/EnergyMetricEndpoints.cs");

        Assert.Matches(
            "MapGroup\\(\"/api/shift-schedules\"\\)\\s*\\.WithTags\\(\"ShiftSchedules\"\\)\\s*" +
            "\\.RequireBanknotePaperDomain\\(\\)\\s*\\.RequireConfigureAccess\\(\\)",
            shifts);
        Assert.Matches(
            "MapGroup\\(\"/api/shift-schedule-view\"\\)\\s*\\.WithTags\\(\"ShiftScheduleView\"\\)\\s*" +
            "\\.RequireBanknotePaperDomain\\(\\)(?!\\s*\\.RequireConfigureAccess)",
            shifts);
        Assert.Contains(".RequirePermission(AuthorizationCatalog.ShiftManagePermission)", shifts,
            StringComparison.Ordinal);
        Assert.Contains("\"/teams\"", energy, StringComparison.Ordinal);
        Assert.DoesNotContain(".RequireConfigureAccess()", energy, StringComparison.Ordinal);
    }

    /// <summary>
    /// 桌面主壳必须同时受登录态和强制改密状态约束，避免半登录会话触发业务请求。
    /// </summary>
    [Fact]
    public void Desktop_Shell_Should_Block_Navigation_Until_Password_Change_Completes()
    {
        string viewModel = ReadRepoFile(
            "DAP.Presentation.AvaloniaApp/ViewModels/MainWindowViewModel.cs");
        string view = ReadRepoFile("DAP.Presentation.AvaloniaApp/Views/MainWindow.axaml");

        Assert.Contains(
            "public bool ShowShell => _session.IsAuthenticated && !_session.MustChangePassword;",
            viewModel,
            StringComparison.Ordinal);
        // 断言入口校验存在即可，不绑定大括号风格：全仓清理会把单行语句强制加大括号。
        Assert.Contains("if (!ShowShell)", viewModel, StringComparison.Ordinal);
        Assert.Contains("Login.NeedsPasswordChange = true;", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowShell}\"", view, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowLogin}\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVisible=\"{Binding Session.IsAuthenticated}\"", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 CSS 中读取变量值（形如 --md-primary: #0b4fc4;）。
    /// </summary>
    private static string? ReadCssVariable(string css, string variableName)
    {
        Match match = Regex.Match(css, $@"{Regex.Escape(variableName)}\s*:\s*(#[0-9a-fA-F]{{6}})",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 计算两个十六进制颜色之间的 WCAG 对比度。
    /// </summary>
    private static double ContrastRatio(string foreground, string background)
    {
        double a = RelativeLuminance(foreground);
        double b = RelativeLuminance(background);

        double lighter = Math.Max(a, b);
        double darker = Math.Min(a, b);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        string value = hex.TrimStart('#');
        double r = Convert.ToInt32(value[..2], 16) / 255.0;
        double g = Convert.ToInt32(value.Substring(2, 2), 16) / 255.0;
        double b = Convert.ToInt32(value.Substring(4, 2), 16) / 255.0;

        return (0.2126 * ToLinear(r)) + (0.7152 * ToLinear(g)) + (0.0722 * ToLinear(b));
    }

    private static double ToLinear(double channel)
    {
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// 桌面端必须引入品牌色覆盖，否则会继续使用 Semi 默认亮蓝 #0064fa。
    /// 校验只针对实际的资源定义行（Brush / Color），注释中的说明文字不算。
    /// </summary>
    [Fact]
    public void Avalonia_Should_Load_Brand_Palette_And_Semi_Override()
    {
        string app = ReadRepoFile("DAP.Presentation.AvaloniaApp/App.axaml");

        Assert.Contains("DapPalette.axaml", app, StringComparison.Ordinal);
        Assert.Contains("SemiBrandOverride.axaml", app, StringComparison.Ordinal);

        // Semi 默认主色为 #0064fa，覆盖层中不允许再定义该色值
        string semiDefaultPrimary = "#0064FA";
        List<string> offenders = AvaloniaXamlFiles()
            .Select(ReadRepoFile)
            .SelectMany(SelectResourceDefinitionLines)
            .Where(line => line.Contains(semiDefaultPrimary, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            "覆盖层不得保留 Semi 默认主色 #0064fa：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Axaml 文件的 XML 注释中不得出现连续两个连字符，会导致 AVLN1001 解析失败。
    /// 仅检查注释内部内容（不含起始标记 &lt;!-- 与结束标记 --&gt;）。
    /// </summary>
    [Fact]
    public void Avalonia_Xaml_Comments_Should_Not_Contain_Double_Hyphen()
    {
        List<string> offenders = new();

        foreach (string file in AvaloniaXamlFiles())
        {
            string text = ReadRepoFile(file);
            foreach (Match match in XmlCommentRegex().Matches(text))
            {
                // 去掉 <!-- 与 --> 后检查剩余正文
                string body = match.Value;
                if (body.StartsWith("<!--", StringComparison.Ordinal))
                {
                    body = body[4..];
                }

                if (body.EndsWith("-->", StringComparison.Ordinal))
                {
                    body = body[..^3];
                }

                if (body.Contains("--", StringComparison.Ordinal))
                {
                    int line = text[..match.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{file}:{line}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Axaml 注释内不得出现连续两个连字符（--），会导致 AVLN1001 解析失败：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 页面根容器统一为 platform-page-shell，保证全站间距体系一致。
    ///
    /// 例外是登录与改密页：它们走 AuthLayout（platform-auth-shell），
    /// 没有顶栏与抽屉，套用业务页外壳反而会多出一层留白。
    /// 判断依据是页面自己声明了 @layout AuthLayout，不是按文件名逐个豁免。
    /// </summary>
    [Fact]
    public void Pages_Should_Use_Platform_Page_Shell_Root()
    {
        List<string> offenders = new();

        foreach (string file in RazorFiles())
        {
            string text = ReadRepoFile(file);
            if (!text.Contains("@page", StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Contains("@layout AuthLayout", StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.Contains("platform-page-shell", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        Assert.True(offenders.Count == 0,
            "页面根容器应为 <MudStack Class=\"platform-page-shell\" Spacing=\"2\">：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 统计卡片应使用 accent 修饰类，避免各卡片配色脱离色板体系。
    ///
    /// accent 允许写在插值表达式里（如 Class="@($"... {MetricAccents[i]} ...")"），
    /// 因为指标卡片的取色本来就在代码后置中按索引决定，但结果必须仍然来自
    /// MetricAccents 之类的白名单常量，不能是任意的具体色相。
    /// </summary>
    [Fact]
    public void Stat_Cards_Should_Declare_Accent_Class()
    {
        List<string> offenders = new();
        Regex accentRegex = new(@"platform-accent-(\w+)");

        foreach (string file in RazorFiles())
        {
            string[] lines = ReadLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.Contains("platform-stat-card", StringComparison.Ordinal))
                {
                    continue;
                }

                if (accentRegex.IsMatch(line))
                {
                    continue;
                }

                // 指标卡片通过插值注入 accent，要求代码后置中确有对应的白名单常量声明。
                if (line.Contains("MetricAccents[", StringComparison.Ordinal) &&
                    HasAccentAllowListConstant(file))
                {
                    continue;
                }

                offenders.Add($"{Relative(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "统计卡片必须声明 platform-accent-* 修饰类：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 判断页面的代码后置文件是否声明了 accent 白名单数组常量。
    /// </summary>
    private static bool HasAccentAllowListConstant(string razorFile)
    {
        string codeBehind = Path.ChangeExtension(razorFile, ".razor.cs");
        if (!File.Exists(codeBehind))
        {
            return false;
        }

        string text = ReadRepoFile(Path.GetRelativePath(RepoRoot(), codeBehind));
        return text.Contains("MetricAccents", StringComparison.Ordinal) &&
               text.Contains("platform-accent-", StringComparison.Ordinal);
    }

    /// <summary>
    /// 三级导航目录中的每个条目都必须指向真实存在的页面。
    ///
    /// **为什么强制**：顶栏与左侧菜单共用 NavigationCatalog，新增菜单项时若把路由写错
    /// （拼写、少写、或被删页面的残留），编译完全通过，只有点到菜单才会 404。
    /// 这里把「菜单 → @page 路由」的对应关系固化为断言。
    /// </summary>
    [Fact]
    public void Navigation_Links_Should_Point_To_Existing_Pages()
    {
        string catalog = ReadRepoFile("DAP.Presentation.BlazorWeb.Client/Layout/NavigationCatalog.cs");

        // 页面路由：去掉 {Id:guid} 之类的模板参数后取前缀，用于与菜单 href 比较。
        HashSet<string> routes = RazorFiles()
            .Select(ReadRepoFile)
            .SelectMany(text =>
                Regex.Matches(text, "@page\\s+\"(?<route>[^\"]+)\"").Select(m => m.Groups["route"].Value))
            .Select(NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        MatchCollection items = Regex.Matches(catalog,
            "new NavItem\\(\\s*\"(?<title>[^\"]*)\"\\s*,\\s*\"(?<href>[^\"]*)\"");
        Assert.True(items.Count >= 8, $"导航条目仅解析出 {items.Count} 条，NavigationCatalog 的结构可能已变更");

        List<string> offenders = new();
        HashSet<string> seenHrefs = new(StringComparer.OrdinalIgnoreCase);

        foreach (Match item in items)
        {
            string title = item.Groups["title"].Value;
            string href = item.Groups["href"].Value;
            string normalized = NormalizeRoute(href);

            if (!routes.Contains(normalized))
            {
                offenders.Add($"{title} → {href}：找不到对应的 @page 路由");
            }

            if (!seenHrefs.Add(normalized))
            {
                offenders.Add($"{title} → {href}：同一路由在导航中重复出现");
            }
        }

        Assert.True(offenders.Count == 0,
            "导航条目必须指向存在且唯一的页面：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 顶级模块必须唯一且非空：顶栏按 Id 高亮，Id 重复会让两个模块同时选中。
    /// </summary>
    [Fact]
    public void Navigation_Sections_Should_Have_Unique_Ids_And_Items()
    {
        string catalog = ReadRepoFile("DAP.Presentation.BlazorWeb.Client/Layout/NavigationCatalog.cs");

        MatchCollection sections = Regex.Matches(catalog,
            "new\\(\\s*\\n\\s*(?<ref>\\w+),\\s*\\n\\s*\"(?<title>[^\"]*)\"");

        List<string> ids = new();
        List<string> offenders = new();

        foreach (Match section in sections)
        {
            string reference = section.Groups["ref"].Value;
            string title = section.Groups["title"].Value;

            Match declaration = Regex.Match(catalog,
                $"public const string {Regex.Escape(reference)}\\s*=\\s*\"(?<id>[^\"]+)\"");

            if (!declaration.Success)
            {
                offenders.Add($"{title}：Id 常量 {reference} 未找到");
                continue;
            }

            ids.Add(declaration.Groups["id"].Value);
        }

        Assert.True(ids.Count >= 3, $"仅解析出 {ids.Count} 个顶级模块，导航结构可能已变更");
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 归一化路由：去掉查询串、模板参数与首尾斜杠，根路由归一为空串。
    /// </summary>
    private static string NormalizeRoute(string route)
    {
        string value = route.Trim();

        int templateIndex = value.IndexOf('{');
        if (templateIndex >= 0)
        {
            value = value[..templateIndex];
        }

        int queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            value = value[..queryIndex];
        }

        return value.Trim('/');
    }

    // ---------- 辅助方法 ----------
    /// <summary>
    /// MudChart 的系列与 X 轴标签必须来自同一个数据源方法。
    ///
    /// MudChart 的语义是「系列 × X 轴标签」：每条系列是一组柱子/折线，
    /// X 轴标签决定横向位置。若标签与数据取自不同来源，
    /// 图表会退化成只剩一个标签、多个系列挤在同一位置，界面看不出任何对比关系。
    ///
    /// 这类错误编译能通过、单测也测不出，只有渲染后才暴露。
    /// 此处只校验「标签方法名与系列方法名一致」这一强约束——
    /// 同一方法同时产出数据与标签，结构上就不可能错位。
    /// </summary>
    [Fact]
    public void MudChart_Series_And_Labels_Should_Share_Same_Source()
    {
        List<string> offenders = new();

        foreach (string file in RazorFiles())
        {
            string text = ReadRepoFile(file);
            string codeBehind = Path.ChangeExtension(file, ".razor.cs");
            if (!File.Exists(codeBehind))
            {
                continue;
            }

            string code = ReadRepoFile(Path.GetRelativePath(RepoRoot(), codeBehind));

            // 匹配同一组 MudChart 上的 ChartSeries 与 ChartLabels 取值
            foreach (Match match in ChartBindingRegex().Matches(text))
            {
                string seriesExpr = match.Groups["series"].Value;
                string labelsExpr = match.Groups["labels"].Value;

                string seriesMethod = MethodNameOf($"@{seriesExpr}(");
                string labelsMethod = MethodNameOf($"@{labelsExpr}(");

                if (seriesMethod.Length == 0 || labelsMethod.Length == 0)
                {
                    offenders.Add($"{Relative(file)}: ChartSeries/ChartLabels 未使用方法调用：" +
                                  $"{seriesExpr} / {labelsExpr}");
                    continue;
                }

                if (seriesMethod == labelsMethod)
                {
                    continue;
                }

                // 允许「两者都接受同一入参」的写法，此时数据与标签天然对齐；
                // 其余情况要求标签方法存在且被系列方法内部使用（即同源实现）。
                if (!IsLabelsAlignedWithSeries(code, seriesMethod, labelsMethod))
                {
                    offenders.Add(
                        $"{Relative(file)}: 图表系列来自 {seriesMethod}()，标签来自 {labelsMethod}()，二者不同源。" +
                        "MudChart 按「系列 × 标签」定位，标签错位会让所有柱子挤在同一位置。");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "MudChart 的系列与 X 轴标签必须同源，否则图表无法正确对齐：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 判断标签方法是否与系列方法同源：两者必须引用同一个数据来源。
    ///
    /// 合法写法有两种：
    /// 1. 同一方法既产出数据又产出标签（折线图常用）；
    /// 2. 两个方法读取同一个集合（横向对比：系列按班组取值、标签取班组名）。
    /// </summary>
    private static bool IsLabelsAlignedWithSeries(string code, string seriesMethod, string labelsMethod)
    {
        if (!Regex.IsMatch(code, $@"string\[\]\s+{Regex.Escape(labelsMethod)}\s*\("))
        {
            return false;
        }

        string? seriesBody = ExtractMethodBody(code, seriesMethod);
        string? labelsBody = ExtractMethodBody(code, labelsMethod);

        if (seriesBody is null || labelsBody is null)
        {
            return false;
        }

        // 两者引用了同一个集合属性，说明数据与标签取自同一来源
        List<string> seriesSources = CollectionReferences(seriesBody);
        List<string> labelsSources = CollectionReferences(labelsBody);

        return seriesSources.Any(source => labelsSources.Contains(source, StringComparer.Ordinal));
    }

    private static string? ExtractMethodBody(string code, string methodName)
    {
        // 匹配方法签名后第一个 { 到配对 } 之间的内容（支持嵌套花括号）
        Match signature = Regex.Match(code, $@"\b{Regex.Escape(methodName)}\s*\([^)]*\)");
        if (!signature.Success)
        {
            return null;
        }

        int start = code.IndexOf('{', signature.Index + signature.Length);
        if (start < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int i = start; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return code[(start + 1)..i];
                }
            }
        }

        return code[(start + 1)..];
    }

    /// <summary>
    /// 提取方法体内引用的集合属性名（如 Teams、points、Trends）。
    /// </summary>
    private static List<string> CollectionReferences(string body)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(body, @"\b([A-Z]\w*)\b"))
        {
            names.Add(match.Groups[1].Value);
        }

        return names.ToList();
    }

    /// <summary>
    /// 从表达式中取出方法名，例如 "@BuildTeamSeries(metric.MetricCode)" → "BuildTeamSeries"。
    /// </summary>
    private static string MethodNameOf(string expression)
    {
        Match match = Regex.Match(expression, @"@(\w+)\s*\(");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(
        @"ChartSeries\s*=\s*""@(?<series>\w+)\([^""]*\)[^""]*""[\s\S]{0,300}?ChartLabels\s*=\s*""@(?<labels>\w+)\([^""]*\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChartBindingRegex();

    /// <summary>
    /// 提取 Axaml 中的资源定义行（Brush / Color 元素），剔除注释与普通文本行。
    /// </summary>
    private static IEnumerable<string> SelectResourceDefinitionLines(string axamlText)
    {
        bool inComment = false;
        foreach (string raw in axamlText.Split('\n'))
        {
            string line = raw.Trim();

            if (inComment)
            {
                if (line.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = false;
                }

                continue;
            }

            if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                if (!line.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = true;
                }

                continue;
            }

            if (line.StartsWith("<!--", StringComparison.Ordinal) || line.Length == 0)
            {
                continue;
            }

            if (line.Contains("Brush", StringComparison.Ordinal) || line.Contains("<Color", StringComparison.Ordinal))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// accent 类白名单：新增色相前必须先更新 docs/DESIGN_SYSTEM.md 与 app.css。
    /// </summary>
    private static readonly string[] AllowedAccents =
    [
        "primary", "secondary", "tertiary",
        "success", "warning", "error", "info", "neutral"
    ];

    /// <summary>
    /// 校验 accent 类名是否都在白名单内，防止出现第 8 种色相。
    /// </summary>
    [Fact]
    public void Accent_Classes_Should_Stay_Within_Whitelist()
    {
        List<string> defined = Regex.Matches(ReadRepoFile("DAP.Presentation.BlazorWeb/wwwroot/app.css"),
                @"\.platform-accent-(\w+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        List<string> unexpected = defined.Except(AllowedAccents, StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            $"app.css 中出现白名单外的 accent 类：{string.Join(", ", unexpected)}。" +
            "新增色相前必须先更新 docs/DESIGN_SYSTEM.md 与 AGENTS.md 中的色表。");
    }

    /// <summary>
    /// 共享 API 客户端必须落在 DAP.Clients，两端共同引用。
    ///
    /// 曾经的形态是 Web.Client 自己持有 IPlatformApiClient，桌面端要用就得反向引用
    /// 展示层项目（还会把 WASM 依赖带进来）。契约与 HTTP 实现属于「基础设施」，
    /// 与任何一端 UI 无关，因此单独成项目：谁都不许再回到旧位置。
    /// </summary>
    [Fact]
    public void Shared_Api_Client_Should_Live_In_Dap_Clients_Project()
    {
        string contracts = ReadRepoFile("DAP.Clients/IPlatformApiClient.cs");
        Assert.Contains("namespace DAP.Clients;", contracts, StringComparison.Ordinal);

        // 三块内容都在共享项目内：业务契约、HTTP 实现、认证契约
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "DAP.Clients/PlatformApiClient.cs")),
            "缺少 DAP.Clients/PlatformApiClient.cs");
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "DAP.Clients/IPlatformAuthClient.cs")),
            "缺少 DAP.Clients/IPlatformAuthClient.cs");

        // 旧位置不得复活
        Assert.False(File.Exists(Path.Combine(RepoRoot(),
                "DAP.Presentation.BlazorWeb.Client/Services/IPlatformApiClient.cs")),
            "IPlatformApiClient 已迁到 DAP.Clients，不得在 Web.Client 中重新定义");
        Assert.False(File.Exists(Path.Combine(RepoRoot(),
                "DAP.Presentation.AvaloniaApp/Services/PlatformApiClient.cs")),
            "桌面端不得再维护一份 PlatformApiClient 副本");

        // 两端都必须引用共享项目
        foreach (string project in new[]
                 {
                     "DAP.Presentation.BlazorWeb.Client/DAP.Presentation.BlazorWeb.Client.csproj",
                     "DAP.Presentation.AvaloniaApp/DAP.Presentation.AvaloniaApp.csproj"
                 })
        {
            string csproj = ReadRepoFile(project);
            Assert.Contains("DAP.Clients.csproj", csproj, StringComparison.Ordinal);
        }

        // 共享项目不得反向依赖展示层，也不得引用服务端实现
        string clientCsproj = ReadRepoFile("DAP.Clients/DAP.Clients.csproj");
        Assert.DoesNotContain("DAP.Presentation", clientCsproj, StringComparison.Ordinal);
        Assert.DoesNotContain("DAP.Core.Infrastructure", clientCsproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// 桌面端 XAML 中禁止硬编码十六进制色值，颜色只能来自 DapPalette.axaml。
    ///
    /// 两个豁免文件是色板本身与 Semi 品牌色覆盖层：它们就是「色值的唯一落点」，
    /// 且已被 Avalonia_Palette_Should_Match_Web_Color 校验与 Web 一致。
    /// </summary>
    [Fact]
    public void Avalonia_Styles_Should_Not_Hardcode_Hex_Colors()
    {
        string[] allowList = new[]
        {
            Path.Combine("DAP.Presentation.AvaloniaApp", "Themes", "DapPalette.axaml"),
            Path.Combine("DAP.Presentation.AvaloniaApp", "Themes", "SemiBrandOverride.axaml")
        };

        List<string> offenders = new();

        foreach (string file in AvaloniaXamlFiles())
        {
            string relative = Relative(file);
            if (allowList.Contains(relative))
            {
                continue;
            }

            string[] lines = ReadLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!HexColorRegex().IsMatch(line))
                {
                    continue;
                }

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{relative}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "桌面端样式禁止硬编码十六进制色值，请引用 DapPalette 资源里的 DynamicResource：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 桌面端每个页面视图模型都必须登记在导航目录里。
    ///
    /// 与 Web 端 Navigation_Links_Should_Point_To_Existing_Pages 对称：
    /// Web 端是「链接必须指向真实页面」，桌面端是「页面必须能被导航到」，
    /// 否则写完页面却忘了挂菜单，功能等于没上线。
    /// </summary>
    [Fact]
    public void Avalonia_Page_ViewModels_Should_Be_Registered_In_Navigation_Catalog()
    {
        string catalog = ReadRepoFile("DAP.Presentation.AvaloniaApp/Navigation/DesktopNavigationCatalog.cs");
        List<string> offenders = new();

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(AvaloniaAppDir(), "ViewModels", "Pages"), "*.cs"))
        foreach (Match match in PageViewModelRegex().Matches(ReadRepoFile(file)))
        {
            string name = match.Groups[1].Value;
            if (!catalog.Contains($"typeof({name})", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: {name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "页面视图模型未登记到 DesktopNavigationCatalog，界面上将无法进入该页面：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 桌面端页面视图必须与其视图模型同名配对，且 x:DataType 指向正确的类型。
    /// 命名对不上时 Avalonia 不会报错，只是运行期绑定全部失效（整页空白）。
    /// </summary>
    [Fact]
    public void Avalonia_Page_Views_Should_Pair_With_ViewModels()
    {
        string viewDir = Path.Combine(AvaloniaAppDir(), "Views", "Pages");
        string vmDir = Path.Combine(AvaloniaAppDir(), "ViewModels", "Pages");
        List<string> offenders = new();

        foreach (string file in Directory.EnumerateFiles(viewDir, "*PageView.axaml"))
        {
            string viewName = Path.GetFileNameWithoutExtension(file);
            // ServerStatusPageView.axaml -> ServerStatusPageViewModel
            string expectedVm = viewName[..^"View".Length] + "ViewModel";
            string axaml = ReadRepoFile(Relative(file));

            if (!axaml.Contains($"x:DataType=\"vm:{expectedVm}\"", StringComparison.Ordinal))
            {
                offenders.Add($"{Relative(file)}: 缺少 x:DataType=\"vm:{expectedVm}\"");
            }

            if (!File.Exists(Path.Combine(vmDir, $"{expectedVm}.cs")))
            {
                offenders.Add($"{Relative(file)}: 找不到视图模型 {expectedVm}.cs");
            }
        }

        Assert.True(offenders.Count == 0,
            "页面视图与视图模型必须同名配对：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> RazorFiles()
    {
        return Directory.EnumerateFiles(RazorClientDir(), "*.razor", SearchOption.AllDirectories);
    }

    private static string AvaloniaAppDir()
    {
        return Path.Combine(RepoRoot(), "DAP.Presentation.AvaloniaApp");
    }

    private static IEnumerable<string> AvaloniaXamlFiles()
    {
        return Directory.EnumerateFiles(RepoRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));
    }

    private static string RazorClientDir()
    {
        return Path.Combine(RepoRoot(), "DAP.Presentation.BlazorWeb.Client");
    }

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

    private static string[] ReadLines(string path)
    {
        return File.ReadAllLines(path);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string full = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(full), $"找不到文件：{relativePath}");
        return File.ReadAllText(full);
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepoRoot(), path);
    }

    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlCommentRegex();

    /// <summary>
    /// 匹配页面视图模型类名。结尾的 \b 让抽象基类 EnergyPageViewModelBase 不会被算作页面。
    /// </summary>
    [GeneratedRegex(@"class\s+(\w+PageViewModel)\b")]
    private static partial Regex PageViewModelRegex();

    /// <summary>
    /// 保留给后续扩展：accent 白名单校验。
    /// </summary>
    public static TheoryData<string> AllowedAccentNames()
    {
        TheoryData<string> data = new();
        foreach (string name in AllowedAccents)
        {
            data.Add(name);
        }

        return data;
    }
}

using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Layout;

/// <summary>
/// 平台统一色板（Material Design 3）。
///
/// 此处色值须与 app.css 的 --md-* 变量保持同步，改动规则见 docs/DESIGN_SYSTEM.md。
/// 登录页与主框架共用同一份主题，避免未登录时看到另一套配色。
///
/// 全站以深色为主题：MudThemeProvider 传 IsDarkMode="true"，取 <see cref="Dark"/>。
/// 浅色面板仍然保留——MudBlazor 组件在个别场景（打印、第三方便利贴）会读浅色值，
/// 且将来若提供主题切换，这里就是现成的浅色档。
/// 七色 base 是全站唯一色相锚点，浅色与深色共用；深浅差异只体现在
/// 中性表面与 accent 的 soft/tint/strong 三档（深色容器 + 亮色强调文字）。
/// </summary>
public static class PlatformTheme
{
    /// <summary>
    /// 构造平台主题。<see cref="MudTheme"/> 是可变对象，共享同一实例会被 MudThemeProvider
    /// 反复赋值，因此每次取用都返回新实例。
    /// </summary>
    public static MudTheme Create()
    {
        return new MudTheme
        {
            PaletteLight = Light(),
            PaletteDark = Dark(),
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "4px",
                AppbarHeight = "56px",
                DrawerWidthLeft = "232px"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["PingFang SC", "Microsoft YaHei", "sans-serif"],
                    FontSize = "0.875rem",
                    LineHeight = "1.5"
                },
                H3 = new H3Typography {FontSize = "2rem", FontWeight = "600", LineHeight = "1.25"},
                H4 = new H4Typography {FontSize = "1.375rem", FontWeight = "600", LineHeight = "1.4"},
                H5 = new H5Typography {FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.4"},
                H6 = new H6Typography {FontSize = "1rem", FontWeight = "600", LineHeight = "1.5"},
                Body1 = new Body1Typography {FontSize = "0.875rem", LineHeight = "1.5"},
                Body2 = new Body2Typography {FontSize = "0.8125rem", LineHeight = "1.5"},
                Button = new ButtonTypography {FontSize = "0.8125rem", FontWeight = "500", TextTransform = "none"}
            }
        };
    }

    /// <summary>
    /// 浅色面板。中性面与 accent 三档维持原浅色值，深浅两套共用同一份品牌 base。
    /// </summary>
    private static PaletteLight Light()
    {
        PaletteLight palette = new();

        ApplyBrand(palette);

        palette.Background = "#f4f7fa";
        palette.BackgroundGray = "#eef2f7";
        palette.Surface = "#ffffff";
        palette.AppbarBackground = "#ffffff";
        palette.AppbarText = "#16202b";
        palette.DrawerBackground = "#ffffff";
        palette.DrawerIcon = "#4a5c70";
        palette.DrawerText = "#16202b";
        palette.TextPrimary = "#16202b";
        palette.TextSecondary = "#4e5a70";
        palette.TextDisabled = "#9aa6b6";
        palette.ActionDefault = "#16202b";
        palette.Divider = "#e1e7ee";
        palette.DividerLight = "#eef2f7";
        palette.LinesDefault = "#e1e7ee";
        palette.LinesInputs = "#c7d0da";
        palette.TableLines = "#e1e7ee";
        palette.TableHover = "#dce8fa";
        palette.TableStriped = "#f8fafc";
        palette.Skeleton = "#e7ecf3";

        return palette;
    }

    /// <summary>
    /// 深色面板：整体采用深色背景（Material Design 3 dark 规范）。
    ///
    /// 层级遵循「越浮越亮」：页面底 → 卡片表面 → 区块容器逐级提亮，
    /// 与 M3 dark 的 elevation 惯例一致。accent 的 soft/tint 档在深色下是
    /// 「深色容器」（带色相的暗底），strong 档改为亮色文字——深浅两套里
    /// strong 的职责都是「在容器底上最清晰的强调色」，方向相反、语义相同。
    /// 各色值均已核验：白字在 base 上 ≥4.5:1，strong 在 soft/tint 容器上 ≥4.5:1，
    /// soft/tint 与深色表面的对比 ≥1.20:1（守卫测试强制）。
    /// </summary>
    private static PaletteDark Dark()
    {
        PaletteDark palette = new();

        ApplyBrand(palette);

        palette.Background = "#0e151d";
        palette.BackgroundGray = "#101823";
        palette.Surface = "#17212c";
        palette.AppbarBackground = "#17212c";
        palette.AppbarText = "#e6edf5";
        palette.DrawerBackground = "#17212c";
        palette.DrawerIcon = "#a7b6c8";
        palette.DrawerText = "#e6edf5";
        palette.TextPrimary = "#e6edf5";
        palette.TextSecondary = "#a7b6c8";
        palette.TextDisabled = "#5f7085";
        palette.ActionDefault = "#e6edf5";
        palette.Divider = "#2b3a4b";
        palette.DividerLight = "#223042";
        palette.LinesDefault = "#2b3a4b";
        palette.LinesInputs = "#42576e";
        palette.TableLines = "#2b3a4b";
        palette.TableHover = "#1f2e42";
        palette.TableStriped = "#1a2530";
        palette.Skeleton = "#223042";

        return palette;
    }

    /// <summary>
    /// 品牌七色：全站唯一色相锚点，浅色与深色共用同一组 base。
    /// base 同时承担主按钮背景（配白字 ≥4.5:1），因此深色主题下也不提亮。
    /// </summary>
    private static void ApplyBrand(Palette palette)
    {
        palette.Primary = "#0b4fc4";
        palette.PrimaryContrastText = "#ffffff";
        palette.Secondary = "#33475c";
        palette.SecondaryContrastText = "#ffffff";
        palette.Tertiary = "#7b2f9e";
        palette.TertiaryContrastText = "#ffffff";
        palette.Info = "#0f6fbd";
        palette.InfoContrastText = "#ffffff";
        palette.Success = "#0d7a3c";
        palette.SuccessContrastText = "#ffffff";
        palette.Warning = "#b15600";
        palette.WarningContrastText = "#ffffff";
        palette.Error = "#b32318";
        palette.ErrorContrastText = "#ffffff";
    }
}
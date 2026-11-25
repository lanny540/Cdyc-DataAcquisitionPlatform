using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Layout;

/// <summary>
/// 平台统一色板（Material Design 2，浅色品牌红 + 琥珀配色，示意稿定稿）。
///
/// 此处色值须与 app.css 的 --md-* 变量保持同步，改动规则见 docs/DESIGN_SYSTEM.md。
/// 登录页与主框架共用同一份主题，避免未登录时看到另一套配色。
///
/// 全站固定浅色主题：MudThemeProvider 传 IsDarkMode="false"，取 <see cref="Light"/>。
/// 七色 base 是全站唯一色相锚点，每色配一档对比文字（-on）。
///
/// 色彩分工：primary 是红 #d32f2f（Red 700），承担顶栏与一切「操作」——主按钮、开关、选中、
/// 输入焦点、图表主系列；tertiary 是强调琥珀 #ffb300，只作点缀（填充底配深褐字）；error 固定深酒红。
/// 各色均为深色，对比文字一律白字。
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
            LayoutProperties =
                new LayoutProperties
                {
                    DefaultBorderRadius = "4px", AppbarHeight = "56px", DrawerWidthLeft = "232px"
                },
            Typography = new Typography
            {
                Default =
                    new DefaultTypography
                    {
                        FontFamily = ["PingFang SC", "Microsoft YaHei", "sans-serif"],
                        FontSize = "0.875rem",
                        LineHeight = "1.5"
                    },
                H3 = new H3Typography { FontSize = "2rem", FontWeight = "600", LineHeight = "1.25" },
                H4 = new H4Typography { FontSize = "1.375rem", FontWeight = "600", LineHeight = "1.4" },
                H5 = new H5Typography { FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.4" },
                H6 = new H6Typography { FontSize = "1rem", FontWeight = "600", LineHeight = "1.5" },
                Body1 = new Body1Typography { FontSize = "0.875rem", LineHeight = "1.5" },
                Body2 = new Body2Typography { FontSize = "0.8125rem", LineHeight = "1.5" },
                Button = new ButtonTypography
                {
                    FontSize = "0.8125rem", FontWeight = "500", TextTransform = "none"
                }
            }
        };
    }

    /// <summary>
    /// 浅色面板：中性面与 app.css 的 --platform-* 变量一一对应（黑色透明度阶 + 中性灰页底）。
    /// 输入框描边取深一档（对白面 ≥3:1），表格行悬停取页面底色浅灰。
    /// </summary>
    private static PaletteLight Light()
    {
        PaletteLight palette = new();

        ApplyBrand(palette);

        palette.Background = "#fafafa";
        palette.BackgroundGray = "#eeeeee";
        palette.Surface = "#ffffff";
        palette.AppbarBackground = "#d32f2f";
        palette.AppbarText = "#ffffff";
        palette.DrawerBackground = "#ffffff";
        palette.DrawerIcon = "#4f4f4f";
        palette.DrawerText = "#212121";
        palette.TextPrimary = "#212121";
        palette.TextSecondary = "#4f4f4f";
        palette.TextDisabled = "#9e9e9e";
        palette.ActionDefault = "#212121";
        palette.ActionDisabled = "#bdbdbd";
        palette.Divider = "#e0e0e0";
        palette.DividerLight = "#eeeeee";
        palette.LinesDefault = "#e0e0e0";
        palette.LinesInputs = "#8c8c8c";
        palette.TableLines = "#eeeeee";
        palette.TableHover = "#fafafa";
        palette.TableStriped = "#fafafa";
        palette.Skeleton = "#eeeeee";

        return palette;
    }

    /// <summary>
    /// 品牌七色（MD2 色阶）：全站唯一色相锚点，与 app.css 的 --md-* base / -on 变量同值。
    /// 对比文字是「填充底上的文字」：主色红与各语义色均为深色配白字；
    /// 强调琥珀偏亮，填充底配深褐字（TertiaryContrastText）。
    /// </summary>
    private static void ApplyBrand(Palette palette)
    {
        palette.Primary = "#d32f2f";
        palette.PrimaryContrastText = "#ffffff";
        palette.Secondary = "#455a64";
        palette.SecondaryContrastText = "#ffffff";
        palette.Tertiary = "#ffb300";
        palette.TertiaryContrastText = "#3e2a00";
        palette.Info = "#1976d2";
        palette.InfoContrastText = "#ffffff";
        palette.Success = "#2e7d32";
        palette.SuccessContrastText = "#ffffff";
        palette.Warning = "#b15600";
        palette.WarningContrastText = "#ffffff";
        palette.Error = "#8e1b1e";
        palette.ErrorContrastText = "#ffffff";
    }
}

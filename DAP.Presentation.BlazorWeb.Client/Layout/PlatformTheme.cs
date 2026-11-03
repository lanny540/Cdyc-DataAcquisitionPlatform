using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Layout;

/// <summary>
/// 平台统一色板（Material Design 3）。
///
/// 此处色值须与 app.css 的 --md-* 变量保持同步，改动规则见 docs/DESIGN_SYSTEM.md。
/// 登录页与主框架共用同一份主题，避免未登录时看到另一套配色。
/// </summary>
public static class PlatformTheme
{
    /// <summary>
    /// 浅色主题。<see cref="MudTheme"/> 是可变对象，共享同一实例会被 MudThemeProvider 反复赋值，
    /// 因此每次取用都返回新实例。
    /// </summary>
    public static MudTheme Create()
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#0b4fc4",
                PrimaryContrastText = "#ffffff",
                Secondary = "#33475c",
                SecondaryContrastText = "#ffffff",
                Tertiary = "#7b2f9e",
                TertiaryContrastText = "#ffffff",
                Info = "#0f6fbd",
                InfoContrastText = "#ffffff",
                Success = "#0d7a3c",
                SuccessContrastText = "#ffffff",
                Warning = "#b15600",
                WarningContrastText = "#ffffff",
                Error = "#b32318",
                ErrorContrastText = "#ffffff",
                Background = "#f4f7fa",
                BackgroundGray = "#eef2f7",
                Surface = "#ffffff",
                AppbarBackground = "#ffffff",
                AppbarText = "#16202b",
                DrawerBackground = "#ffffff",
                DrawerIcon = "#4a5c70",
                DrawerText = "#16202b",
                TextPrimary = "#16202b",
                TextSecondary = "#4e5a70",
                TextDisabled = "#9aa6b6",
                ActionDefault = "#16202b",
                Divider = "#e1e7ee",
                DividerLight = "#eef2f7",
                LinesDefault = "#e1e7ee",
                LinesInputs = "#c7d0da",
                TableLines = "#e1e7ee",
                TableHover = "#dce8fa",
                TableStriped = "#f8fafc",
                Skeleton = "#e7ecf3"
            },
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
}

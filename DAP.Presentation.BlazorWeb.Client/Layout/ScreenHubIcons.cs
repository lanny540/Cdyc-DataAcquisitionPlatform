using DAP.Core.Shared.Catalog;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Layout;

/// <summary>
/// 大屏导航卡图标键到 MudBlazor 图标常量的映射。
///
/// 图标常量是整段 SVG path，不能落库（会撑爆短字符列），所以卡片只存
/// <see cref="ScreenHubCatalog.IconKeys"/> 里的短键，渲染时在这里取常量。
/// 键集合与目录白名单一一对应，管理端下拉与根路由渲染共用本表。
/// </summary>
public static class ScreenHubIcons
{
    /// <summary>图标键 → MudBlazor 常量映射。全部取自 Icons.Material.Filled 已核实存在的图标。</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["query-stats"] = Icons.Material.Filled.QueryStats,
        ["electric-bolt"] = Icons.Material.Filled.ElectricBolt,
        ["dashboard"] = Icons.Material.Filled.Dashboard,
        ["insights"] = Icons.Material.Filled.Insights,
        ["assessment"] = Icons.Material.Filled.Assessment,
        ["timeline"] = Icons.Material.Filled.Timeline,
        ["leaderboard"] = Icons.Material.Filled.Leaderboard,
        ["sensors"] = Icons.Material.Filled.Sensors,
        ["storage"] = Icons.Material.Filled.Storage,
        ["dns"] = Icons.Material.Filled.Dns,
        ["router"] = Icons.Material.Filled.Router,
        ["settings-input-component"] = Icons.Material.Filled.SettingsInputComponent,
        ["api"] = Icons.Material.Filled.Api,
        ["account-tree"] = Icons.Material.Filled.AccountTree,
        ["group"] = Icons.Material.Filled.Group,
        ["fact-check"] = Icons.Material.Filled.FactCheck,
        ["tune"] = Icons.Material.Filled.Tune,
        ["calendar-month"] = Icons.Material.Filled.CalendarMonth,
        ["upload-file"] = Icons.Material.Filled.UploadFile,
        ["manage-accounts"] = Icons.Material.Filled.ManageAccounts
    };

    /// <summary>图标键到中文显示名的映射，供管理端下拉展示。</summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["query-stats"] = "数据指标",
        ["electric-bolt"] = "能源电力",
        ["dashboard"] = "仪表板",
        ["insights"] = "洞察分析",
        ["assessment"] = "报表评估",
        ["timeline"] = "时间趋势",
        ["leaderboard"] = "排名对比",
        ["sensors"] = "采集传感",
        ["storage"] = "数据存储",
        ["dns"] = "服务器",
        ["router"] = "网络路由",
        ["settings-input-component"] = "点位配置",
        ["api"] = "接口服务",
        ["account-tree"] = "组织架构",
        ["group"] = "部门团队",
        ["fact-check"] = "审计核对",
        ["tune"] = "参数调节",
        ["calendar-month"] = "日历排班",
        ["upload-file"] = "文件上传",
        ["manage-accounts"] = "用户管理"
    };

    /// <summary>
    /// 按图标键取 MudBlazor 图标常量；键无效时回落到仪表板图标。
    /// </summary>
    /// <param name="iconKey">图标键。</param>
    /// <returns>图标常量值。</returns>
    public static string Resolve(string? iconKey)
    {
        return !string.IsNullOrWhiteSpace(iconKey) && Map.TryGetValue(iconKey.Trim(), out string? icon)
            ? icon
            : Icons.Material.Filled.Dashboard;
    }

    /// <summary>
    /// 按图标键取中文显示名；键无效时回落到键本身。
    /// </summary>
    /// <param name="iconKey">图标键。</param>
    /// <returns>显示名。</returns>
    public static string Label(string? iconKey)
    {
        return !string.IsNullOrWhiteSpace(iconKey) && Labels.TryGetValue(iconKey.Trim(), out string? label)
            ? label
            : iconKey ?? string.Empty;
    }
}

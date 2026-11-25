namespace DAP.Core.Shared.Catalog;

/// <summary>
/// 表示大屏导航枢纽页（根路由）上的一张默认跳转卡片。
/// 种子与前端兜底渲染共用本定义，字段与数据库列一一对应。
/// </summary>
/// <param name="Title">卡片标题。</param>
/// <param name="Description">一句话说明。</param>
/// <param name="Route">目标路由，以 / 开头的站内地址。</param>
/// <param name="IconKey">图标键，取值见 <see cref="IconKeys"/>。</param>
/// <param name="Accent">卡片主色键，对应前端 platform-accent-* 类的色名。</param>
/// <param name="SortOrder">显示顺序，小的在前。</param>
public sealed record ScreenHubCardDefault(
    string Title,
    string Description,
    string Route,
    string IconKey,
    string Accent,
    int SortOrder);

/// <summary>
/// 大屏导航卡的共享目录：默认卡片集与图标键 / 主色键白名单。
///
/// 卡片内容可在后台由管理员增删改排序（screen_hub_cards 表），本目录只承担
/// 两件事——空表时的种子内容、新增/编辑时的取值校验白名单。图标键到
/// MudBlazor 图标常量的映射在前端（MudBlazor 包不进 Core.Shared）。
/// </summary>
public static class ScreenHubCatalog
{
    /// <summary>管理员可选择的图标键白名单（对应前端映射表的键）。</summary>
    public static readonly string[] IconKeys =
    [
        "query-stats",
        "electric-bolt",
        "dashboard",
        "insights",
        "assessment",
        "timeline",
        "leaderboard",
        "sensors",
        "storage",
        "dns",
        "router",
        "settings-input-component",
        "api",
        "account-tree",
        "group",
        "fact-check",
        "tune",
        "calendar-month",
        "upload-file",
        "manage-accounts"
    ];

    /// <summary>卡片主色键白名单，与 app.css 中 platform-accent-* 修饰类的色名一致。</summary>
    public static readonly string[] Accents =
    [
        "primary",
        "secondary",
        "tertiary",
        "success",
        "warning",
        "error",
        "info"
    ];

    /// <summary>
    /// 空表时种子写入的默认卡片：三个模块的大屏入口。
    /// </summary>
    public static readonly ScreenHubCardDefault[] DefaultCards =
    [
        new(
            "班组能耗仪表板",
            "钞纸工房班组原辅材料与能源消耗：四项指标合计、历史趋势与数字孪生工房。",
            "/energy-dashboard",
            "query-stats",
            "success",
            1),
        new(
            "园区能源总览",
            "园区级用能总量、介质结构、负荷曲线与分部门工序折标分析。",
            "/park-energy",
            "electric-bolt",
            "secondary",
            2),
        new(
            "平台概览",
            "采集点运行状态、在线率与最新入库数据一览。",
            "/platform-overview",
            "dashboard",
            "info",
            3)
    ];

    /// <summary>图标键是否在白名单内。</summary>
    public static bool IsValidIconKey(string? iconKey)
    {
        return !string.IsNullOrWhiteSpace(iconKey) && IconKeys.Contains(iconKey.Trim(), StringComparer.Ordinal);
    }

    /// <summary>主色键是否在白名单内。</summary>
    public static bool IsValidAccent(string? accent)
    {
        return !string.IsNullOrWhiteSpace(accent) && Accents.Contains(accent.Trim(), StringComparer.Ordinal);
    }

    /// <summary>
    /// 校验跳转路由：必须是站内相对地址（以 / 开头、无协议头），长度 1–200。
    /// </summary>
    /// <param name="route">待校验的路由。</param>
    /// <returns>通过返回 true。</returns>
    public static bool IsValidRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        string trimmed = route.Trim();

        return trimmed.Length is >= 1 and <= 200
               && trimmed.StartsWith("/", StringComparison.Ordinal)
               && !trimmed.Contains("://", StringComparison.Ordinal);
    }
}

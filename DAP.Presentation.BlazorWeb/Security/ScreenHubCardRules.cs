using DAP.Core.Shared.Catalog;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 大屏导航卡的取值校验规则（纯函数，供管理服务与单元测试共用）。
/// </summary>
public static class ScreenHubCardRules
{
    /// <summary>校验卡片标题：1–32 字，去首尾空白。</summary>
    /// <param name="title">标题。</param>
    /// <param name="normalized">归一化后的标题。</param>
    /// <returns>通过返回空串，否则返回中文原因。</returns>
    public static string ValidateTitle(string? title, out string normalized)
    {
        normalized = (title ?? string.Empty).Trim();

        return normalized.Length is >= 1 and <= 32
            ? string.Empty
            : "卡片标题不能为空，且不超过 32 个字。";
    }

    /// <summary>校验说明文字：不超过 256 字。</summary>
    /// <param name="description">说明。</param>
    /// <param name="normalized">归一化后的说明。</param>
    /// <returns>通过返回空串，否则返回中文原因。</returns>
    public static string ValidateDescription(string? description, out string normalized)
    {
        normalized = (description ?? string.Empty).Trim();

        return normalized.Length <= 256
            ? string.Empty
            : "卡片说明不能超过 256 个字。";
    }

    /// <summary>校验目标路由：站内相对地址（/ 开头、无协议头），白名单见 <see cref="ScreenHubCatalog"/>。</summary>
    /// <param name="route">路由。</param>
    /// <param name="normalized">归一化后的路由（去首尾空白）。</param>
    /// <returns>通过返回空串，否则返回中文原因。</returns>
    public static string ValidateRoute(string? route, out string normalized)
    {
        normalized = (route ?? string.Empty).Trim();

        return ScreenHubCatalog.IsValidRoute(normalized)
            ? string.Empty
            : "目标路由必须是站内相对地址（以 / 开头），例如 /energy-dashboard。";
    }

    /// <summary>校验图标键白名单。</summary>
    /// <param name="iconKey">图标键。</param>
    /// <param name="normalized">归一化后的图标键。</param>
    /// <returns>通过返回空串，否则返回中文原因。</returns>
    public static string ValidateIconKey(string? iconKey, out string normalized)
    {
        normalized = (iconKey ?? string.Empty).Trim();

        return ScreenHubCatalog.IsValidIconKey(normalized)
            ? string.Empty
            : "图标不在可选列表内，请重新选择。";
    }

    /// <summary>校验主色键白名单。</summary>
    /// <param name="accent">主色键。</param>
    /// <param name="normalized">归一化后的主色键。</param>
    /// <returns>通过返回空串，否则返回中文原因。</returns>
    public static string ValidateAccent(string? accent, out string normalized)
    {
        normalized = (accent ?? string.Empty).Trim();

        return ScreenHubCatalog.IsValidAccent(normalized)
            ? string.Empty
            : "卡片主色不在可选列表内，请重新选择。";
    }
}

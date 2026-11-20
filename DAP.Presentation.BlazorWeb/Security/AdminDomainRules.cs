using System.Text.RegularExpressions;
using DAP.Core.Shared.Catalog;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 域管理的纯判定规则（docs/AUTH_DESIGN.md 第 4 节）。
///
/// 与 <see cref="AdminUserRules"/> 同样的思路：把「编码能不能用」「这个域能不能删」
/// 抽成可单测的静态判定，服务只负责落库与审计。
/// </summary>
public static partial class AdminDomainRules
{
    /// <summary>
    /// 域编码允许的字符集：小写字母开头，后接小写字母、数字与连字符，2–32 位。
    ///
    /// 编码会被写进授权 claims 与导航比对（`PlatformClaimTypes.Domain`），
    /// 一旦允许大小写混用，`Banknote-Paper` 与 `banknote-paper` 会变成两个域，
    /// 授权看起来配了却不生效。因此统一小写并在入口处拒绝非法值。
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainCodePattern();

    /// <summary>
    /// 判断域编码是否合法。
    /// </summary>
    /// <param name="code">归一化后的域编码。</param>
    /// <returns>合法时返回 true。</returns>
    public static bool IsValidCode(string? code)
    {
        return !string.IsNullOrWhiteSpace(code) && DomainCodePattern().IsMatch(code);
    }

    /// <summary>
    /// 归一化域编码：去首尾空白并转小写。
    /// </summary>
    /// <param name="code">原始输入。</param>
    /// <returns>归一化结果；输入为空时返回空字符串。</returns>
    public static string NormalizeCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 判断域是否允许删除，不允许时返回中文原因。
    ///
    /// 只允许删除**没有页面**的域。域下有页面时删除会让这些页面失去归属、
    /// 从菜单里整体消失，而作者只会看到「页面不见了」——先让管理员把页面迁走或停用更安全。
    /// 系统域（平台管理）不允许删除：它是超管的固定入口。
    /// </summary>
    /// <param name="pageCount">域内页面数量（含已下线）。</param>
    /// <param name="isSystem">是否系统域。</param>
    /// <returns>可删除时返回 null，否则返回中文原因。</returns>
    public static string? DescribeDeleteViolation(int pageCount, bool isSystem)
    {
        if (isSystem)
        {
            return "系统域是平台管理的固定入口，不允许删除。";
        }

        return pageCount > 0
            ? $"该域下还有 {pageCount} 个页面，请先把页面迁到其他域或停用，再删除域。"
            : null;
    }

    /// <summary>
    /// 判断某个内置域编码是否被代码引用（这类编码可以被改名，但不能被重建）。
    /// </summary>
    /// <param name="code">域编码。</param>
    /// <returns>属于内置编码时返回 true。</returns>
    public static bool IsBuiltInCode(string code)
    {
        return code is AuthorizationCatalog.BanknotePaperDomain
            or AuthorizationCatalog.BanknotePrintingDomain
            or AuthorizationCatalog.EnergyDomain
            or AuthorizationCatalog.PlatformDomain;
    }
}

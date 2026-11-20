using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 采集服务器凭据的落库加密（ClientSecret、密码等）。
///
/// 这些字段原先以明文存在 <c>server_connections.configuration_json</c> 里：
/// 能读库的人（备份、只读账号、DBA）可以直接拿到 Historian / Modbus 的密码，
/// 而界面上却已经把它们掩码成 <c>********</c>——掩码只挡住了界面，没挡住数据本身。
///
/// 设计要点：
/// <list type="bullet">
/// <item>密钥复用平台的数据保护密钥环（已持久化到磁盘）。密钥环丢了等于凭据不可用，
/// 与 Cookie 失效同一个前提，不会引入第二套备份要求。</item>
/// <item>带 <c>dp:v1:</c> 前缀识别密文。**没有前缀的值按明文原样返回**——
/// 历史数据不需要迁移，下一次保存时自然升级为密文。</item>
/// <item>解不开（换了密钥、密文被截断）时返回空串而不是抛异常：
/// 让上游以「未配置密码」失败，比把一段密文当密码发出去更容易排查。</item>
/// </list>
/// </summary>
public sealed class PlatformSecretProtector
{
    /// <summary>
    /// 密文前缀。带版本号，将来换算法时可以并行识别。
    /// </summary>
    private const string Prefix = "dp:v1:";

    private readonly IDataProtector _protector;

    /// <summary>
    /// 初始化一个 <see cref="PlatformSecretProtector"/> 实例。
    /// </summary>
    /// <param name="provider">数据保护提供者。</param>
    public PlatformSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("DAP.Secrets.v1");
    }

    /// <summary>
    /// 加密单个凭据值。空值与已加密值原样返回（幂等）。
    /// </summary>
    /// <param name="value">明文。</param>
    /// <returns>密文或原值。</returns>
    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
        {
            return value ?? string.Empty;
        }

        return Prefix + _protector.Protect(value);
    }

    /// <summary>
    /// 解密单个凭据值。没有前缀的按明文处理（历史数据兼容）。
    /// </summary>
    /// <param name="value">密文或明文。</param>
    /// <returns>明文；解不开时返回空串。</returns>
    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!IsProtected(value))
        {
            return value;
        }

        try
        {
            return _protector.Unprotect(value[Prefix.Length..]);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 判断某个值是否已经是密文。
    /// </summary>
    /// <param name="value">待判断的值。</param>
    /// <returns>是密文时返回 true。</returns>
    public static bool IsProtected(string? value)
    {
        return value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }
}

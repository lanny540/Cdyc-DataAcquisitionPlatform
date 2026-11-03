using System.Security.Cryptography;

namespace DAP.Core.Domain.Services;

/// <summary>
/// 提供本地账号的密码哈希与校验。采用 PBKDF2-HMAC-SHA256，每个账号独立随机盐。
///
/// 只做哈希算法本身，不涉及账号状态与锁定策略——那些属于登录服务的职责。
/// </summary>
public static class PasswordHasher
{
    /// <summary>
    /// 迭代次数。数值越高越难暴力破解，代价是每次登录多花几十毫秒。
    /// </summary>
    public const int Iterations = 210_000;

    /// <summary>
    /// 存储格式的算法标识。将来若更换算法或迭代次数，靠它区分旧哈希。
    /// </summary>
    private const string Algorithm = "PBKDF2-SHA256";

    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>
    /// 计算密码哈希，输出形如 <c>PBKDF2-SHA256$210000$salt$hash</c> 的字符串。
    /// </summary>
    /// <param name="password">明文密码。</param>
    /// <returns>可直接落库的哈希字符串。</returns>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return string.Join('$', Algorithm, Iterations.ToString(), Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    /// <summary>
    /// 校验密码是否与哈希匹配。哈希为空（外部账号）或格式非法时一律返回 false。
    /// </summary>
    /// <param name="password">待校验的明文密码。</param>
    /// <param name="encodedHash">库中保存的哈希。</param>
    /// <returns>匹配时返回 true。</returns>
    public static bool Verify(string? password, string? encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedKey.Length == 0)
        {
            return false;
        }

        // 以库中记录的迭代次数重算：将来调高默认值后，旧哈希仍可校验通过。
        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}

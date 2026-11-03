using System.Security.Cryptography;
using DAP.Core.Domain.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 密码哈希的契约测试。
///
/// 哈希串是落库格式：一旦上线，格式变更必须能兼容旧数据，
/// 因此这里把「格式、加盐、迭代次数可回读」三件事固化下来。
/// </summary>
public class PasswordHasherTests
{
    private const string Password = "Cdyc@2026dev";

    [Fact]
    public void Hash_Should_Produce_Four_Part_Encoded_String()
    {
        var encoded = PasswordHasher.Hash(Password);
        var parts = encoded.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("PBKDF2-SHA256", parts[0]);
        Assert.Equal(PasswordHasher.Iterations, int.Parse(parts[1]));
        Assert.NotEmpty(parts[2]);
        Assert.NotEmpty(parts[3]);
    }

    /// <summary>
    /// 同一密码两次哈希必须不同：盐是随机的，否则相同密码在库里长得一样，
    /// 拖库后可以一眼看出哪些账号用同一个密码。
    /// </summary>
    [Fact]
    public void Hash_Should_Use_Random_Salt()
    {
        var first = PasswordHasher.Hash(Password);
        var second = PasswordHasher.Hash(Password);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Split('$')[2], second.Split('$')[2]);
        Assert.True(PasswordHasher.Verify(Password, first));
        Assert.True(PasswordHasher.Verify(Password, second));
    }

    [Fact]
    public void Verify_Should_Accept_Correct_Password()
    {
        Assert.True(PasswordHasher.Verify(Password, PasswordHasher.Hash(Password)));
    }

    [Theory]
    [InlineData("cdyc@2026dev")]
    [InlineData("Cdyc@2026de")]
    [InlineData("")]
    public void Verify_Should_Reject_Wrong_Password(string candidate)
    {
        Assert.False(PasswordHasher.Verify(candidate, PasswordHasher.Hash(Password)));
    }

    /// <summary>
    /// 外部账号（LDAP / OIDC）没有本地密码，库里哈希为空。
    /// 此时任何输入都必须失败，不能因为「空等于空」而放行。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_Should_Reject_Missing_Hash(string? encodedHash)
    {
        Assert.False(PasswordHasher.Verify(Password, encodedHash));
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256$210000$only-three-parts")]
    [InlineData("MD5$210000$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2-SHA256$0$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2-SHA256$210000$***$aGFzaA==")]
    public void Verify_Should_Reject_Malformed_Hash(string encodedHash)
    {
        Assert.False(PasswordHasher.Verify(Password, encodedHash));
    }

    /// <summary>
    /// 迭代次数写在哈希串里，校验时以库中记录为准：
    /// 将来把默认值调高，老账号仍能用原迭代次数登录，不会被一刀切锁在门外。
    /// </summary>
    [Fact]
    public void Verify_Should_Honor_Iterations_From_Stored_Hash()
    {
        const int legacyIterations = 1_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(Password, salt, legacyIterations, HashAlgorithmName.SHA256, 32);
        var legacy = $"PBKDF2-SHA256${legacyIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        Assert.True(PasswordHasher.Verify(Password, legacy));
        Assert.False(PasswordHasher.Verify("wrong-password", legacy));
    }
}

using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证域管理的纯判定规则（docs/AUTH_DESIGN.md 第 4 节）。
///
/// 这些规则决定了「编码能不能落库」与「域能不能删」，属于配置层最容易出事的两处：
/// 编码大小写混用会让授权看起来配了却不生效；删掉带页面的域会让页面整体消失。
/// </summary>
public class AdminDomainRulesTests
{
    [Theory]
    [InlineData("banknote-paper", true)]
    [InlineData("energy", true)]
    [InlineData("ab", true)]
    [InlineData("line-2", true)]
    [InlineData("Banknote", false)] // 大写会造成「同一个域两个编码」
    [InlineData("1abc", false)] // 不能以数字开头
    [InlineData("a", false)] // 至少 2 位
    [InlineData("a_b", false)] // 下划线不在允许集内
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Domain_Code_Validation_Should_Follow_Catalog_Convention(string? code, bool expected)
    {
        Assert.Equal(expected, AdminDomainRules.IsValidCode(code));
    }

    [Theory]
    [InlineData("  Banknote-Paper  ", "banknote-paper")]
    [InlineData("ENERGY", "energy")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_Code_Should_Trim_And_Lowercase(string? input, string expected)
    {
        Assert.Equal(expected, AdminDomainRules.NormalizeCode(input));
    }

    [Fact]
    public void System_Domain_Should_Not_Be_Deletable()
    {
        Assert.NotNull(AdminDomainRules.DescribeDeleteViolation(pageCount: 0, isSystem: true));
    }

    [Fact]
    public void Domain_With_Pages_Should_Not_Be_Deletable()
    {
        var reason = AdminDomainRules.DescribeDeleteViolation(pageCount: 3, isSystem: false);

        Assert.NotNull(reason);
        Assert.Contains("3", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_Business_Domain_Should_Be_Deletable()
    {
        Assert.Null(AdminDomainRules.DescribeDeleteViolation(pageCount: 0, isSystem: false));
    }

    [Theory]
    [InlineData("banknote-paper", true)]
    [InlineData("platform-admin", true)]
    [InlineData("custom-domain", false)]
    public void Built_In_Code_Detection_Should_Match_Catalog(string code, bool expected)
    {
        Assert.Equal(expected, AdminDomainRules.IsBuiltInCode(code));
    }
}

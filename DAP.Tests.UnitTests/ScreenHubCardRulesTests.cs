using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证大屏导航卡的取值校验规则与共享目录的白名单一致性。
/// </summary>
public sealed class ScreenHubCardRulesTests
{
    [Fact]
    public void ValidateTitle_Should_Accept_Normal_Title()
    {
        string error = ScreenHubCardRules.ValidateTitle("  班组能耗仪表板  ", out string normalized);

        Assert.Equal(string.Empty, error);
        Assert.Equal("班组能耗仪表板", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateTitle_Should_Reject_Blank(string? title)
    {
        string error = ScreenHubCardRules.ValidateTitle(title, out _);

        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void ValidateTitle_Should_Reject_Over_32_Chars()
    {
        string error = ScreenHubCardRules.ValidateTitle(new string('长', 33), out _);

        Assert.NotEqual(string.Empty, error);
    }

    [Theory]
    [InlineData("/energy-dashboard")]
    [InlineData("/park-energy/meters")]
    [InlineData("/platform-overview")]
    public void ValidateRoute_Should_Accept_Relative_Route(string route)
    {
        string error = ScreenHubCardRules.ValidateRoute(route, out string normalized);

        Assert.Equal(string.Empty, error);
        Assert.Equal(route, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("energy-dashboard")]
    [InlineData("http://10.9.3.54:8080/historian-rest-api")]
    [InlineData("https://example.com")]
    public void ValidateRoute_Should_Reject_Non_Relative_Or_External(string? route)
    {
        string error = ScreenHubCardRules.ValidateRoute(route, out _);

        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void ValidateIconKey_Should_Accept_Only_Whitelisted_Keys()
    {
        foreach (string iconKey in ScreenHubCatalog.IconKeys)
        {
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateIconKey(iconKey, out _));
        }

        Assert.NotEqual(string.Empty, ScreenHubCardRules.ValidateIconKey("not-exist", out _));
        Assert.NotEqual(string.Empty, ScreenHubCardRules.ValidateIconKey(null, out _));
    }

    [Fact]
    public void ValidateAccent_Should_Accept_Only_Whitelisted_Accents()
    {
        foreach (string accent in ScreenHubCatalog.Accents)
        {
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateAccent(accent, out _));
        }

        Assert.NotEqual(string.Empty, ScreenHubCardRules.ValidateAccent("magenta", out _));
        Assert.NotEqual(string.Empty, ScreenHubCardRules.ValidateAccent(null, out _));
    }

    [Fact]
    public void DefaultCards_Should_Pass_All_Validation_And_Be_Unique_By_Route()
    {
        Assert.NotEmpty(ScreenHubCatalog.DefaultCards);

        foreach (ScreenHubCardDefault card in ScreenHubCatalog.DefaultCards)
        {
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateTitle(card.Title, out _));
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateDescription(card.Description, out _));
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateRoute(card.Route, out _));
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateIconKey(card.IconKey, out _));
            Assert.Equal(string.Empty, ScreenHubCardRules.ValidateAccent(card.Accent, out _));
        }

        Assert.Equal(
            ScreenHubCatalog.DefaultCards.Length,
            ScreenHubCatalog.DefaultCards.Select(card => card.Route).Distinct().Count());
    }
}

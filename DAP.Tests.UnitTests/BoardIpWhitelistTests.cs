using System.Net;
using DAP.Presentation.BlazorWeb.Security;
using Xunit;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 车间白名单 CIDR 匹配的守卫测试（docs/AUTH_DESIGN.md 6.6 五条硬约束之 1、2）。
/// </summary>
public sealed class BoardIpWhitelistTests
{
    [Theory]
    [InlineData("192.168.1.10", "192.168.1.10", true)]
    [InlineData("192.168.1.10/32", "192.168.1.10", true)]
    [InlineData("192.168.1.10", "192.168.1.11", false)]
    [InlineData("192.168.1.0/24", "192.168.1.200", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("10.0.0.5/8", "10.255.0.1", true)]
    [InlineData("10.0.0.5", "10.0.0.5", true)]
    [InlineData("2001:db8::1/128", "2001:db8::1", true)]
    [InlineData("2001:db8::1/128", "2001:db8::2", false)]
    [InlineData("2001:db8::/64", "2001:db8::ffff", true)]
    [InlineData("192.168.1.10", "2001:db8::1", false)]
    [InlineData("", "192.168.1.10", false)]
    [InlineData("not-an-ip", "192.168.1.10", false)]
    [InlineData("192.168.1.10/abc", "192.168.1.10", false)]
    [InlineData("192.168.1.10/40", "192.168.1.10", false)]
    public void IsMatch_Should_Resolve_Cidr_Entries(string entry, string remote, bool expected)
    {
        var actual = BoardIpWhitelist.IsMatch(IPAddress.Parse(remote), entry);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("192.168.1.10", false)]
    [InlineData("192.168.1.10/32", false)]
    [InlineData("192.168.1.0/24", false)]
    [InlineData("192.168.1.0/23", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("2001:db8::1/128", false)]
    [InlineData("2001:db8::/64", true)]
    [InlineData("not-an-ip", true)]
    public void IsWideScope_Should_Flag_Entries_Wider_Than_Slash24(string entry, bool expected)
    {
        var actual = BoardIpWhitelist.IsWideScope(entry);

        Assert.Equal(expected, actual);
    }
}

using System.Net;
using System.Net.Sockets;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 车间终端白名单的 CIDR 匹配（docs/AUTH_DESIGN.md 6.6）。
///
/// IP 只决定「是否签发只读会话」，授权判定仍然只认 claims——
/// 这里不做任何权限判断，签发时的 claims 集合才是授权边界。
/// </summary>
public static class BoardIpWhitelist
{
    /// <summary>
    /// 判断远端地址是否命中一条白名单条目。条目支持 CIDR（如 192.168.1.10/32）；
    /// 不带前缀长度的条目按单机地址处理（IPv4 即 /32，IPv6 即 /128）。
    /// </summary>
    /// <param name="remote">远端地址。</param>
    /// <param name="entry">白名单条目。</param>
    /// <returns>命中时返回 true。</returns>
    public static bool IsMatch(IPAddress remote, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var parts = entry.Trim().Split('/');
        if (!IPAddress.TryParse(parts[0], out IPAddress? baseAddress))
        {
            return false;
        }

        var maxPrefix = baseAddress.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        var prefixLength = maxPrefix;

        if (parts.Length > 1 && (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix))
        {
            return false;
        }

        // 地址族不同（IPv4 vs IPv6）不匹配；IPv4 映射地址先归一化。
        remote = Unmap(remote);
        baseAddress = Unmap(baseAddress);

        if (remote.AddressFamily != baseAddress.AddressFamily)
        {
            return false;
        }

        byte[] remoteBytes = remote.GetAddressBytes();
        byte[] baseBytes = baseAddress.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (remoteBytes[i] != baseBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte) (0xFF << (8 - remainingBits));
        return (remoteBytes[fullBytes] & mask) == (baseBytes[fullBytes] & mask);
    }

    /// <summary>
    /// 判断条目是否为宽网段（IPv4 前缀长度小于 24）。
    /// 宽网段意味着整个网段的设备都能拿到只读会话，启动时必须告警。
    /// 不带前缀的条目按单机地址处理，不算宽段。
    /// </summary>
    /// <param name="entry">白名单条目。</param>
    /// <returns>是宽网段时返回 true。</returns>
    public static bool IsWideScope(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var parts = entry.Trim().Split('/');
        if (!IPAddress.TryParse(parts[0], out IPAddress? baseAddress))
        {
            return true;
        }

        if (baseAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 不套用 /24 规则，按前缀显式判断：小于 120 视为宽段。
            return parts.Length > 1 && int.TryParse(parts[1], out var v6Prefix) && v6Prefix < 120;
        }

        // 无前缀长度 = 单机（/32），不是宽段。
        return parts.Length > 1 && (!int.TryParse(parts[1], out var prefix) || prefix < 24);
    }

    private static IPAddress Unmap(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}

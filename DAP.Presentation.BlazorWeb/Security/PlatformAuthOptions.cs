namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 认证相关配置。对应 appsettings 的 <c>Auth</c> 节。
/// </summary>
public sealed class PlatformAuthOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// 获取或设置初始超级管理员配置。库中已存在超级管理员时该配置被忽略。
    /// </summary>
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();

    /// <summary>
    /// 获取或设置登录失败锁定策略。
    /// </summary>
    public LockoutOptions Lockout { get; set; } = new();

    /// <summary>
    /// 获取或设置会话 Cookie 时长。
    /// </summary>
    public CookieSessionOptions Cookie { get; set; } = new();

    /// <summary>
    /// 获取或设置车间只读终端的 IP 白名单配置（docs/AUTH_DESIGN.md 6.6）。
    /// </summary>
    public BoardOptions Board { get; set; } = new();
}

/// <summary>
/// 车间只读终端配置。全场仅一台、所有班组共用、只看数据：
/// 命中白名单的未认证请求直接签发只读会话（钞纸域 + 等级 1），不弹登录。
/// </summary>
public sealed class BoardOptions
{
    /// <summary>
    /// 获取或设置是否启用。默认关闭：白名单未配置就启用会拒绝启动。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置白名单地址（CIDR）。生产环境只应有一条单机 /32；
    /// 宽于 /24 的条目启动时会输出告警——开成网段等于整个网段的人都能读到全部班组数据。
    /// </summary>
    public string[] IpWhitelist { get; set; } = [];

    /// <summary>
    /// 获取或设置会话固定有效期（小时），覆盖一个班次，不做滑动续期。
    /// </summary>
    public int SessionHours { get; set; } = 12;
}

/// <summary>
/// 初始超级管理员配置。
/// </summary>
public sealed class BootstrapAdminOptions
{
    /// <summary>
    /// 获取或设置登录账号。
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 获取或设置姓名。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 获取或设置初始密码。首次登录会被强制要求修改。
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// 登录失败锁定策略。
/// </summary>
public sealed class LockoutOptions
{
    /// <summary>
    /// 获取或设置触发锁定的连续失败次数。
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// 获取或设置锁定时长（分钟）。
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>
/// 会话 Cookie 时长。滑动过期覆盖一个工作日，绝对过期防止会话无限延长。
/// </summary>
public sealed class CookieSessionOptions
{
    /// <summary>
    /// 获取或设置滑动过期小时数。
    /// </summary>
    public int SlidingHours { get; set; } = 8;

    /// <summary>
    /// 获取或设置绝对过期小时数。超过后即使一直在操作也要求重新登录。
    /// </summary>
    public int AbsoluteHours { get; set; } = 12;
}

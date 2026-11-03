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

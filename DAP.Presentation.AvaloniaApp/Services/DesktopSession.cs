using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Clients;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.AvaloniaApp.Services;

/// <summary>
/// 桌面端会话：持有带 Cookie 的 <see cref="HttpClient"/>、当前登录用户与全部业务接口。
///
/// 服务端会话是 Cookie 方式签发的，浏览器会自动保存，桌面端必须自己持有
/// <see cref="CookieContainer"/>——容器随本单例存活，HttpClient 一旦被工厂回收，
/// 容器也会跟着丢，表现为「刚登录就掉线」。因此这里直接 new 一个专用 HttpClient，
/// 不走 IHttpClientFactory 的连接池。
/// </summary>
public sealed partial class DesktopSession : ObservableObject
{
    private readonly PlatformAuthClient _authClient;

    /// <summary>
    /// 初始化桌面会话。
    /// </summary>
    /// <param name="options">API 配置。</param>
    public DesktopSession(IOptions<ApiOptions> options)
    {
        ApiOptions apiOptions = options.Value;
        Uri baseUri = new(apiOptions.BaseUrl);

        ServerAddress = baseUri.GetLeftPart(UriPartial.Authority);

        CookieContainer cookies = new();
        HttpClientHandler handler = new()
        {
            CookieContainer = cookies,
            UseCookies = true,
            // 登录/改密用 302 表达结果，失败原因在 Location 的 error 参数里，
            // 自动跟随重定向会把它变成一张 HTML 页面，拿不到任何原因。
            AllowAutoRedirect = false
        };

        Http = new HttpClient(handler)
        {
            BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(Math.Clamp(apiOptions.TimeoutSeconds, 5, 180))
        };

        _authClient = new PlatformAuthClient(Http);
        Api = new PlatformApiClient(Http);
    }

    /// <summary>
    /// 会话专用的 HTTP 客户端（带 Cookie 容器）。
    /// </summary>
    public HttpClient Http { get; }

    /// <summary>
    /// 平台业务接口。
    /// </summary>
    public IPlatformApiClient Api { get; }

    /// <summary>
    /// 服务端地址，用于登录页与抽屉底部展示。
    /// </summary>
    public string ServerAddress { get; }

    /// <summary>
    /// 当前登录用户；未登录时为 null。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticated))]
    [NotifyPropertyChangedFor(nameof(UserDisplayName))]
    [NotifyPropertyChangedFor(nameof(UserRoleText))]
    [NotifyPropertyChangedFor(nameof(MustChangePassword))]
    private AuthUserDto? _currentUser;

    /// <summary>
    /// 是否已登录。
    /// </summary>
    public bool IsAuthenticated => CurrentUser is not null;

    /// <summary>
    /// 顶栏展示的用户名。
    /// </summary>
    public string UserDisplayName => CurrentUser?.DisplayName ?? "未登录";

    /// <summary>
    /// 顶栏展示的部门与角色。
    /// </summary>
    public string UserRoleText
    {
        get
        {
            if (CurrentUser is null)
            {
                return "请先登录";
            }

            string roles = CurrentUser.Roles.Count == 0 ? "普通用户" : string.Join(" / ", CurrentUser.Roles);
            return string.IsNullOrWhiteSpace(CurrentUser.DepartmentName)
                ? roles
                : $"{CurrentUser.DepartmentName} · {roles}";
        }
    }

    /// <summary>
    /// 是否处于「首次登录必须改密」状态。
    /// </summary>
    public bool MustChangePassword => CurrentUser?.MustChangePassword == true;

    /// <summary>
    /// 尝试用已有 Cookie 恢复会话。服务端不可达时返回 false，不抛异常。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话有效时返回 true。</returns>
    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CurrentUser = await _authClient.GetCurrentUserAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 启动时的网络故障不该挡住界面：留在登录页并提示，比弹一个未处理异常更可诊断。
            CurrentUser = null;
        }

        return IsAuthenticated;
    }

    /// <summary>
    /// 登录。
    /// </summary>
    /// <param name="userName">账号。</param>
    /// <param name="password">密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果。</returns>
    public async Task<PlatformSignInResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        PlatformSignInResult result = await _authClient.SignInAsync(userName, password, cancellationToken);

        if (!result.Succeeded)
        {
            return result;
        }

        AuthUserDto? user = await _authClient.GetCurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return PlatformSignInResult.Failure("登录成功但未能读取会话，请重试。");
        }

        CurrentUser = user;
        return PlatformSignInResult.Success;
    }

    /// <summary>
    /// 修改密码。首次登录强制改密时同样走这里，成功后会刷新会话信息。
    /// </summary>
    /// <param name="currentPassword">当前密码。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="confirmPassword">确认密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        AuthOperationResultDto result =
            await _authClient.ChangePasswordAsync(currentPassword, newPassword, confirmPassword, cancellationToken);

        if (result.Succeeded)
        {
            CurrentUser = await _authClient.GetCurrentUserAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// 退出登录。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _authClient.SignOutAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 服务端不可达时也要允许本地退出，否则现场只能强杀进程。
        }

        CurrentUser = null;
    }
}

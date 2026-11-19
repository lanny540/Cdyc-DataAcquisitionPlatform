using DAP.Core.Shared.Contracts;

namespace DAP.Clients;

/// <summary>
/// 平台认证接口的客户端契约：登录、读取当前会话、登出与改密。
///
/// Web 端的登录走浏览器原生表单（见 <c>AuthEndpoints</c>），不需要本接口；
/// 桌面端没有浏览器代管 Cookie，必须自己发起表单请求并持有会话，
/// 因此把「如何与服务端交换凭据」也收在共享客户端里，避免两端各写一套。
/// </summary>
public interface IPlatformAuthClient
{
    /// <summary>
    /// 使用账号密码登录，成功后会话 Cookie 由底层 <c>CookieContainer</c> 持有。
    /// </summary>
    /// <param name="userName">账号。</param>
    /// <param name="password">密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果。失败时 <see cref="PlatformSignInResult.ErrorMessage"/> 为中文原因。</returns>
    Task<PlatformSignInResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取当前会话对应的用户。未登录或会话失效时返回 null。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前用户。</returns>
    Task<AuthUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 退出登录并清除服务端会话。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改密码。首次登录被强制改密时同样走这里。
    /// </summary>
    /// <param name="currentPassword">当前密码。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="confirmPassword">确认密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    Task<AuthOperationResultDto> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 表示一次登录尝试的结果。
/// </summary>
/// <param name="Succeeded">是否登录成功。</param>
/// <param name="ErrorMessage">失败原因；成功时为空。</param>
public sealed record PlatformSignInResult(bool Succeeded, string? ErrorMessage)
{
    /// <summary>
    /// 构造成功结果。
    /// </summary>
    public static PlatformSignInResult Success { get; } = new(true, null);

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="message">中文失败原因。</param>
    /// <returns>失败结果。</returns>
    public static PlatformSignInResult Failure(string message)
    {
        return new PlatformSignInResult(false, message);
    }
}
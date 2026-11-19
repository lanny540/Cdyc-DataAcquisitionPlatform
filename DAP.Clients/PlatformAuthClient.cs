using System.Net;
using System.Net.Http.Json;
using DAP.Core.Shared.Contracts;

namespace DAP.Clients;

/// <summary>
/// <see cref="IPlatformAuthClient"/> 的 HTTP 实现。
///
/// 服务端登录/改密端点是给浏览器表单设计的：成功与失败都靠 302 跳转表达，
/// 失败原因放在 <c>Location</c> 的 <c>error</c> 查询参数里。桌面端因此必须
/// **关闭自动重定向**，否则 302 会被跟成一张 HTML 页面，拿不到任何原因。
/// 会话 Cookie 由调用方配置的 <c>CookieContainer</c> 负责保存与回放。
/// </summary>
public sealed class PlatformAuthClient(HttpClient httpClient) : IPlatformAuthClient
{
    /// <inheritdoc />
    public async Task<PlatformSignInResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["userName"] = userName,
            ["password"] = password,
            ["returnUrl"] = string.Empty
        });

        using HttpResponseMessage response =
            await httpClient.PostAsync("/api/auth/login", content, cancellationToken);

        return await InterpretCredentialResponseAsync(response, "登录失败。", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuthUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.GetAsync("/api/auth/me", cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;

        await PlatformHttpResponse.EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<AuthUserDto>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync("/api/auth/logout", null, cancellationToken);

        // 登出失败不应阻塞界面：本地已清空会话时，服务端会话会在绝对过期时间后自然失效。
        _ = response.StatusCode;
    }

    /// <inheritdoc />
    public async Task<AuthOperationResultDto> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return new AuthOperationResultDto(false, "两次输入的新密码不一致。");

        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["currentPassword"] = currentPassword,
            ["newPassword"] = newPassword,
            ["confirmPassword"] = confirmPassword
        });

        using HttpResponseMessage response =
            await httpClient.PostAsync("/api/auth/change-password", content, cancellationToken);

        PlatformSignInResult result =
            await InterpretCredentialResponseAsync(response, "修改密码失败。", cancellationToken);

        return new AuthOperationResultDto(result.Succeeded, result.ErrorMessage);
    }

    /// <summary>
    /// 解析登录/改密这类「重定向表达结果」的响应：
    /// 成功是 302 且 Location 不含 error；失败是 302 且 Location 带 error 参数。
    /// 未开启自动重定向的处理器才会走到这里，若被配置成自动重定向则退化为状态码判定。
    /// </summary>
    private static async Task<PlatformSignInResult> InterpretCredentialResponseAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Found
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.SeeOther)
        {
            string location = response.Headers.Location?.ToString() ?? string.Empty;
            string? error = ReadQueryValue(location, "error");

            return string.IsNullOrWhiteSpace(error)
                ? PlatformSignInResult.Success
                : PlatformSignInResult.Failure(error);
        }

        if (response.IsSuccessStatusCode)
            // 自动重定向已跟随时，服务端失败会最终渲染登录页（200 HTML），无法取到原因。
            return PlatformSignInResult.Failure(fallbackMessage);

        return PlatformSignInResult.Failure(
            await PlatformHttpResponse.ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>
    /// 从相对地址中取查询参数。Location 可能是相对地址（<c>/login?error=...</c>），
    /// 因此不能依赖 <see cref="Uri"/> 的绝对地址解析。
    /// </summary>
    private static string? ReadQueryValue(string location, string key)
    {
        int queryIndex = location.IndexOf('?');
        if (queryIndex < 0 || queryIndex == location.Length - 1) return null;

        foreach (string pair in location[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0) continue;

            if (!string.Equals(pair[..separator], key, StringComparison.OrdinalIgnoreCase)) continue;

            string raw = pair[(separator + 1)..].Replace('+', ' ');
            return Uri.UnescapeDataString(raw);
        }

        return null;
    }
}
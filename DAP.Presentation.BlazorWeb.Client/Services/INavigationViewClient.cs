using System.Security.Claims;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

/// <summary>
/// 双端导航加载器。
///
/// 服务端实现（SSR 预渲染与 Server 电路）按级联认证状态直调导航构建服务，无 HTTP 往返；
/// WASM 实现调 /api/navigation，服务端按会话 Cookie 过滤后返回同样的树。
/// 两端返回结构一致，MainLayout 不感知自己跑在哪一端。
/// </summary>
public interface INavigationViewClient
{
    /// <summary>
    /// 加载当前用户的导航树。
    /// </summary>
    /// <param name="principal">当前级联认证状态中的主体；WASM 端忽略（服务端按 Cookie 判定）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导航树；未认证或加载失败时返回 null，由路由守卫处理后续跳转。</returns>
    Task<NavigationDto?> LoadAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default);
}

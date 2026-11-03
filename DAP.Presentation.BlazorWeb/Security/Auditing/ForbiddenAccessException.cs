namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 表示会话缺少执行写操作所需的权限点。由领域服务装饰器抛出，
/// 中间件统一映射为 API 403 或页面跳转 /forbidden（docs/AUTH_DESIGN.md 7.2）。
/// </summary>
public sealed class ForbiddenAccessException(string message) : Exception(message);

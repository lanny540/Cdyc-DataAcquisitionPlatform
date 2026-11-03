using DAP.Core.Shared.Catalog;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 端点策略挂载扩展。集中收敛策略名的书写位置，
/// 端点文件不再各自拼字符串（docs/AUTH_DESIGN.md 第 10 节）。
/// </summary>
public static class PlatformEndpointPolicyExtensions
{
    /// <summary>
    /// 要求可见钞纸职能域。能耗指标、数据报表等查看类端点组的读策略。
    /// </summary>
    /// <typeparam name="TBuilder">端点构建器。</typeparam>
    /// <param name="builder">端点构建器。</param>
    /// <returns>当前构建器。</returns>
    public static TBuilder RequireBanknotePaperDomain<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(
            AuthorizationCatalog.PolicyNames.Domain(AuthorizationCatalog.BanknotePaperDomain));
    }

    /// <summary>
    /// 要求配置类页面等级（≥ 2）。排班等只服务配置类页面的端点组的读策略。
    /// </summary>
    /// <typeparam name="TBuilder">端点构建器。</typeparam>
    /// <param name="builder">端点构建器。</param>
    /// <returns>当前构建器。</returns>
    public static TBuilder RequireConfigureAccess<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(AuthorizationCatalog.PolicyNames.Configure);
    }

    /// <summary>
    /// 要求平台管理（系统域）权限：仅超级管理员通过。采集配置与接口联调类端点。
    /// </summary>
    /// <typeparam name="TBuilder">端点构建器。</typeparam>
    /// <param name="builder">端点构建器。</param>
    /// <returns>当前构建器。</returns>
    public static TBuilder RequirePlatformAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(AuthorizationCatalog.PolicyNames.PlatformAdmin);
    }

    /// <summary>
    /// 要求指定权限点。写操作端点的附加策略，与组级读策略叠加（AND）。
    /// </summary>
    /// <typeparam name="TBuilder">端点构建器。</typeparam>
    /// <param name="builder">端点构建器。</param>
    /// <param name="permissionCode">权限点编码，取 AuthorizationCatalog 中的常量。</param>
    /// <returns>当前构建器。</returns>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permissionCode)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(permissionCode);
    }
}

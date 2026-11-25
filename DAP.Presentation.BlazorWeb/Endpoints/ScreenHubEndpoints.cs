using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供大屏导航枢纽页（根路由）的卡片端点。
///
/// 读取端点只要求登录（与导航树一致的口径：所有登录用户都能看到大屏入口）；
/// 管理端点归 action.domain.manage——大屏导航是界面配置，与域与页面配置同组权限。
/// </summary>
public static class ScreenHubEndpoints
{
    /// <summary>
    /// 映射大屏导航卡端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapScreenHubEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/screen-hub/cards",
                async (ScreenHubAdminService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetVisibleCardsAsync(cancellationToken)))
            .WithName("GetScreenHubCards")
            .WithTags("ScreenHub");

        RouteGroupBuilder adminGroup = endpoints.MapGroup("/api/admin/screen-hub-cards")
            .WithTags("AdminScreenHub")
            .RequirePermission(Authorization_DomainManage);

        adminGroup.MapGet(
                "/",
                async (ScreenHubAdminService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetAdminCardsAsync(cancellationToken)))
            .WithName("GetAdminScreenHubCards");

        adminGroup.MapPost(
                "/",
                async (ScreenHubCardUpsertRequest request, ScreenHubAdminService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.CreateCardAsync(request, cancellationToken);
                    return result.Succeeded
                        ? Results.NoContent()
                        : Results.BadRequest(new { message = result.ErrorMessage });
                })
            .WithName("CreateScreenHubCard");

        adminGroup.MapPut(
                "/{cardId:guid}",
                async (Guid cardId, ScreenHubCardUpsertRequest request, ScreenHubAdminService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateCardAsync(cardId, request, cancellationToken);
                    return result.Succeeded
                        ? Results.NoContent()
                        : Results.BadRequest(new { message = result.ErrorMessage });
                })
            .WithName("UpdateScreenHubCard");

        adminGroup.MapPut(
                "/{cardId:guid}/move",
                async (Guid cardId, ScreenHubCardMoveRequest request, ScreenHubAdminService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.MoveCardAsync(cardId, request.Direction, cancellationToken);
                    return result.Succeeded
                        ? Results.NoContent()
                        : Results.BadRequest(new { message = result.ErrorMessage });
                })
            .WithName("MoveScreenHubCard");

        adminGroup.MapDelete(
                "/{cardId:guid}",
                async (Guid cardId, ScreenHubAdminService service, CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.DeleteCardAsync(cardId, cancellationToken);
                    return result.Succeeded
                        ? Results.NoContent()
                        : Results.BadRequest(new { message = result.ErrorMessage });
                })
            .WithName("DeleteScreenHubCard");

        return endpoints;
    }

    /// <summary>域与页面配置权限常量（与 AdminEndpoints 保持同一授权口径）。</summary>
    private const string Authorization_DomainManage = AuthorizationCatalog.DomainManagePermission;
}

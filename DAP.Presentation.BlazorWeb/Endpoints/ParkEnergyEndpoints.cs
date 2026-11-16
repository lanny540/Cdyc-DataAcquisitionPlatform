using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供园区能源板块相关 API 端点映射。
///
/// 与 <see cref="EnergyMetricEndpoints"/> 的区别：那组端点在钞纸域下，
/// 给班组能耗四页用；这组在能源域下，服务全园区的经营视角。
/// 两组数据源、口径、受众都不同，不要合并。
/// </summary>
public static class ParkEnergyEndpoints
{
    /// <summary>
    /// 映射园区能源相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapParkEnergyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/park-energy").WithTags("ParkEnergy")
            .RequireAuthorization(AuthorizationCatalog.PolicyNames.Domain(AuthorizationCatalog.EnergyDomain));

        group.MapGet(
                "/overview",
                async (string? period, bool? demo, IParkEnergyQueryService service,
                    CancellationToken cancellationToken) =>
                {
                    ParkEnergyOverviewDto overview = await service.GetOverviewAsync(
                        period ?? "month",
                        demo ?? false,
                        cancellationToken);

                    return Results.Ok(overview);
                })
            .WithName("GetParkEnergyOverview");

        group.MapGet(
                "/dictionary",
                async (IParkEnergyQueryService service, CancellationToken cancellationToken) =>
                {
                    EnergyDictionaryDto dictionary = await service.GetDictionaryAsync(cancellationToken);
                    return Results.Ok(dictionary);
                })
            .WithName("GetParkEnergyDictionary");

        group.MapGet(
                "/meters",
                async (IParkEnergyQueryService service, CancellationToken cancellationToken) =>
                {
                    IReadOnlyList<EnergyMeterDto> meters = await service.GetMetersAsync(cancellationToken);
                    return Results.Ok(meters);
                })
            .WithName("GetEnergyMeters");

        group.MapPost(
                "/meters",
                async (EnergyMeterUpsertRequest request, IParkEnergyQueryService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        EnergyMeterDto saved = await service.UpsertMeterAsync(request, cancellationToken);
                        return Results.Ok(saved);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                })
            // 台账维护属于配置类操作，要求页面等级 ≥ 2，而不是能源域可见即可。
            .RequireAuthorization(AuthorizationCatalog.PolicyNames.Configure)
            .WithName("UpsertEnergyMeter");

        group.MapDelete(
                "/meters/{id:guid}",
                async (Guid id, IParkEnergyQueryService service, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var deleted = await service.DeleteMeterAsync(id, cancellationToken);
                        return deleted ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                })
            .RequireAuthorization(AuthorizationCatalog.PolicyNames.Configure)
            .WithName("DeleteEnergyMeter");

        group.MapPost(
                "/org-units",
                async (EnergyOrgUnitUpsertRequest request, IParkEnergyQueryService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        EnergyOrgUnitDto saved = await service.UpsertOrgUnitAsync(request, cancellationToken);
                        return Results.Ok(saved);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                })
            .RequireAuthorization(AuthorizationCatalog.PolicyNames.Configure)
            .WithName("UpsertEnergyOrgUnit");

        group.MapPut(
                "/media/{id:guid}/factor",
                async (Guid id, EnergyMediumFactorRequest request, IParkEnergyQueryService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        EnergyMediumDto saved = await service.UpdateMediumFactorAsync(
                            id, request.CoalFactor, cancellationToken);
                        return Results.Ok(saved);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                })
            .RequireAuthorization(AuthorizationCatalog.PolicyNames.Configure)
            .WithName("UpdateEnergyMediumFactor");

        group.MapPost(
                "/readings",
                async (IngestEnergyReadingsRequest request, IParkEnergyQueryService service,
                    CancellationToken cancellationToken) =>
                {
                    if (request.Readings.Count == 0)
                    {
                        return Results.BadRequest(new { message = "读数列表为空。" });
                    }

                    try
                    {
                        IngestEnergyReadingsResultDto result =
                            await service.IngestReadingsAsync(request, cancellationToken);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                })
            // 写读数等于直接改报表：只要求能源域可见时，任何查看者都能伪造计量值，
            // 而这些值会直接进综合能耗与部门排名。正式环境接入需要独立凭据（API Key）
            // 后再放开，当前先挂平台配置权限。
            .RequirePermission(AuthorizationCatalog.SystemManagePermission)
            .WithName("IngestParkEnergyReadings");

        return endpoints;
    }
}

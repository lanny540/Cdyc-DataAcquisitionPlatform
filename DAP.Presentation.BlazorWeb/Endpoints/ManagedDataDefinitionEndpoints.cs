using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供后台数据定义相关 API 端点映射。
/// </summary>
public static class ManagedDataDefinitionEndpoints
{
    /// <summary>
    /// 映射后台数据定义相关 API 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapManagedDataDefinitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/managed-data-definitions").WithTags("ManagedDataDefinitions");

        group.MapGet(
                "/",
                async (IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
                {
                    IReadOnlyCollection<ManagedDataDefinitionDto> items =
                        await platformService.GetManagedDataDefinitionsAsync(cancellationToken);
                    return Results.Ok(items);
                })
            .WithName("GetManagedDataDefinitions");

        group.MapGet(
                "/{id:guid}",
                async (Guid id, int? historyLimit, IDataAcquisitionPlatformService platformService,
                    CancellationToken cancellationToken) =>
                {
                    ManagedDataDefinitionDetailsDto? details =
                        await platformService.GetManagedDataDefinitionDetailsAsync(id, historyLimit ?? 50,
                            cancellationToken);
                    return details is null ? Results.NotFound() : Results.Ok(details);
                })
            .WithName("GetManagedDataDefinitionDetails");

        group.MapPost(
                "/",
                async (ManagedDataDefinitionUpsertRequest request, IDataAcquisitionPlatformService platformService,
                    CancellationToken cancellationToken) =>
                {
                    IResult? validationError = ValidateManagedDataDefinitionRequest(request);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    ManagedDataDefinitionDto savedDefinition =
                        await platformService.UpsertManagedDataDefinitionAsync(request, cancellationToken);
                    return Results.Ok(savedDefinition);
                })
            .WithName("UpsertManagedDataDefinition");

        group.MapDelete(
                "/{id:guid}",
                async (Guid id, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
                {
                    bool deleted = await platformService.DeleteManagedDataDefinitionAsync(id, cancellationToken);
                    return deleted ? Results.NoContent() : Results.NotFound();
                })
            .WithName("DeleteManagedDataDefinition");

        return endpoints;
    }

    private static IResult? ValidateManagedDataDefinitionRequest(ManagedDataDefinitionUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.AcquisitionType) ||
            string.IsNullOrWhiteSpace(request.ConnectionAddress) ||
            string.IsNullOrWhiteSpace(request.Identifier) ||
            string.IsNullOrWhiteSpace(request.Department) ||
            string.IsNullOrWhiteSpace(request.ProcessCode) ||
            string.IsNullOrWhiteSpace(request.DataCategory) ||
            request.CollectionIntervalSeconds <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ManagedDataDefinition"] = ["编码、名称、采集方式、连接地址、数据标识、部门、工序和数据类别不能为空，且采集频次必须大于 0 秒。"]
            });
        }

        return null;
    }
}

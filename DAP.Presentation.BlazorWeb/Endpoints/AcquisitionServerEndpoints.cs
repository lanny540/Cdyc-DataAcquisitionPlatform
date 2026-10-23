using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供采集服务器配置相关 API 端点映射。
/// </summary>
public static class AcquisitionServerEndpoints
{
    /// <summary>
    /// 映射采集服务器配置相关 API 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapAcquisitionServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/acquisition-servers")
            .WithTags("AcquisitionServers");

        group.MapGet("/", async (IAcquisitionServerService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetServersAsync(cancellationToken)))
            .WithName("GetAcquisitionServers");

        group.MapGet("/types", () => Results.Ok(AcquisitionServerCatalog.Descriptors))
            .WithName("GetAcquisitionServerTypes");

        group.MapGet("/{id:guid}", async (Guid id, IAcquisitionServerService service,
                CancellationToken cancellationToken) =>
            {
                AcquisitionServerDto? server = await service.GetServerAsync(id, cancellationToken);
                return server is null ? Results.NotFound() : Results.Ok(server);
            })
            .WithName("GetAcquisitionServer");

        group.MapPost("/", async (AcquisitionServerUpsertRequest request, IAcquisitionServerService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.UpsertServerAsync(request, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new {message = ex.Message});
                }
            })
            .WithName("UpsertAcquisitionServer");

        group.MapPost("/{id:guid}/test", async (Guid id, AcquisitionServerProbeRequest request,
                IAcquisitionServerService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    AcquisitionServerProbeResultDto result = await service.TestServerAsync(
                        request.ToUpsertRequest(id), request.Identifier, request.RequestPath,
                        request.NodeConfiguration, cancellationToken);

                    return Results.Ok(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 上游不可达（超时 / 拒绝连接）不应返回 500，改为带回失败原因的探测结果。
                    // ErrorMessage 不进英文框架文案：原始消息只写日志，界面提示统一中文。
                    return Results.Ok(new AcquisitionServerProbeResultDto(
                        false,
                        "网络探测",
                        string.Empty,
                        0,
                        "探测失败，请查看服务端日志。",
                        string.Empty,
                        null,
                        0,
                        DateTimeOffset.UtcNow,
                        null));
                }
            })
            .WithName("TestAcquisitionServer");

        group.MapPost("/test", async (AcquisitionServerProbeRequest request,
                IAcquisitionServerService service, CancellationToken cancellationToken) =>
            {
                // 草稿测试：使用空标识跳过掩码还原，仅按提交的配置探测。
                try
                {
                    AcquisitionServerProbeResultDto result = await service.TestServerAsync(
                        request.ToUpsertRequest(null), request.Identifier, request.RequestPath,
                        request.NodeConfiguration, cancellationToken);

                    return Results.Ok(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 同按 ID 探测：失败原因不外泄英文框架文案与内网地址。
                    return Results.Ok(new AcquisitionServerProbeResultDto(
                        false,
                        "网络探测",
                        string.Empty,
                        0,
                        "探测失败，请查看服务端日志。",
                        string.Empty,
                        null,
                        0,
                        DateTimeOffset.UtcNow,
                        null));
                }
            })
            .WithName("TestAcquisitionServerDraft");

        group.MapDelete("/{id:guid}", async (Guid id, IAcquisitionServerService service,
                CancellationToken cancellationToken) =>
            {
                var (succeeded, errorMessage) =
                    await service.DeleteServerAsync(id, cancellationToken);

                return succeeded
                    ? Results.NoContent()
                    : Results.BadRequest(new {message = errorMessage ?? "删除采集服务器失败。"});
            })
            .WithName("DeleteAcquisitionServer");

        return endpoints;
    }
}

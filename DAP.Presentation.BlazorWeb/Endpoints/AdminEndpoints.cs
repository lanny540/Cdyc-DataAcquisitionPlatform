using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供域与页面配置、部门与授权矩阵的管理端点（docs/AUTH_DESIGN.md 第 10 节 /api/admin/*）。
///
/// 策略按操作对象拆分：域与页面归 action.domain.manage，部门与授权矩阵归
/// action.department.manage——两者都是超管职责，拆开是为了将来把部门管理
/// 单独下放时不必改端点。
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// 映射管理端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder domainGroup = endpoints.MapGroup("/api/admin/domains")
            .WithTags("AdminDomains")
            .RequirePermission(Authorization_DomainManage);

        domainGroup.MapGet(
                "/",
                async (AdminDomainService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetDomainsAsync(cancellationToken)))
            .WithName("GetAdminDomains");

        domainGroup.MapPut(
                "/pages/{pageId:guid}",
                async (Guid pageId, AdminDomainPageUpdateRequest request, AdminDomainService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateDomainPageAsync(pageId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminDomainPage");

        domainGroup.MapPost(
                "/pages/{pageId:guid}/reset",
                async (Guid pageId, AdminDomainService service, CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.ResetDomainPageAsync(pageId, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("ResetAdminDomainPage");

        RouteGroupBuilder departmentGroup = endpoints.MapGroup("/api/admin/departments")
            .WithTags("AdminDepartments")
            .RequirePermission(Authorization_DepartmentManage);

        departmentGroup.MapGet(
                "/",
                async (AdminDomainService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetDepartmentsAsync(cancellationToken)))
            .WithName("GetAdminDepartments");

        departmentGroup.MapPost(
                "/",
                async (AdminDepartmentUpsertRequest request, AdminDomainService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.CreateDepartmentAsync(request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("CreateAdminDepartment");

        departmentGroup.MapPut(
                "/{departmentId:guid}",
                async (Guid departmentId, AdminDepartmentUpsertRequest request, AdminDomainService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateDepartmentAsync(departmentId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminDepartment");

        departmentGroup.MapPut(
                "/{departmentId:guid}/grants",
                async (Guid departmentId, AdminDepartmentGrantsRequest request, AdminDomainService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateDepartmentGrantsAsync(departmentId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminDepartmentGrants");

        return endpoints;
    }

    private const string Authorization_DomainManage = AuthorizationCatalog.DomainManagePermission;
    private const string Authorization_DepartmentManage = AuthorizationCatalog.DepartmentManagePermission;
}

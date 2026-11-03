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

        // 用户与角色：策略挂 action.user.manage。超级管理员在策略提供程序里短路，
        // 部门管理员靠该权限点进入，服务内再按部门收窄（docs/AUTH_DESIGN.md 9.3）。
        RouteGroupBuilder userGroup = endpoints.MapGroup("/api/admin/users")
            .WithTags("AdminUsers")
            .RequirePermission(Authorization_UserManage);

        userGroup.MapGet(
                "/",
                async (string? keyword, AdminUserService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetUsersAsync(keyword, cancellationToken)))
            .WithName("GetAdminUsers");

        userGroup.MapGet(
                "/roles",
                async (AdminUserService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.GetAssignableRolesAsync(cancellationToken)))
            .WithName("GetAdminAssignableRoles");

        userGroup.MapPost(
                "/",
                async (AdminUserCreateRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.CreateUserAsync(request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("CreateAdminUser");

        userGroup.MapPut(
                "/{userId:guid}",
                async (Guid userId, AdminUserUpdateRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateUserAsync(userId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminUser");

        userGroup.MapPut(
                "/{userId:guid}/department",
                async (Guid userId, AdminUserDepartmentRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateUserDepartmentAsync(userId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminUserDepartment");

        userGroup.MapPut(
                "/{userId:guid}/roles",
                async (Guid userId, AdminUserRolesRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.UpdateUserRolesAsync(userId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("UpdateAdminUserRoles");

        userGroup.MapPost(
                "/{userId:guid}/reset-password",
                async (Guid userId, AdminUserResetPasswordRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result =
                        await service.ResetPasswordAsync(userId, request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("ResetAdminUserPassword");

        userGroup.MapPost(
                "/batch-status",
                async (AdminUserBatchStatusRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.SetUsersEnabledAsync(request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("SetAdminUsersEnabled");

        userGroup.MapPost(
                "/handover",
                async (SuperAdminHandoverRequest request, AdminUserService service,
                    CancellationToken cancellationToken) =>
                {
                    AuthOperationResultDto result = await service.HandoverSuperAdminAsync(request, cancellationToken);
                    return result.Succeeded ? Results.NoContent() : Results.BadRequest(new {message = result.ErrorMessage});
                })
            .WithName("HandoverSuperAdmin");

        // 审计查询：只读端点，没有写端点。权限归 action.system.manage（平台运维范畴）。
        RouteGroupBuilder auditGroup = endpoints.MapGroup("/api/audit-logs")
            .WithTags("AuditLogs")
            .RequirePermission(Authorization_SystemManage);

        auditGroup.MapGet(
                "/",
                async (DateTimeOffset? from, DateTimeOffset? to, string? userName, string? targetType,
                    string? result, int? page, int? pageSize, AdminAuditService service,
                    CancellationToken cancellationToken) =>
                {
                    AuditLogFilter filter = new(
                        from, to, userName, targetType, result, page ?? 1, pageSize ?? 50);

                    return Results.Ok(await service.QueryAsync(filter, cancellationToken));
                })
            .WithName("GetAuditLogs");

        auditGroup.MapGet(
                "/export",
                async (DateTimeOffset? from, DateTimeOffset? to, string? userName, string? targetType,
                    string? result, AdminAuditService service, CancellationToken cancellationToken) =>
                {
                    AuditLogFilter filter = new(from, to, userName, targetType, result);

                    var csv = await service.ExportCsvAsync(filter, cancellationToken);
                    var fileName = $"audit-{DateTimeOffset.Now:yyyyMMddHHmmss}.csv";

                    return Results.File(
                        System.Text.Encoding.UTF8.GetBytes(csv),
                        "text/csv; charset=utf-8",
                        fileName);
                })
            .WithName("ExportAuditLogs");

        return endpoints;
    }

    private const string Authorization_DomainManage = AuthorizationCatalog.DomainManagePermission;
    private const string Authorization_DepartmentManage = AuthorizationCatalog.DepartmentManagePermission;
    private const string Authorization_UserManage = AuthorizationCatalog.UserManagePermission;
    private const string Authorization_SystemManage = AuthorizationCatalog.SystemManagePermission;
}

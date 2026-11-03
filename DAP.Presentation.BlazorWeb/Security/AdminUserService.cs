using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 用户与角色管理服务（docs/AUTH_DESIGN.md 6.4 / 9.3 / P2）。
///
/// 两类操作者共用同一套方法，靠服务内判定收窄范围：
/// 超级管理员可管全部账号；部门管理员只能管本部门（department_id 精确相等），
/// 且只能分配「普通用户 / 技术人员」两种角色，不能改部门、不能授予部门管理员及以上。
/// 超管账号受硬约束保护：不可停用、不可改部门、不可被他人重置密码、不可删除。
///
/// 角色与启停的变更**在下一次登录时生效**：授权判定只认 Cookie 里的 claims，
/// 会话内不热切换（与域授权变更的处理一致）。**停用与重置密码例外**——
/// 这两件事换掉安全戳，已签发的会话立即失效（docs/AUTH_DESIGN.md 9.5）。
/// </summary>
public sealed class AdminUserService(
    DataAcquisitionPlatformDbContext db,
    AuditLogWriter audit,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider,
    UserSessionValidator sessionValidator)
{
    /// <summary>
    /// 查询账号列表。超管返回全部，部门管理员只返回本部门。
    /// </summary>
    /// <param name="keyword">关键字，匹配账号或姓名，可空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号列表；无权限时为空集合。</returns>
    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? _, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);

        if (scopeDepartmentId is null && !await IsActorSuperAdminAsync(cancellationToken))
        {
            // 无用户管理权限，或部门管理员未归属部门：返回空列表而不是异常，
            // 端点策略已挡住无权请求，这里只处理「有权限但范围为空」的情形。
            return [];
        }

        IQueryable<AppUser> query = db.AppUsers
            .Include(user => user.Department)
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .AsQueryable();

        if (scopeDepartmentId is not null)
        {
            query = query.Where(user => user.DepartmentId == scopeDepartmentId);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(user =>
                EF.Functions.ILike(user.UserName, $"%{trimmed}%")
                || EF.Functions.ILike(user.DisplayName, $"%{trimmed}%"));
        }

        List<AppUser> users = await query
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();

        return
        [
            .. users.Select(user => new AdminUserDto(
                user.Id,
                user.UserName,
                user.DisplayName,
                user.DepartmentId,
                user.Department?.Name,
                user.Email,
                user.IsEnabled,
                user.MustChangePassword,
                user.LockoutEndAt is { } lockoutEnd && lockoutEnd > now,
                user.LastLoginAt,
                [.. user.UserRoles
                    .Where(link => link.Role is not null)
                    .Select(link => toRoleDto(link.Role!))
                    .OrderBy(role => role.Name, StringComparer.Ordinal)],
                user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole),
                user.DisabledSource))
        ];

        static AdminRoleDto toRoleDto(Role role) => new(role.Id, role.Code, role.Name, role.PageLevel, []);
    }

    /// <summary>
    /// 查询可分配的角色。超管见全部已启用角色（不含超管），部门管理员只见普通用户与技术人员。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>角色列表。</returns>
    public async Task<IReadOnlyList<AdminRoleDto>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, _) = await ResolveScopeAsync(cancellationToken);

        if (actor is null || !actor.HasPermission(AuthorizationCatalog.UserManagePermission))
        {
            return [];
        }

        List<Role> roles = await db.Roles
            .Where(role => role.IsEnabled && role.Code != AuthorizationCatalog.SuperAdminRole)
            .OrderBy(role => role.SortOrder)
            .ToListAsync(cancellationToken);

        if (!actor.IsSuperAdmin)
        {
            // 部门管理员只能授予「普通用户 / 技术人员」：授予部门管理员会形成自我提权链。
            roles = [.. roles.Where(role => role.Code is AuthorizationCatalog.ViewerRole or AuthorizationCatalog.TechnicianRole)];
        }

        List<RolePermission> links = await db.RolePermissions
            .Where(link => roles.Select(role => role.Id).Contains(link.RoleId))
            .ToListAsync(cancellationToken);

        List<Permission> permissions = await db.Permissions.ToListAsync(cancellationToken);
        Dictionary<Guid, string> permissionCodes = permissions.ToDictionary(p => p.Id, p => p.Code);

        return
        [
            .. roles.Select(role => new AdminRoleDto(
                role.Id,
                role.Code,
                role.Name,
                role.PageLevel,
                [.. links
                    .Where(link => link.RoleId == role.Id && permissionCodes.ContainsKey(link.PermissionId))
                    .Select(link => permissionCodes[link.PermissionId])]))
        ];
    }

    /// <summary>
    /// 新建账号。初始密码由管理员设置，账号首次登录强制改密。
    /// </summary>
    /// <param name="request">新建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> CreateUserAsync(
        AdminUserCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);
        var denied = await CheckManagerAsync(actor, cancellationToken);

        if (denied is not null)
        {
            return new AuthOperationResultDto(false, denied);
        }

        var userName = PlatformAuthService.NormalizeUserName(request.UserName);

        if (!AdminUserRules.IsValidUserName(userName))
        {
            return new AuthOperationResultDto(false, "账号需为 3–32 位小写字母、数字、点、下划线或连字符，且以字母或数字开头。");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return new AuthOperationResultDto(false, "请填写姓名。");
        }

        var passwordError = PlatformAuthService.ValidateNewPassword(userName, request.InitialPassword);

        if (passwordError is not null)
        {
            return new AuthOperationResultDto(false, passwordError);
        }

        var exists = await db.AppUsers.AnyAsync(user => user.UserName == userName, cancellationToken);

        if (exists)
        {
            return new AuthOperationResultDto(false, $"账号 {userName} 已存在。");
        }

        // 部门管理员只能把新账号建在本部门，且该部门必须已归属。
        Guid? departmentId = request.DepartmentId;

        if (!actor!.IsSuperAdmin)
        {
            if (scopeDepartmentId is null)
            {
                return await DenyAsync(actor, "user.create", null, userName,
                    "当前账号未归属部门，无法新建用户。", cancellationToken);
            }

            departmentId = scopeDepartmentId;
        }

        if (departmentId is { } id)
        {
            var departmentExists = await db.Departments.AnyAsync(item => item.Id == id, cancellationToken);

            if (!departmentExists)
            {
                return new AuthOperationResultDto(false, "所选部门不存在。");
            }
        }

        List<Role> roles = await ResolveRolesAsync(request.RoleIds, actor, cancellationToken);

        if (roles.Count != request.RoleIds.Distinct().Count())
        {
            return await DenyAsync(actor, "user.create", null, userName,
                "角色包含不存在、已停用或超出授权范围的角色。", cancellationToken);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        AppUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = request.DisplayName.Trim(),
            DepartmentId = departmentId,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            IsEnabled = true,
            MustChangePassword = true,
            PasswordHash = PasswordHasher.Hash(request.InitialPassword),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AppUsers.Add(user);

        foreach (Role role in roles)
        {
            db.UserRoles.Add(new UserRole {UserId = user.Id, RoleId = role.Id});
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.create", "user", user.Id.ToString(), user.UserName, true,
            new {user.UserName, user.DisplayName, departmentId, roles = roles.Select(role => role.Code).ToArray()},
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 修改账号基本信息（姓名、邮箱、启停）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="request">修改请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> UpdateUserAsync(
        Guid userId,
        AdminUserUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);
        var denied = await CheckManagerAsync(actor, cancellationToken);

        if (denied is not null)
        {
            return new AuthOperationResultDto(false, denied);
        }

        AppUser? user = await db.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return new AuthOperationResultDto(false, "账号不存在，请刷新后重试。");
        }

        var scopeError = CheckTargetInScope(actor!, scopeDepartmentId, user);

        if (scopeError is not null)
        {
            return await DenyAsync(actor, "user.update", user.Id.ToString(), user.UserName, scopeError,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return new AuthOperationResultDto(false, "请填写姓名。");
        }

        var isTargetSuperAdmin = user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole);

        // 超管账号保护：不可停用。启停开关在页面对超管隐藏，服务端再兜一层。
        if (isTargetSuperAdmin && !request.IsEnabled)
        {
            return await DenyAsync(actor, "user.update", user.Id.ToString(), user.UserName,
                "超级管理员账号不可停用。如需更换请使用「移交超管」。", cancellationToken);
        }

        var before = new {user.DisplayName, user.Email, user.IsEnabled};
        var changed = false;

        if (!string.Equals(user.DisplayName, request.DisplayName.Trim(), StringComparison.Ordinal))
        {
            user.DisplayName = request.DisplayName.Trim();
            changed = true;
        }

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (!string.Equals(user.Email, email, StringComparison.Ordinal))
        {
            user.Email = email;
            changed = true;
        }

        if (user.IsEnabled != request.IsEnabled)
        {
            user.IsEnabled = request.IsEnabled;
            // 停用来源必须跟着状态走：管理端停用一律标 manual，
            // 目录同步才不会在下一轮把这个人自动启用回来。
            user.DisabledSource = request.IsEnabled ? null : UserDisableSources.Manual;
            changed = true;

            // 停用即清空失败计数与锁定，避免启用后仍被剩余锁定期拦在门外。
            // 同时换安全戳：停用的目的就是「立刻不让这个人再操作」，
            // 不换戳的话他手上的会话还能继续用到绝对过期（最长 12 小时）。
            if (!request.IsEnabled)
            {
                user.AccessFailedCount = 0;
                user.LockoutEndAt = null;
                sessionValidator.RenewSecurityStamp(user);
            }
        }

        if (!changed)
        {
            return new AuthOperationResultDto(true, null);
        }

        user.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.update", "user", user.Id.ToString(), user.UserName, true,
            new {before, after = new {user.DisplayName, user.Email, user.IsEnabled}}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 调整账号所属部门。仅超级管理员可用——跨部门调动不能下放，
    /// 否则部门管理员可以把别部门的人拉进本部门再处置（docs/AUTH_DESIGN.md 9.3）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="request">部门请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> UpdateUserDepartmentAsync(
        Guid userId,
        AdminUserDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, _) = await ResolveScopeAsync(cancellationToken);

        if (actor is null || !actor.HasPermission(AuthorizationCatalog.UserManagePermission))
        {
            return new AuthOperationResultDto(false, "没有用户管理权限。");
        }

        if (!actor.IsSuperAdmin)
        {
            return await DenyAsync(actor, "user.department.update", userId.ToString(), null,
                "调整部门仅超级管理员可执行。", cancellationToken);
        }

        AppUser? user = await db.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return new AuthOperationResultDto(false, "账号不存在，请刷新后重试。");
        }

        if (user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole))
        {
            return await DenyAsync(actor, "user.department.update", user.Id.ToString(), user.UserName,
                "超级管理员账号不可调整部门。", cancellationToken);
        }

        if (request.DepartmentId is { } departmentId)
        {
            var exists = await db.Departments.AnyAsync(item => item.Id == departmentId, cancellationToken);

            if (!exists)
            {
                return new AuthOperationResultDto(false, "所选部门不存在。");
            }
        }

        if (user.DepartmentId == request.DepartmentId)
        {
            return new AuthOperationResultDto(true, null);
        }

        Guid? before = user.DepartmentId;
        user.DepartmentId = request.DepartmentId;
        user.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.department.update", "user", user.Id.ToString(), user.UserName, true,
            new {before, after = request.DepartmentId}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 全量设置账号角色。超级管理员角色不在此分配，只能走「移交超管」。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="request">角色请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> UpdateUserRolesAsync(
        Guid userId,
        AdminUserRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);
        var denied = await CheckManagerAsync(actor, cancellationToken);

        if (denied is not null)
        {
            return new AuthOperationResultDto(false, denied);
        }

        AppUser? user = await db.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return new AuthOperationResultDto(false, "账号不存在，请刷新后重试。");
        }

        var scopeError = CheckTargetInScope(actor!, scopeDepartmentId, user);

        if (scopeError is not null)
        {
            return await DenyAsync(actor, "user.roles.update", user.Id.ToString(), user.UserName, scopeError,
                cancellationToken);
        }

        if (user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole))
        {
            return await DenyAsync(actor, "user.roles.update", user.Id.ToString(), user.UserName,
                "超级管理员角色只能通过「移交超管」调整。", cancellationToken);
        }

        List<Role> roles = await ResolveRolesAsync(request.RoleIds, actor!, cancellationToken);

        if (roles.Count != request.RoleIds.Distinct().Count())
        {
            return await DenyAsync(actor, "user.roles.update", user.Id.ToString(), user.UserName,
                "角色包含不存在、已停用或超出授权范围的角色。", cancellationToken);
        }

        var before = user.UserRoles
            .Where(link => link.Role is not null)
            .Select(link => link.Role!.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var after = roles.Select(role => role.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();

        if (before.SequenceEqual(after))
        {
            return new AuthOperationResultDto(true, null);
        }

        List<UserRole> existingLinks = [.. user.UserRoles];
        db.UserRoles.RemoveRange(existingLinks);

        foreach (Role role in roles)
        {
            db.UserRoles.Add(new UserRole {UserId = user.Id, RoleId = role.Id});
        }

        user.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.roles.update", "user", user.Id.ToString(), user.UserName, true,
            new {before, after}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 重置账号密码。重置后账号被置为「首次登录强制改密」，并清空失败计数与锁定。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="request">重置请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> ResetPasswordAsync(
        Guid userId,
        AdminUserResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);
        var denied = await CheckManagerAsync(actor, cancellationToken);

        if (denied is not null)
        {
            return new AuthOperationResultDto(false, denied);
        }

        AppUser? user = await db.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return new AuthOperationResultDto(false, "账号不存在，请刷新后重试。");
        }

        var scopeError = CheckTargetInScope(actor!, scopeDepartmentId, user);

        if (scopeError is not null)
        {
            return await DenyAsync(actor, "user.password.reset", user.Id.ToString(), user.UserName, scopeError,
                cancellationToken);
        }

        if (user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole))
        {
            return await DenyAsync(actor, "user.password.reset", user.Id.ToString(), user.UserName,
                "超级管理员密码只能由本人修改。", cancellationToken);
        }

        var passwordError = PlatformAuthService.ValidateNewPassword(user.UserName, request.NewPassword);

        if (passwordError is not null)
        {
            return new AuthOperationResultDto(false, passwordError);
        }

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        // 重置密码通常意味着「这个账号可能被冒用过」，因此把已签发的会话一并作废。
        sessionValidator.RenewSecurityStamp(user);
        user.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.password.reset", "user", user.Id.ToString(), user.UserName, true,
            new {reason = "管理员重置密码，下次登录强制修改；已签发会话全部失效"}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 批量启用/停用账号。命中超管账号时整批拒绝——部分成功会让操作者误以为全部生效。
    /// </summary>
    /// <param name="request">批量请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> SetUsersEnabledAsync(
        AdminUserBatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, Guid? scopeDepartmentId) = await ResolveScopeAsync(cancellationToken);
        var denied = await CheckManagerAsync(actor, cancellationToken);

        if (denied is not null)
        {
            return new AuthOperationResultDto(false, denied);
        }

        if (request.UserIds.Count == 0)
        {
            return new AuthOperationResultDto(false, "请先选择账号。");
        }

        List<AppUser> users = await db.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .Where(item => request.UserIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (users.Count != request.UserIds.Distinct().Count())
        {
            return new AuthOperationResultDto(false, "部分账号不存在，请刷新后重试。");
        }

        foreach (AppUser user in users)
        {
            var scopeError = CheckTargetInScope(actor!, scopeDepartmentId, user);

            if (scopeError is not null)
            {
                return await DenyAsync(actor, "user.status.batch", user.Id.ToString(), user.UserName, scopeError,
                    cancellationToken);
            }

            if (!request.IsEnabled && user.UserRoles.Any(link => link.Role?.Code == AuthorizationCatalog.SuperAdminRole))
            {
                return await DenyAsync(actor, "user.status.batch", user.Id.ToString(), user.UserName,
                    "超级管理员账号不可停用。", cancellationToken);
            }
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<string> changed = [];

        foreach (AppUser user in users.Where(item => item.IsEnabled != request.IsEnabled))
        {
            user.IsEnabled = request.IsEnabled;
            user.DisabledSource = request.IsEnabled ? null : UserDisableSources.Manual;
            user.UpdatedAt = now;
            changed.Add(user.UserName);

            if (!request.IsEnabled)
            {
                user.AccessFailedCount = 0;
                user.LockoutEndAt = null;
                sessionValidator.RenewSecurityStamp(user);
            }
        }

        if (changed.Count == 0)
        {
            return new AuthOperationResultDto(true, null);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "user.status.batch", "user", null, $"{changed.Count} 个账号", true,
            new {isEnabled = request.IsEnabled, users = changed.ToArray()}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 移交超级管理员。同一事务内先降级原账号再升级目标账号，不出现「零超管」中间态；
    /// 完成后当前会话必须重新登录（claims 里仍是旧身份）。
    /// </summary>
    /// <param name="request">移交请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> HandoverSuperAdminAsync(
        SuperAdminHandoverRequest request,
        CancellationToken cancellationToken = default)
    {
        (CurrentUser? actor, _) = await ResolveScopeAsync(cancellationToken);

        if (actor?.UserId is null)
        {
            return new AuthOperationResultDto(false, "未登录或会话已过期，请重新登录。");
        }

        if (!actor.IsSuperAdmin)
        {
            return await DenyAsync(actor, "user.super_admin.handover", request.TargetUserId.ToString(), null,
                "只有超级管理员可以移交账号。", cancellationToken);
        }

        if (actor.UserId == request.TargetUserId)
        {
            return new AuthOperationResultDto(false, "接任账号不能是当前账号。");
        }

        Role superAdminRole = await db.Roles
            .FirstAsync(role => role.Code == AuthorizationCatalog.SuperAdminRole, cancellationToken);

        AppUser? target = await db.AppUsers
            .Include(user => user.UserRoles)
            .FirstOrDefaultAsync(user => user.Id == request.TargetUserId, cancellationToken);

        if (target is null)
        {
            return new AuthOperationResultDto(false, "接任账号不存在，请刷新后重试。");
        }

        if (!target.IsEnabled)
        {
            return new AuthOperationResultDto(false, "接任账号已停用，请先启用再移交。");
        }

        if (target.UserRoles.Any(link => link.RoleId == superAdminRole.Id))
        {
            return new AuthOperationResultDto(false, "该账号已经是超级管理员。");
        }

        UserRole? currentLink = await db.UserRoles.FirstOrDefaultAsync(
            link => link.UserId == actor.UserId && link.RoleId == superAdminRole.Id, cancellationToken);

        if (currentLink is null)
        {
            return new AuthOperationResultDto(false, "当前会话的超管身份与库中记录不一致，请重新登录后重试。");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        AppUser? actorUser = await db.AppUsers
            .FirstOrDefaultAsync(user => user.Id == actor.UserId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.UserRoles.Remove(currentLink);
        db.UserRoles.Add(new UserRole {UserId = target.Id, RoleId = superAdminRole.Id});
        target.UpdatedAt = now;

        // 两边都换安全戳：移交后原超管的会话必须立刻结束（否则旧会话里还带着超管 claim），
        // 接任者也要重新登录一次才能拿到超管权限——会话内不热切换是既定设计。
        if (actorUser is not null)
        {
            sessionValidator.RenewSecurityStamp(actorUser);
            actorUser.UpdatedAt = now;
        }

        sessionValidator.RenewSecurityStamp(target);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(actor, "user.super_admin.handover", "user", target.Id.ToString(), target.UserName, true,
            new {from = actor.UserName, to = target.UserName}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    private async Task<(CurrentUser? Actor, Guid? ScopeDepartmentId)> ResolveScopeAsync(
        CancellationToken cancellationToken)
    {
        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        if (actor?.UserId is null)
        {
            return (actor, null);
        }

        if (actor.IsSuperAdmin)
        {
            // 超管不受部门限制：范围用哨兵值表示「全部」，调用方据 actor.IsSuperAdmin 区分。
            return (actor, null);
        }

        Guid? departmentId = await db.AppUsers
            .Where(user => user.Id == actor.UserId)
            .Select(user => user.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        return (actor, departmentId);
    }

    private async Task<bool> IsActorSuperAdminAsync(CancellationToken cancellationToken)
    {
        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        return actor?.IsSuperAdmin == true;
    }

    private async Task<string?> CheckManagerAsync(CurrentUser? actor, CancellationToken cancellationToken)
    {
        if (actor?.UserId is null)
        {
            return "未登录或会话已过期，请重新登录。";
        }

        if (!actor.HasPermission(AuthorizationCatalog.UserManagePermission))
        {
            return "没有用户管理权限。";
        }

        if (actor.IsSuperAdmin)
        {
            return null;
        }

        var hasDepartment = await db.AppUsers
            .AnyAsync(user => user.Id == actor.UserId && user.DepartmentId != null, cancellationToken);

        return hasDepartment ? null : "当前账号未归属部门，无法管理用户。";
    }

    /// <summary>
    /// 校验目标账号是否在操作者范围内：超管不限，部门管理员只限本部门。
    /// 「本部门」= department_id 精确相等，不递归（组织模型无层级）。
    /// </summary>
    private static string? CheckTargetInScope(CurrentUser actor, Guid? scopeDepartmentId, AppUser target)
    {
        return AdminUserRules.DescribeScopeViolation(actor.IsSuperAdmin, scopeDepartmentId, target.DepartmentId);
    }

    /// <summary>
    /// 记录一次被拒绝的写操作，并返回失败结果。
    ///
    /// 授权类拒绝必须留痕：`AuditLogWriter` 的语义是「失败的操作也记录」，
    /// 否则连续尝试越权（跨部门改人、给自己提权）在审计页上完全看不到。
    /// 未登录时无法归到人，不落审计——此时端点策略已先一步拒绝。
    /// </summary>
    private async Task<AuthOperationResultDto> DenyAsync(
        CurrentUser? actor,
        string actionCode,
        string? targetId,
        string? targetName,
        string reason,
        CancellationToken cancellationToken)
    {
        if (actor?.UserId is not null)
        {
            await audit.RecordAsync(actor, actionCode, "user", targetId, targetName, false,
                new {reason}, cancellationToken);
        }

        return new AuthOperationResultDto(false, reason);
    }

    /// <summary>
    /// 解析并校验角色集合：角色必须存在、已启用，且不超出操作者的授予范围。
    /// </summary>
    private async Task<List<Role>> ResolveRolesAsync(
        IReadOnlyList<Guid> roleIds,
        CurrentUser actor,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. roleIds.Distinct()];

        if (ids.Count == 0)
        {
            return [];
        }

        List<Role> roles = await db.Roles
            .Where(role => ids.Contains(role.Id) && role.IsEnabled)
            .ToListAsync(cancellationToken);

        if (roles.Any(role => role.Code == AuthorizationCatalog.SuperAdminRole))
        {
            // 超管角色只能通过移交流程分配，任何直接分配请求都按越权处理。
            return [];
        }

        if (roles.Any(role => !AdminUserRules.IsAssignableRoleCode(actor.IsSuperAdmin, role.Code)))
        {
            return [];
        }

        return roles;
    }
}

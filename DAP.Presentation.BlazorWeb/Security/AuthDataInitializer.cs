using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 应用启动时准备认证所需的基础数据：内置角色与权限点、职能域、页面归属对账、超管种子。
///
/// 任何一步失败都会阻止应用启动。理由很直接：启用登录后如果角色或超管缺失，
/// 结果不是「功能少了一点」，而是所有人都进不去，且现场没有任何入口可以修。
/// </summary>
public sealed class AuthDataInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformAuthOptions> options,
    IHostEnvironment environment,
    ILogger<AuthDataInitializer> logger) : IHostedService
{
    /// <summary>
    /// appsettings.json 中公开的初始密码。生产环境禁止用它建超管。
    /// </summary>
    private const string KnownDefaultBootstrapPassword = "Admin@123456";

    private readonly PlatformAuthOptions _options = options.Value;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        DataAcquisitionPlatformDbContext db =
            scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();
        TimeProvider timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "认证初始化失败：无法连接 PostgreSQL。请检查 ConnectionStrings:PostgreSql 配置与数据库服务状态。");
            }

            await EnsureRolesAsync(db, timeProvider, cancellationToken);
            await ApplyDepartmentAdminSwitchAsync(db, timeProvider, cancellationToken);
            await EnsurePermissionsAsync(db, timeProvider, cancellationToken);
            await SyncRolePermissionsAsync(db, timeProvider, cancellationToken);
            await EnsureDomainsAsync(db, timeProvider, cancellationToken);

            DomainPageReconcileResult reconcile = await DomainPageReconciler.ReconcileAsync(
                db, timeProvider, logger, cancellationToken);

            await EnsureSuperAdminAsync(db, timeProvider, cancellationToken);
            await VerifySuperAdminUniquenessAsync(db, cancellationToken);
            await WarnOnEmptyAuthorizationAsync(db, cancellationToken);

            ValidateBoardOptions();
            ValidateExternalIdentityOptions();

            logger.LogInformation(
                "认证数据初始化完成：页面新增 {Created}、刷新 {Updated}、保留管理员改动 {Kept}、标记下线 {Orphaned}、跳过 {Skipped}。",
                reconcile.Created, reconcile.Updated, reconcile.Kept, reconcile.Orphaned, reconcile.Skipped);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "认证数据初始化失败。");
            throw new InvalidOperationException(
                "认证初始化失败，应用已停止启动。请确认数据库迁移已执行（dotnet ef database update），然后重新启动。",
                exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 空配置兜底告警（docs/AUTH_DESIGN.md 4.3）。
    ///
    /// 这两种情形都不会让超管进不去，所以不阻断启动；但现场表现是「除超管外全员登录后没东西可看」，
    /// 而日志里一句提示都没有时，排查会从「页面的问题」一路猜到「数据库连错了」。
    /// 启动时把成因写清楚，比事后加日志便宜得多。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WarnOnEmptyAuthorizationAsync(
        DataAcquisitionPlatformDbContext db,
        CancellationToken cancellationToken)
    {
        var domainCount = await db.BusinessDomains.CountAsync(cancellationToken);

        if (domainCount == 0)
        {
            logger.LogWarning(
                "职能域表为空：除超级管理员外，所有用户登录后将没有任何页面可看。请用超管登录后进入「域与页面配置」建域，并在「部门与授权」里授权。");
            return;
        }

        var departmentCount = await db.Departments.CountAsync(cancellationToken);

        if (departmentCount == 0)
        {
            return;
        }

        var grantedDepartmentCount = await db.DepartmentDomainGrants
            .Select(item => item.DepartmentId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (grantedDepartmentCount == 0)
        {
            logger.LogWarning(
                "已有 {DepartmentCount} 个部门，但没有任何部门获得职能域授权：非超管用户登录后会落在「无访问权限」页。请在「部门与授权」矩阵里勾选。",
                departmentCount);
        }
        else if (grantedDepartmentCount < departmentCount)
        {
            logger.LogInformation(
                "部门授权情况：{Granted}/{Total} 个部门已获得至少一个职能域。未授权的部门成员登录后会落在「无访问权限」页。",
                grantedDepartmentCount, departmentCount);
        }
    }

    /// <summary>
    /// 校验外部身份源配置（docs/AUTH_DESIGN.md 11）。启用但配置不完整时**拒绝启动**：
    /// 半配置的目录认证会让域账号登录失败，而失败现场看起来和「密码错了」一模一样，
    /// 没人会想到是配置少了一项。
    /// </summary>
    private void ValidateExternalIdentityOptions()
    {
        IReadOnlyList<string> ldapErrors = _options.Ldap.Validate();

        if (ldapErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"LDAP 配置不完整，应用已停止启动：{string.Join(" ", ldapErrors)}");
        }

        IReadOnlyList<string> oidcErrors = _options.Oidc.Validate();

        if (oidcErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"OIDC 配置不完整，应用已停止启动：{string.Join(" ", oidcErrors)}");
        }

        if (_options.Ldap.Enabled)
        {
            logger.LogInformation(
                "LDAP 已启用：{Host}:{Port}，BaseDn={BaseDn}，同步间隔 {Interval} 分钟。",
                _options.Ldap.Host,
                _options.Ldap.Port,
                _options.Ldap.BaseDn,
                _options.Ldap.SyncIntervalMinutes);
        }

        foreach (var provider in _options.Oidc.EnabledProviders())
        {
            logger.LogInformation(
                "OIDC 已启用：{Code}（{Authority}），回调 {CallbackPath}。",
                provider.Code,
                provider.Authority,
                provider.CallbackPath);
        }
    }

    /// <summary>
    /// 校验车间只读终端配置（docs/AUTH_DESIGN.md 6.6）：
    /// 启用但白名单为空说明配置没跟上，拒绝启动——静默跳过会让现场误以为免登录已生效；
    /// 宽于 /24 的条目输出告警——开成网段等于整个网段的人都能读到全部班组数据，且不会报错。
    /// </summary>
    private void ValidateBoardOptions()
    {
        if (!_options.Board.Enabled)
        {
            return;
        }

        if (_options.Board.IpWhitelist.Length == 0)
        {
            throw new InvalidOperationException(
                "Auth:Board:Enabled 已启用但 Auth:Board:IpWhitelist 为空。请登记车间那台机器的固定 IP（单机 /32），或暂时关闭开关。");
        }

        foreach (var entry in _options.Board.IpWhitelist)
        {
            if (BoardIpWhitelist.IsWideScope(entry))
            {
                logger.LogWarning(
                    "车间白名单条目 {Entry} 是宽网段（宽于 /24）。本方案不做行级过滤，等级 1 能看到全部班组的历史数据，开成网段等于整个网段都拿得到。生产环境只应有一条单机 /32。",
                    entry);
            }
        }
    }

    private async Task EnsureRolesAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        Dictionary<string, Role> roles = await db.Roles
            .ToDictionaryAsync(role => role.Code, StringComparer.Ordinal, cancellationToken);

        foreach (BuiltInRole builtIn in AuthorizationCatalog.Roles)
        {
            var isNew = !roles.TryGetValue(builtIn.Code, out Role? role);

            if (isNew)
            {
                role = new Role
                {
                    Id = Guid.NewGuid(),
                    Code = builtIn.Code,

                    // 启用状态只在新建时取代码默认值：部门管理员默认停用，
                    // 但管理员手工启用之后不能被下一次启动改回去。
                    IsEnabled = builtIn.IsEnabled,
                    CreatedAt = now
                };

                db.Roles.Add(role);
                roles[builtIn.Code] = role;
                logger.LogInformation("已创建内置角色 {RoleCode}（{RoleName}）。", builtIn.Code, builtIn.Name);
            }

            role!.Name = builtIn.Name;
            role.PageLevel = builtIn.PageLevel;
            role.IsBuiltin = builtIn.IsBuiltin;
            role.SortOrder = builtIn.SortOrder;
            role.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 应用部门管理员启用开关（docs/AUTH_DESIGN.md 9.2）。
    ///
    /// 只朝一个方向生效：配置为 true 时把内置角色翻到启用；配置为 false 时**不**把
    /// 已被手工启用的角色改回去——人工决定优先于默认值，否则一次重启就会让
    /// 已经依赖部门管理员的现场失去入口。
    /// </summary>
    private async Task ApplyDepartmentAdminSwitchAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!_options.DepartmentAdmin.Enabled)
        {
            return;
        }

        Role? role = await db.Roles.FirstOrDefaultAsync(
            item => item.Code == AuthorizationCatalog.DepartmentAdminRole, cancellationToken);

        if (role is null || role.IsEnabled)
        {
            return;
        }

        role.IsEnabled = true;
        role.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "部门管理员角色已按 Auth:DepartmentAdmin:Enabled 启用。可在用户管理页把该角色分配给部门负责人，其只能管理本部门账号。");
    }

    private async Task EnsurePermissionsAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        Dictionary<string, Permission> permissions = await db.Permissions
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        foreach (BuiltInPermission builtIn in AuthorizationCatalog.Permissions)
        {
            var isNew = !permissions.TryGetValue(builtIn.Code, out Permission? permission);

            if (isNew)
            {
                permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = builtIn.Code,
                    CreatedAt = now
                };

                db.Permissions.Add(permission);
                permissions[builtIn.Code] = permission;
            }

            permission!.Name = builtIn.Name;
            permission.Group = builtIn.Group;
            permission.SortOrder = builtIn.SortOrder;
            permission.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncRolePermissionsAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        Dictionary<string, Role> roles = await db.Roles
            .ToDictionaryAsync(role => role.Code, StringComparer.Ordinal, cancellationToken);

        Dictionary<string, Permission> permissions = await db.Permissions
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        List<RolePermission> existing = await db.RolePermissions.ToListAsync(cancellationToken);

        foreach (BuiltInRole builtIn in AuthorizationCatalog.Roles)
        {
            if (!roles.TryGetValue(builtIn.Code, out Role? role))
            {
                continue;
            }

            HashSet<Guid> desired = builtIn.PermissionCodes
                .Where(code => permissions.ContainsKey(code))
                .Select(code => permissions[code].Id)
                .ToHashSet();

            foreach (RolePermission link in existing.Where(link => link.RoleId == role.Id))
            {
                if (!desired.Contains(link.PermissionId))
                {
                    db.RolePermissions.Remove(link);
                }
            }

            foreach (Guid permissionId in desired)
            {
                if (existing.Any(link => link.RoleId == role.Id && link.PermissionId == permissionId))
                {
                    continue;
                }

                db.RolePermissions.Add(new RolePermission {RoleId = role.Id, PermissionId = permissionId});
            }

            role.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDomainsAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        HashSet<string> existingCodes = await db.BusinessDomains
            .Select(domain => domain.Code)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        foreach (BuiltInDomain builtIn in AuthorizationCatalog.Domains)
        {
            if (existingCodes.Contains(builtIn.Code))
            {
                continue;
            }

            db.BusinessDomains.Add(new BusinessDomain
            {
                Id = Guid.NewGuid(),
                Code = builtIn.Code,
                Name = builtIn.Name,
                SortOrder = builtIn.SortOrder,
                IsSystem = builtIn.IsSystem,
                IsEnabled = true,
                VisibleWhenEmpty = false,
                CreatedAt = now,
                UpdatedAt = now
            });

            logger.LogInformation("已创建职能域 {DomainCode}（{DomainName}）。", builtIn.Code, builtIn.Name);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSuperAdminAsync(
        DataAcquisitionPlatformDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Role superAdminRole = await db.Roles
            .FirstAsync(role => role.Code == AuthorizationCatalog.SuperAdminRole, cancellationToken);

        var hasSuperAdmin = await db.UserRoles
            .AnyAsync(link => link.RoleId == superAdminRole.Id && link.User!.IsEnabled, cancellationToken);

        if (hasSuperAdmin)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var userName = PlatformAuthService.NormalizeUserName(_options.BootstrapAdmin.UserName);
        var password = _options.BootstrapAdmin.Password;

        if (userName.Length == 0 || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "库中不存在启用状态的超级管理员，且未配置 Auth:BootstrapAdmin:UserName / Password，应用拒绝启动。"
                + "否则首次上线将无人能登录。请在 appsettings 中补上初始账号后重新启动。");
        }

        var passwordError = PlatformAuthService.ValidateNewPassword(userName, password);
        if (passwordError is not null)
        {
            throw new InvalidOperationException($"Auth:BootstrapAdmin:Password 不满足要求：{passwordError}");
        }

        // appsettings.json 里的初始密码是仓库公开的默认值，只能用于开发环境。
        // 生产环境若照抄这份配置建超管，等于把入口密码写进了代码库。
        if (environment.IsProduction()
            && string.Equals(password, KnownDefaultBootstrapPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "生产环境拒绝使用内置的默认初始密码创建超级管理员。"
                + "请通过环境变量 Auth__BootstrapAdmin__Password 或用户机密覆盖后再启动。");
        }

        AppUser? user = await db.AppUsers
            .FirstOrDefaultAsync(item => item.UserName == userName, cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                DisplayName = string.IsNullOrWhiteSpace(_options.BootstrapAdmin.DisplayName)
                    ? "超级管理员"
                    : _options.BootstrapAdmin.DisplayName.Trim(),
                IsEnabled = true,
                MustChangePassword = true,
                PasswordHash = PasswordHasher.Hash(password),
                CreatedAt = now,
                UpdatedAt = now
            };

            db.AppUsers.Add(user);
        }
        else
        {
            // 账号已存在但没有超管角色：补上角色并重置为初始密码，符合「超管必须存在」的硬约束。
            // 这是「零超管」的自救路径，只在启动自检发现无可用超管时才会走到。
            user.IsEnabled = true;
            user.DisabledSource = null;
            user.MustChangePassword = true;
            user.PasswordHash = PasswordHasher.Hash(password);
            user.AccessFailedCount = 0;
            user.LockoutEndAt = null;
            user.UpdatedAt = now;
        }

        db.UserRoles.Add(new UserRole {UserId = user.Id, RoleId = superAdminRole.Id});
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "已按配置创建初始超级管理员 {UserName}。初始密码来自 appsettings，首次登录将强制修改，请尽快完成改密。",
            userName);
    }

    private async Task VerifySuperAdminUniquenessAsync(
        DataAcquisitionPlatformDbContext db,
        CancellationToken cancellationToken)
    {
        var count = await db.UserRoles.CountAsync(
            link => link.Role!.Code == AuthorizationCatalog.SuperAdminRole && link.User!.IsEnabled,
            cancellationToken);

        if (count == 1)
        {
            return;
        }

        throw new InvalidOperationException(count == 0
            ? "启动自检失败：库中不存在启用状态的超级管理员，系统将无人可管理，已拒绝启动。"
            : $"启动自检失败：库中存在 {count} 个启用状态的超级管理员，违反「超管全局唯一」约束，已拒绝启动。"
              + "请只保留一个，其余通过「移交超管」流程处理。");
    }
}

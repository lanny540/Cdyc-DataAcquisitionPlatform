using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 域与页面配置、部门与授权矩阵的管理服务（docs/AUTH_DESIGN.md 第 4 节）。
///
/// 授权判定在端点策略（action.domain.manage / action.department.manage），
/// 本服务负责变更本身与审计留痕。页面归属改动会把 source 置为 override，
/// 启动对账不再覆盖管理员的调整；「恢复代码默认值」把 source 还原为 code。
/// </summary>
public sealed class AdminDomainService(
    DataAcquisitionPlatformDbContext db,
    AuditLogWriter audit,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 获取全部域与域内页面（含下线页面，供超管核对配置）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>域列表。</returns>
    public async Task<IReadOnlyList<AdminDomainDto>> GetDomainsAsync(CancellationToken cancellationToken = default)
    {
        List<BusinessDomain> domains = await db.BusinessDomains
            .OrderBy(domain => domain.SortOrder)
            .ToListAsync(cancellationToken);

        List<DomainPage> pages = await db.DomainPages
            .OrderBy(page => page.DomainId).ThenBy(page => page.SortOrder)
            .ToListAsync(cancellationToken);

        return domains
            .Select(domain => new AdminDomainDto(
                domain.Id,
                domain.Code,
                domain.Name,
                domain.SortOrder,
                domain.IsSystem,
                domain.IsEnabled,
                domain.VisibleWhenEmpty,
                [.. pages
                    .Where(page => page.DomainId == domain.Id)
                    .Select(page => new AdminDomainPageDto(
                        page.Id,
                        page.PageKey,
                        page.Title,
                        page.Route,
                        page.GroupTitle,
                        domain.Code,
                        page.AccessLevel,
                        page.SortOrder,
                        page.IsEnabled,
                        page.IsOrphaned,
                        page.Source))]))
            .ToList();
    }

    /// <summary>
    /// 新建职能域（docs/AUTH_DESIGN.md 4.2：新增域先在这里建，再让页面代码声明它的编码）。
    ///
    /// 没有这一步时「新增域」只能改代码发版，而文档写的是后台先建域——
    /// 两者不一致的结果是实施人员照文档操作却发现没有入口。
    /// </summary>
    /// <param name="request">新建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> CreateDomainAsync(
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = AdminDomainRules.NormalizeCode(request.Code);

        if (!AdminDomainRules.IsValidCode(code))
        {
            return new AuthOperationResultDto(false, "域编码只能用小写字母、数字与连字符，以字母开头，2–32 位。");
        }

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length is < 1 or > 32)
        {
            return new AuthOperationResultDto(false, "域名称不能为空，且不超过 32 个字。");
        }

        if (await db.BusinessDomains.AnyAsync(item => item.Code == code, cancellationToken))
        {
            return new AuthOperationResultDto(false, $"域编码 {code} 已存在，请换一个。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        BusinessDomain domain = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            SortOrder = request.SortOrder,
            IsSystem = request.IsSystem,
            IsEnabled = request.IsEnabled,
            VisibleWhenEmpty = request.VisibleWhenEmpty,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.BusinessDomains.Add(domain);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "domain.create", "domain", domain.Id.ToString(), domain.Name, true,
            new {domain.Code, domain.Name, domain.SortOrder, domain.IsSystem, domain.IsEnabled, domain.VisibleWhenEmpty},
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 修改域的显示名称、排序、启停、空域展示与系统域标记。域编码不允许改。
    /// </summary>
    /// <param name="domainId">域标识。</param>
    /// <param name="request">修改请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> UpdateDomainAsync(
        Guid domainId,
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        BusinessDomain? domain = await db.BusinessDomains
            .FirstOrDefaultAsync(item => item.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return new AuthOperationResultDto(false, "域不存在，请刷新后重试。");
        }

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length is < 1 or > 32)
        {
            return new AuthOperationResultDto(false, "域名称不能为空，且不超过 32 个字。");
        }

        var before = new
        {
            domain.Name,
            domain.SortOrder,
            domain.IsEnabled,
            domain.VisibleWhenEmpty,
            domain.IsSystem
        };

        var changed = false;

        if (!string.Equals(domain.Name, name, StringComparison.Ordinal))
        {
            domain.Name = name;
            changed = true;
        }

        if (domain.SortOrder != request.SortOrder)
        {
            domain.SortOrder = request.SortOrder;
            changed = true;
        }

        if (domain.IsEnabled != request.IsEnabled)
        {
            domain.IsEnabled = request.IsEnabled;
            changed = true;
        }

        if (domain.VisibleWhenEmpty != request.VisibleWhenEmpty)
        {
            domain.VisibleWhenEmpty = request.VisibleWhenEmpty;
            changed = true;
        }

        if (domain.IsSystem != request.IsSystem)
        {
            domain.IsSystem = request.IsSystem;
            changed = true;
        }

        if (!changed)
        {
            return new AuthOperationResultDto(true, null);
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        domain.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "domain.update", "domain", domain.Id.ToString(), domain.Name, true,
            new
            {
                before,
                after = new
                {
                    domain.Name,
                    domain.SortOrder,
                    domain.IsEnabled,
                    domain.VisibleWhenEmpty,
                    domain.IsSystem
                }
            },
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 删除域。仅当域下没有页面且不是系统域时允许（判定见 <see cref="AdminDomainRules"/>）。
    /// </summary>
    /// <param name="domainId">域标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> DeleteDomainAsync(
        Guid domainId,
        CancellationToken cancellationToken = default)
    {
        BusinessDomain? domain = await db.BusinessDomains
            .FirstOrDefaultAsync(item => item.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return new AuthOperationResultDto(false, "域不存在，请刷新后重试。");
        }

        var pageCount = await db.DomainPages.CountAsync(
            item => item.DomainId == domainId, cancellationToken);

        if (AdminDomainRules.DescribeDeleteViolation(pageCount, domain.IsSystem) is { } reason)
        {
            return new AuthOperationResultDto(false, reason);
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        db.BusinessDomains.Remove(domain);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "domain.delete", "domain", domain.Id.ToString(), domain.Name, true,
            new {domain.Code, domain.Name}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 删除已下线的页面记录。
    ///
    /// 只允许删除 <c>IsOrphaned</c> 的行：代码里仍存在的页面删了会在下次启动时被对账重新插入，
    /// 删除就变成一次无效操作，还会让人误以为「页面下线了」。
    /// </summary>
    /// <param name="pageId">页面标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> DeleteDomainPageAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        DomainPage? page = await db.DomainPages
            .FirstOrDefaultAsync(item => item.Id == pageId, cancellationToken);

        if (page is null)
        {
            return new AuthOperationResultDto(false, "页面不存在，请刷新后重试。");
        }

        if (!page.IsOrphaned)
        {
            return new AuthOperationResultDto(false, "代码中仍存在该页面，不能删除；如需隐藏请改用停用。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        db.DomainPages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "domain_page.delete", "domain-page", page.Id.ToString(), page.Title, true,
            new {page.PageKey, page.Route}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 修改页面归属配置（等级 / 归属域 / 启停 / 显示名 / 分组 / 排序）。
    /// 改动后 source = override，启动对账不再覆盖。
    /// </summary>
    /// <param name="pageId">页面标识。</param>
    /// <param name="request">修改请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> UpdateDomainPageAsync(
        Guid pageId,
        AdminDomainPageUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        DomainPage? page = await db.DomainPages.FirstOrDefaultAsync(
            item => item.Id == pageId, cancellationToken);

        if (page is null)
        {
            return new AuthOperationResultDto(false, "页面不存在，可能已被下线，请刷新后重试。");
        }

        if (page.IsOrphaned)
        {
            return new AuthOperationResultDto(false, "该页面已在代码中下线，无法修改配置。");
        }

        if (request.AccessLevel is { } level
            && level is not (AuthorizationCatalog.ViewAccessLevel or AuthorizationCatalog.ConfigureAccessLevel))
        {
            return new AuthOperationResultDto(false, "页面等级只支持 1（查看）或 2（配置）。");
        }

        if (request.DomainId is { } domainId)
        {
            BusinessDomain? domain = await db.BusinessDomains.FirstOrDefaultAsync(
                item => item.Id == domainId, cancellationToken);

            if (domain is null)
            {
                return new AuthOperationResultDto(false, "目标域不存在。");
            }
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        // detail 只记发生变化的字段（docs/AUTH_DESIGN.md 8.4）。
        var before = new
        {
            page.AccessLevel,
            DomainId = page.DomainId,
            page.IsEnabled,
            page.Title,
            page.GroupTitle,
            page.SortOrder
        };
        var changed = false;

        if (request.AccessLevel is { } newLevel && newLevel != page.AccessLevel)
        {
            page.AccessLevel = newLevel;
            changed = true;
        }

        if (request.DomainId is { } newDomainId && newDomainId != page.DomainId)
        {
            page.DomainId = newDomainId;
            changed = true;
        }

        if (request.IsEnabled is { } enabled && enabled != page.IsEnabled)
        {
            page.IsEnabled = enabled;
            changed = true;
        }

        if (request.Title is { } rawTitle)
        {
            var title = rawTitle.Trim();

            if (title.Length is < 1 or > 64)
            {
                return new AuthOperationResultDto(false, "页面名称不能为空，且不超过 64 个字。");
            }

            if (!string.Equals(page.Title, title, StringComparison.Ordinal))
            {
                page.Title = title;
                changed = true;
            }
        }

        // 空字符串是「改为直接挂在域下」的显式指令，null 才表示不改——
        // 否则清空分组标题这个操作永远发不出去。
        if (request.GroupTitle is { } rawGroupTitle)
        {
            var groupTitle = rawGroupTitle.Trim();

            if (groupTitle.Length > 32)
            {
                return new AuthOperationResultDto(false, "分组标题不超过 32 个字。");
            }

            var normalized = groupTitle.Length == 0 ? null : groupTitle;

            if (!string.Equals(page.GroupTitle, normalized, StringComparison.Ordinal))
            {
                page.GroupTitle = normalized;
                changed = true;
            }
        }

        if (request.SortOrder is { } sortOrder && sortOrder != page.SortOrder)
        {
            page.SortOrder = sortOrder;
            changed = true;
        }

        if (!changed)
        {
            return new AuthOperationResultDto(true, null);
        }

        page.Source = AuthorizationCatalog.PageSourceOverride;
        page.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "domain_page.update", "domain-page", page.Id.ToString(), page.Title, true,
            new
            {
                before,
                after = new
                {
                    page.AccessLevel,
                    DomainId = page.DomainId,
                    page.IsEnabled,
                    page.Title,
                    page.GroupTitle,
                    page.SortOrder
                }
            },
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 把页面配置恢复为代码默认值（source 还原为 code，下一次启动对账即按代码刷新）。
    /// </summary>
    /// <param name="pageId">页面标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> ResetDomainPageAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        DomainPage? page = await db.DomainPages.FirstOrDefaultAsync(
            item => item.Id == pageId, cancellationToken);

        if (page is null)
        {
            return new AuthOperationResultDto(false, "页面不存在，可能已被下线，请刷新后重试。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        page.Source = AuthorizationCatalog.PageSourceCode;
        page.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "domain_page.reset", "domain-page", page.Id.ToString(), page.Title, true,
            new {reason = "恢复代码默认值，下一次启动对账生效"}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 获取部门列表（含账号数与已授权域）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>部门列表。</returns>
    public async Task<IReadOnlyList<AdminDepartmentDto>> GetDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        List<Department> departments = await db.Departments
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

        List<Guid> domainIds = await db.BusinessDomains
            .Where(domain => !domain.IsSystem)
            .Select(domain => domain.Id)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> userCounts = await db.AppUsers
            .Where(user => user.DepartmentId != null)
            .GroupBy(user => user.DepartmentId!.Value)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);

        Dictionary<Guid, List<Guid>> grantsByDepartment = (await db.DepartmentDomainGrants
                .Where(grant => domainIds.Contains(grant.DomainId))
                .ToListAsync(cancellationToken))
            .GroupBy(grant => grant.DepartmentId)
            .ToDictionary(group => group.Key, group => group.Select(grant => grant.DomainId).ToList());

        return departments
            .Select(department => new AdminDepartmentDto(
                department.Id,
                department.Code,
                department.Name,
                department.ExternalCode,
                department.IsEnabled,
                userCounts.GetValueOrDefault(department.Id),
                grantsByDepartment.GetValueOrDefault(department.Id, [])))
            .ToList();
    }

    /// <summary>
    /// 新建部门。部门之间平级、无层级（刻意不设 parent_id，见 docs/AUTH_DESIGN.md 8.1）。
    /// </summary>
    /// <param name="request">部门请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> CreateDepartmentAsync(
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateDepartment(request);

        if (validation is not null)
        {
            return new AuthOperationResultDto(false, validation);
        }

        var code = request.Code.Trim();

        bool exists = await db.Departments.AnyAsync(
            item => item.Code == code, cancellationToken);

        if (exists)
        {
            return new AuthOperationResultDto(false, $"部门编码 {code} 已存在。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Department department = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            ExternalCode = string.IsNullOrWhiteSpace(request.ExternalCode) ? null : request.ExternalCode.Trim(),
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "department.create", "department", department.Id.ToString(), department.Name,
            true, new {department.Code, department.Name, department.IsEnabled}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 修改部门（名称、外部编码、启停）。编码不可改——它已被授权数据引用。
    /// </summary>
    /// <param name="departmentId">部门标识。</param>
    /// <param name="request">部门请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> UpdateDepartmentAsync(
        Guid departmentId,
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(
            item => item.Id == departmentId, cancellationToken);

        if (department is null)
        {
            return new AuthOperationResultDto(false, "部门不存在，请刷新后重试。");
        }

        var validation = ValidateDepartment(request);

        if (validation is not null)
        {
            return new AuthOperationResultDto(false, validation);
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        var before = new {department.Name, department.ExternalCode, department.IsEnabled};
        var changed = false;

        if (!string.Equals(department.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            department.Name = request.Name.Trim();
            changed = true;
        }

        var externalCode = string.IsNullOrWhiteSpace(request.ExternalCode) ? null : request.ExternalCode.Trim();

        if (!string.Equals(department.ExternalCode, externalCode, StringComparison.Ordinal))
        {
            department.ExternalCode = externalCode;
            changed = true;
        }

        if (department.IsEnabled != request.IsEnabled)
        {
            department.IsEnabled = request.IsEnabled;
            changed = true;
        }

        if (!changed)
        {
            return new AuthOperationResultDto(true, null);
        }

        department.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "department.update", "department", department.Id.ToString(), department.Name,
            true, new {before, after = new {department.Name, department.ExternalCode, department.IsEnabled}},
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 全量设置部门的域授权（列表外移除）。系统域不允许进入授权矩阵。
    /// </summary>
    /// <param name="departmentId">部门标识。</param>
    /// <param name="request">授权请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> UpdateDepartmentGrantsAsync(
        Guid departmentId,
        AdminDepartmentGrantsRequest request,
        CancellationToken cancellationToken = default)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(
            item => item.Id == departmentId, cancellationToken);

        if (department is null)
        {
            return new AuthOperationResultDto(false, "部门不存在，请刷新后重试。");
        }

        List<BusinessDomain> businessDomains = await db.BusinessDomains
            .Where(domain => !domain.IsSystem)
            .ToListAsync(cancellationToken);

        // 系统域与不存在的域一律拒绝，而不是静默忽略——静默忽略会掩盖配置错误。
        var invalid = request.DomainIds
            .Distinct()
            .Where(id => businessDomains.All(domain => domain.Id != id))
            .ToList();

        if (invalid.Count > 0)
        {
            return new AuthOperationResultDto(false, "授权目标包含系统域或不存在的域。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        List<DepartmentDomainGrant> existing = await db.DepartmentDomainGrants
            .Where(grant => grant.DepartmentId == departmentId)
            .ToListAsync(cancellationToken);

        var before = existing.Select(grant => grant.DomainId).OrderBy(id => id).ToList();
        var after = request.DomainIds.Distinct().OrderBy(id => id).ToList();

        if (before.SequenceEqual(after))
        {
            return new AuthOperationResultDto(true, null);
        }

        db.DepartmentDomainGrants.RemoveRange(existing.Where(grant => !after.Contains(grant.DomainId)));

        foreach (Guid domainId in after.Where(id => existing.All(grant => grant.DomainId != id)))
        {
            db.DepartmentDomainGrants.Add(new DepartmentDomainGrant
            {
                DepartmentId = departmentId,
                DomainId = domainId,
                GrantedAt = now,
                GrantedBy = actor?.UserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(actor, "department.grants.update", "department", department.Id.ToString(),
            department.Name, true,
            new {before, after}, cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    private static string? ValidateDepartment(AdminDepartmentUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return "请填写部门编码。";
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "请填写部门名称。";
        }

        return null;
    }
}

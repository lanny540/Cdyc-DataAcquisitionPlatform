using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Security.Directory;

/// <summary>
/// 目录同步（docs/AUTH_DESIGN.md 11.1、11.3）。
///
/// 两条铁律在这里落地：
/// <list type="number">
/// <item>**幂等 upsert、只改可同步字段**：姓名、邮箱、部门、启停。角色一行都不碰——
/// 每次同步都覆盖角色，等于把管理员手工配的权限每小时冲一次。</item>
/// <item>**账号保留优先于删除**：目录里消失的账号只置 <c>is_enabled = false</c>，不物理删除，
/// 否则历史审计里的账号名会变成悬空引用。</item>
/// </list>
///
/// 另有一条防护：**目录返回 0 个账号时整轮作废**。目录挂了、BindDn 权限被回收、
/// 同步过滤器写错，表现都是「返回 0 个」，如果照常执行，下一步就是把全平台账号停用。
/// </summary>
public sealed class DirectorySyncService(
    DataAcquisitionPlatformDbContext db,
    ILdapDirectory directory,
    IOptions<PlatformAuthOptions> options,
    TimeProvider timeProvider,
    AuditLogWriter auditWriter,
    PlatformAdvisoryLock advisoryLock,
    UserSessionValidator sessionValidator,
    ILogger<DirectorySyncService> logger)
{
    /// <summary>
    /// 审计动作编码。
    /// </summary>
    public const string SyncActionCode = "directory.sync";

    /// <summary>
    /// 待处理项处理完成的审计动作编码。
    /// </summary>
    public const string ResolveIssueActionCode = "directory.issue.resolve";

    /// <summary>
    /// LDAP 身份提供方标识。
    /// </summary>
    public const string LdapProvider = "ldap";

    private readonly PlatformAuthOptions _options = options.Value;

    /// <summary>
    /// 执行一次全量同步。
    /// </summary>
    /// <param name="actor">触发者；定时任务触发时为 null。</param>
    /// <param name="trigger">触发来源，写入审计：schedule 或 manual。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    public async Task<DirectorySyncRunDto> RunAsync(
        CurrentUser? actor,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        LdapOptions ldap = _options.Ldap;
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        if (!ldap.Enabled)
        {
            return new DirectorySyncRunDto(false, "目录认证未启用，未执行同步。", 0, 0, 0, 0, 0);
        }

        // 跨实例互斥：定时任务与手工触发都可能同时发生（两台实例同一分钟各跑一次，
        // 或两名管理员同时点「立即同步」）。不互斥的后果是唯一索引冲突、审计记录翻倍。
        await using IAsyncDisposable? lockHandle =
            await advisoryLock.TryAcquireAsync("directory-sync", cancellationToken);

        if (lockHandle is null)
        {
            logger.LogInformation("目录同步跳过：另一实例（或另一请求）正在同步中（触发方式 {Trigger}）。", trigger);

            return new DirectorySyncRunDto(
                false,
                "已有同步任务正在执行，请稍后刷新查看结果。",
                0,
                0,
                0,
                0,
                0);
        }

        IReadOnlyList<LdapDirectoryUser> records;

        try
        {
            records = await directory.ListUsersAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "目录同步失败：读取目录账号时出错（触发方式 {Trigger}）。", trigger);

            await auditWriter.RecordAsync(
                actor,
                SyncActionCode,
                "directory",
                null,
                "LDAP 目录",
                false,
                new {trigger, stage = "fetch", summary = "无法连接目录服务，本次同步已取消，账号保持原状。"},
                cancellationToken);

            return new DirectorySyncRunDto(
                false,
                "无法连接目录服务，本次同步已取消，账号保持原状。",
                0,
                0,
                0,
                0,
                0);
        }

        if (records.Count == 0)
        {
            const string message = "目录未返回任何账号，已跳过停用阶段，账号保持原状。";

            logger.LogWarning("目录同步中止：{Message}（触发方式 {Trigger}）", message, trigger);

            await auditWriter.RecordAsync(
                actor,
                SyncActionCode,
                "directory",
                null,
                "LDAP 目录",
                false,
                new {trigger, stage = "fetch", summary = message},
                cancellationToken);

            return new DirectorySyncRunDto(false, message, 0, 0, 0, 0, 0);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        List<AppUser> users = await db.AppUsers
            .Include(user => user.ExternalIdentities)
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .ToListAsync(cancellationToken);

        Dictionary<string, AppUser> userByExternalId = BuildIdentityIndex(users);
        Dictionary<string, AppUser> userByUserName = users
            .GroupBy(user => user.UserName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        Dictionary<string, Guid> departmentByCode = await LoadDepartmentIndexAsync(cancellationToken);

        Role? defaultRole = await db.Roles
            .FirstOrDefaultAsync(role => role.Code == ldap.DefaultRoleCode, cancellationToken);

        if (defaultRole is null)
        {
            logger.LogWarning(
                "目录同步：默认角色 {RoleCode} 不存在，新建的账号将没有任何角色。",
                ldap.DefaultRoleCode);
        }

        IssueCollector issues = new();
        HashSet<string> seenExternalIds = new(StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var disabled = 0;
        var unchanged = 0;
        var keptManualDisable = 0;

        foreach (LdapDirectoryUser record in records)
        {
            seenExternalIds.Add(record.ExternalId);

            Guid? mappedDepartmentId = null;

            if (!string.IsNullOrWhiteSpace(record.DepartmentExternalCode))
            {
                if (departmentByCode.TryGetValue(record.DepartmentExternalCode, out Guid departmentId))
                {
                    mappedDepartmentId = departmentId;
                }
                else
                {
                    // 不自动建部门：目录里的脏数据会让组织树膨胀，且新部门没有任何域授权，
                    // 建出来只是把「账号看不到数据」从显式问题变成隐式问题。
                    issues.Add(DirectorySyncIssue.DepartmentUnmatched, record.DepartmentExternalCode, record.UserName);
                }
            }

            if (userByExternalId.TryGetValue(record.ExternalId, out AppUser? user))
            {
                ExternalIdentity? identity = user.ExternalIdentities
                    .FirstOrDefault(item => item.Provider == LdapProvider
                                            && string.Equals(item.ExternalId, record.ExternalId, StringComparison.Ordinal));

                DirectorySyncDecision decision = DirectorySyncPlanner.Decide(record, Snapshot(user), mappedDepartmentId);

                if (!decision.RequiresWrite)
                {
                    unchanged++;

                    if (decision.KeptManualDisable)
                    {
                        keptManualDisable++;

                        logger.LogInformation(
                            "目录同步：账号 {UserName} 由管理员手工停用，目录中仍存在，保持停用（需人工恢复）。",
                            user.UserName);
                    }

                    if (identity is not null)
                    {
                        identity.LastSyncedAt = now;
                    }

                    continue;
                }

                user.DisplayName = decision.DisplayName;
                user.Email = decision.Email;
                user.DepartmentId = decision.DepartmentId;
                user.IsEnabled = decision.IsEnabled;
                // 停用来源随状态改写：同步停用标记为 directory，重新启用则清空。
                // 不清空的话，下次目录再禁用此人时会被误判成手工停用而不再自动停用。
                user.DisabledSource = decision.IsEnabled ? null : UserDisableSources.Directory;
                user.UpdatedAt = now;

                if (identity is not null)
                {
                    identity.EmployeeNo = record.EmployeeNo ?? identity.EmployeeNo;
                    identity.LastSyncedAt = now;
                    identity.UpdatedAt = now;
                }

                if (decision.Action == DirectorySyncAction.Disable)
                {
                    // 目录停用同样要立刻踢掉会话：否则「人在目录里被停用了」这件事
                    // 要等最长 12 小时才反映到他那台已经登录的电脑上。
                    sessionValidator.RenewSecurityStamp(user);
                    disabled++;
                    logger.LogInformation("目录同步：账号 {UserName} 在目录中已停用，平台账号同步停用。", user.UserName);
                }
                else
                {
                    updated++;
                }

                continue;
            }

            var derivedUserName = DirectoryUserFactory.DeriveUserName(record);

            if (userByUserName.TryGetValue(derivedUserName, out AppUser? occupied)
                && !HasIdentity(occupied, LdapProvider, record.ExternalId))
            {
                // 同名但没绑定目录的本地账号：不能直接并号，否则域账号可能挂到本地管理员身上。
                issues.Add(DirectorySyncIssue.UserNameConflict, derivedUserName, record.UserName);
                continue;
            }

            AppUser createdUser = DirectoryUserFactory.CreateUser(record, mappedDepartmentId, now);
            db.AppUsers.Add(createdUser);

            ExternalIdentity createdIdentity =
                DirectoryUserFactory.CreateIdentity(createdUser.Id, record, LdapProvider, now);
            db.ExternalIdentities.Add(createdIdentity);

            if (defaultRole is {IsEnabled: true})
            {
                db.UserRoles.Add(new UserRole {UserId = createdUser.Id, RoleId = defaultRole.Id});
            }

            userByExternalId[record.ExternalId] = createdUser;
            userByUserName[createdUser.UserName] = createdUser;
            created++;
        }

        foreach (AppUser user in users)
        {
            if (user.IsEnabled is false)
            {
                continue;
            }

            List<ExternalIdentity> identities = user.ExternalIdentities
                .Where(item => item.Provider == LdapProvider)
                .ToList();

            if (identities.Count == 0 || identities.Any(item => seenExternalIds.Contains(item.ExternalId)))
            {
                continue;
            }

            if (IsSuperAdmin(user))
            {
                logger.LogWarning(
                    "目录同步：超级管理员 {UserName} 已不在目录中，出于唯一性约束保持启用，请人工确认。",
                    user.UserName);
                continue;
            }

            user.IsEnabled = false;
            user.DisabledSource = UserDisableSources.Directory;
            sessionValidator.RenewSecurityStamp(user);
            user.UpdatedAt = now;
            disabled++;

            foreach (ExternalIdentity identity in identities)
            {
                identity.LastSyncedAt = now;
                identity.UpdatedAt = now;
            }

            logger.LogInformation("目录同步：账号 {UserName} 已不在目录中，平台账号标记停用（不删除）。", user.UserName);
        }

        int issueCount = await PersistIssuesAsync(issues, now, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // 与登录建号并发撞上唯一索引：整批回滚，下一轮重来即可。
            // 不在这里逐条重试——同步是幂等的，整轮重跑比半途修补更容易推理。
            logger.LogError(exception, "目录同步写库失败（可能与登录建号并发），本轮未生效，将在下一周期重试。");

            await auditWriter.RecordAsync(
                actor,
                SyncActionCode,
                "directory",
                null,
                "LDAP 目录",
                false,
                new {trigger, stage = "write", summary = "写库冲突，本轮未生效。"},
                cancellationToken);

            return new DirectorySyncRunDto(
                false,
                "同步过程中出现并发写入冲突，本轮未生效，请稍后重试。",
                created,
                updated,
                disabled,
                unchanged,
                issueCount,
                keptManualDisable);
        }

        var elapsed = timeProvider.GetUtcNow() - startedAt;

        var summary = keptManualDisable > 0
            ? $"同步完成：新建 {created}，更新 {updated}，停用 {disabled}，无变化 {unchanged}，"
              + $"手工停用保持 {keptManualDisable}，待处理 {issueCount}。"
            : $"同步完成：新建 {created}，更新 {updated}，停用 {disabled}，无变化 {unchanged}，待处理 {issueCount}。";

        logger.LogInformation(
            "目录同步完成（触发方式 {Trigger}）：新建 {Created}，更新 {Updated}，停用 {Disabled}，无变化 {Unchanged}，手工停用保持 {KeptManual}，待处理 {Issues}，耗时 {Elapsed}。",
            trigger,
            created,
            updated,
            disabled,
            unchanged,
            keptManualDisable,
            issueCount,
            elapsed);

        await auditWriter.RecordAsync(
            actor,
            SyncActionCode,
            "directory",
            null,
            "LDAP 目录",
            true,
            new
            {
                trigger,
                summary,
                created,
                updated,
                disabled,
                unchanged,
                keptManualDisable,
                issues = issueCount
            },
            cancellationToken);

        return new DirectorySyncRunDto(
            true, summary, created, updated, disabled, unchanged, issueCount, keptManualDisable);
    }

    /// <summary>
    /// 查询同步状态与未处理的待处理项。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步状态。</returns>
    public async Task<DirectorySyncStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        AuditLog? last = await db.AuditLogs
            .AsNoTracking()
            .Where(log => log.ActionCode == SyncActionCode)
            .OrderByDescending(log => log.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        List<DirectorySyncIssue> issues = await db.DirectorySyncIssues
            .AsNoTracking()
            .Where(issue => issue.Provider == LdapProvider && issue.ResolvedAt == null)
            .OrderByDescending(issue => issue.LastSeenAt)
            .ToListAsync(cancellationToken);

        return new DirectorySyncStatusDto(
            _options.Ldap.Enabled,
            last?.OccurredAt,
            last?.Result,
            ReadSummary(last?.Detail),
            issues.Select(ToDto).ToList());
    }

    /// <summary>
    /// 把一条待处理项标记为已处理。不删除记录：处理痕迹本身是运维证据。
    /// </summary>
    /// <param name="issueId">待处理项标识。</param>
    /// <param name="actor">操作者。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<AuthOperationResultDto> ResolveIssueAsync(
        Guid issueId,
        CurrentUser? actor,
        CancellationToken cancellationToken = default)
    {
        DirectorySyncIssue? issue = await db.DirectorySyncIssues
            .FirstOrDefaultAsync(item => item.Id == issueId, cancellationToken);

        if (issue is null)
        {
            return new AuthOperationResultDto(false, "待处理项不存在或已处理。");
        }

        if (issue.ResolvedAt is not null)
        {
            return new AuthOperationResultDto(true, null);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        issue.ResolvedAt = now;
        issue.ResolvedBy = actor?.UserId;
        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.RecordAsync(
            actor,
            ResolveIssueActionCode,
            "directory-issue",
            issue.Id.ToString(),
            issue.ExternalCode,
            true,
            new {issueType = issue.IssueType, externalCode = issue.ExternalCode},
            cancellationToken);

        return new AuthOperationResultDto(true, null);
    }

    private static DirectorySyncIssueDto ToDto(DirectorySyncIssue issue)
    {
        return new DirectorySyncIssueDto(
            issue.Id,
            issue.IssueType,
            issue.ExternalCode,
            issue.SampleUserName,
            issue.Occurrences,
            issue.FirstSeenAt,
            issue.LastSeenAt);
    }

    private static string? ReadSummary(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(detail);

            return document.RootElement.TryGetProperty("summary", out JsonElement value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DirectoryAccountSnapshot Snapshot(AppUser user)
    {
        return new DirectoryAccountSnapshot(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.DepartmentId,
            user.IsEnabled,
            IsSuperAdmin(user),
            user.DisabledSource);
    }

    private static bool IsSuperAdmin(AppUser user)
    {
        return user.UserRoles.Any(link =>
            string.Equals(link.Role?.Code, AuthorizationCatalog.SuperAdminRole, StringComparison.Ordinal));
    }

    private static bool HasIdentity(AppUser user, string provider, string externalId)
    {
        return user.ExternalIdentities.Any(item =>
            string.Equals(item.Provider, provider, StringComparison.Ordinal)
            && string.Equals(item.ExternalId, externalId, StringComparison.Ordinal));
    }

    private static Dictionary<string, AppUser> BuildIdentityIndex(IEnumerable<AppUser> users)
    {
        Dictionary<string, AppUser> index = new(StringComparer.Ordinal);

        foreach (AppUser user in users)
        {
            foreach (ExternalIdentity identity in user.ExternalIdentities)
            {
                if (string.Equals(identity.Provider, LdapProvider, StringComparison.Ordinal))
                {
                    index[identity.ExternalId] = user;
                }
            }
        }

        return index;
    }

    private async Task<Dictionary<string, Guid>> LoadDepartmentIndexAsync(CancellationToken cancellationToken)
    {
        List<Department> departments = await db.Departments.AsNoTracking().ToListAsync(cancellationToken);
        Dictionary<string, Guid> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (Department department in departments)
        {
            if (!string.IsNullOrWhiteSpace(department.ExternalCode))
            {
                index[department.ExternalCode.Trim()] = department.Id;
            }
        }

        return index;
    }

    private async Task<int> PersistIssuesAsync(IssueCollector collector, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (collector.Items.Count == 0)
        {
            return 0;
        }

        List<DirectorySyncIssue> pending = await db.DirectorySyncIssues
            .Where(issue => issue.Provider == LdapProvider && issue.ResolvedAt == null)
            .ToListAsync(cancellationToken);

        foreach (CollectedIssue item in collector.Items.Values)
        {
            DirectorySyncIssue? existing = pending.FirstOrDefault(issue =>
                string.Equals(issue.IssueType, item.IssueType, StringComparison.Ordinal)
                && string.Equals(issue.ExternalCode, item.ExternalCode, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                db.DirectorySyncIssues.Add(new DirectorySyncIssue
                {
                    Id = Guid.NewGuid(),
                    Provider = LdapProvider,
                    IssueType = item.IssueType,
                    ExternalCode = item.ExternalCode,
                    SampleUserName = item.SampleUserName,
                    Occurrences = 1,
                    FirstSeenAt = now,
                    LastSeenAt = now
                });

                continue;
            }

            existing.Occurrences++;
            existing.LastSeenAt = now;
            existing.SampleUserName = item.SampleUserName ?? existing.SampleUserName;
        }

        return collector.Items.Count;
    }

    /// <summary>
    /// 收集本轮命中的待处理项，按「类型 + 外部标识」去重。
    /// </summary>
    private sealed class IssueCollector
    {
        public Dictionary<string, CollectedIssue> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string issueType, string externalCode, string? sampleUserName)
        {
            var key = $"{issueType}|{externalCode}";

            if (Items.TryGetValue(key, out CollectedIssue? existing))
            {
                Items[key] = existing with {SampleUserName = sampleUserName ?? existing.SampleUserName};
                return;
            }

            Items[key] = new CollectedIssue(issueType, externalCode, sampleUserName);
        }
    }

    private sealed record CollectedIssue(string IssueType, string ExternalCode, string? SampleUserName);
}

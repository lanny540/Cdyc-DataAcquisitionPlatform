using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 能源配置写入统一在服务层校验会话、当前授权并记录审计。
/// </summary>
public sealed class AuditedParkEnergyQueryService(
    ParkEnergyQueryService inner,
    ICurrentUserContext userContext,
    IServiceScopeFactory scopeFactory) : IParkEnergyQueryService
{
    public Task<ParkEnergyOverviewDto> GetOverviewAsync(
        string period, bool demo = false, CancellationToken cancellationToken = default)
    {
        return inner.GetOverviewAsync(period, demo, cancellationToken);
    }

    public Task<EnergyDictionaryDto> GetDictionaryAsync(CancellationToken cancellationToken = default)
    {
        return inner.GetDictionaryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EnergyMeterDto>> GetMetersAsync(CancellationToken cancellationToken = default)
    {
        return inner.GetMetersAsync(cancellationToken);
    }

    public Task<EnergyMeterDto> UpsertMeterAsync(
        EnergyMeterUpsertRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync("energy.meter.upsert", "energy-meter", request.Id?.ToString(), request.Code,
            false, () => inner.UpsertMeterAsync(request, cancellationToken), cancellationToken);
    }

    public Task<bool> DeleteMeterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync("energy.meter.delete", "energy-meter", id.ToString(), null,
            false, () => inner.DeleteMeterAsync(id, cancellationToken), cancellationToken);
    }

    public Task<EnergyOrgUnitDto> UpsertOrgUnitAsync(
        EnergyOrgUnitUpsertRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync("energy.org.upsert", "energy-org", request.Id?.ToString(), request.Code,
            false, () => inner.UpsertOrgUnitAsync(request, cancellationToken), cancellationToken);
    }

    public Task<EnergyMediumDto> UpdateMediumFactorAsync(
        Guid mediumId, decimal? coalFactor, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync("energy.factor.update", "energy-medium", mediumId.ToString(), null,
            false, () => inner.UpdateMediumFactorAsync(mediumId, coalFactor, cancellationToken), cancellationToken);
    }

    public Task<IngestEnergyReadingsResultDto> IngestReadingsAsync(
        IngestEnergyReadingsRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync("energy.readings.ingest", "energy-readings", null, null,
            true, () => inner.IngestReadingsAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(
        string action,
        string targetType,
        string? targetId,
        string? targetName,
        bool requiresSystemPermission,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        CurrentUser? actor = await userContext.GetUserAsync(cancellationToken);
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AuditLogWriter writer = scope.ServiceProvider.GetRequiredService<AuditLogWriter>();
        DataAcquisitionPlatformDbContext db =
            scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();
        PlatformAuthService auth = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
        // 会话只做滑动续期（无绝对过期），写入授权以前置校验为准：
        // 安全戳、启用状态与权限逐项核对，任何一项不满足即拒绝并落审计。
        bool allowed = false;
        if (actor?.UserId is { } userId)
        {
            string? stamp = actor.Principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.SecurityStamp)?.Value;
            Guid? currentStamp = await db.AppUsers.AsNoTracking()
                .Where(user => user.Id == userId && user.IsEnabled)
                .Select(user => (Guid?)user.SecurityStamp)
                .FirstOrDefaultAsync(cancellationToken);
            AuthUserDto? current = await auth.GetUserAsync(userId, cancellationToken);

            allowed = currentStamp.HasValue
                      && string.Equals(currentStamp.Value.ToString(), stamp, StringComparison.OrdinalIgnoreCase)
                      && current is not null
                      && !current.MustChangePassword
                      && PlatformClaimsFactory.CanAccessDomain(actor.Principal, AuthorizationCatalog.EnergyDomain)
                      && (current.IsSuperAdmin || current.Domains.Contains(AuthorizationCatalog.EnergyDomain))
                      && (requiresSystemPermission
                          ? actor.HasPermission(AuthorizationCatalog.SystemManagePermission)
                            && (current.IsSuperAdmin ||
                                current.Permissions.Contains(AuthorizationCatalog.SystemManagePermission))
                          : PlatformClaimsFactory.CanConfigure(actor.Principal)
                            && current.PageLevel >= AuthorizationCatalog.ConfigureAccessLevel);
        }

        if (!allowed)
        {
            await writer.RecordAsync(actor, action, targetType, targetId, targetName, false,
                new { reason = "会话失效或缺少能源配置权限" }, cancellationToken);
            throw new ForbiddenAccessException("会话已失效或没有执行该操作的权限，请重新登录或联系管理员。");
        }

        try
        {
            T result = await operation();
            await writer.RecordAsync(actor, action, targetType, targetId, targetName,
                result is not bool flag || flag, null, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await writer.RecordAsync(actor, action, targetType, targetId, targetName, false,
                new { errorType = ex.GetType().Name }, cancellationToken);
            throw;
        }
    }
}

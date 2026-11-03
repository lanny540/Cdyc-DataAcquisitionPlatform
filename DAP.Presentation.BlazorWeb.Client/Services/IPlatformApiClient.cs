using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

public interface IPlatformApiClient
{
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default);

    Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default);

    Task<ManagedDataDefinitionDetailsDto> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default);

    Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ManagedDataExecutionResultDto> DebugReadManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ManagedDataExecutionResultDto> CollectManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServerConnectionStatusDto>> GetServerConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<ServerConnectionStatusDto> CheckServerConnectionStatusAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<HistoryApiQueryResponse> GetHistoryCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<HistoryApiQueryResponse> GetHistoryRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcquisitionServerTypeDescriptor>> GetAcquisitionServerTypesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcquisitionServerDto>> GetAcquisitionServersAsync(
        CancellationToken cancellationToken = default);

    Task<AcquisitionServerDto> UpsertAcquisitionServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAcquisitionServerAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AcquisitionServerProbeResultDto> TestAcquisitionServerAsync(
        Guid? id,
        AcquisitionServerProbeRequest request,
        CancellationToken cancellationToken = default);

    Task<ShiftScheduleStatusDto> GetShiftScheduleStatusAsync(CancellationToken cancellationToken = default);

    Task<ShiftScheduleValidationDto> GetShiftScheduleValidationAsync(
        CancellationToken cancellationToken = default);

    Task<ShiftScheduleImportResultDto> ImportShiftScheduleAsync(
        Stream excelStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<bool> ClearShiftSchedulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyShiftScheduleDto>> GetShiftScheduleCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default);

    Task<TeamShiftScheduleDto?> GetTeamShiftScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<EnergyDashboardDto> GetEnergyDashboardAsync(
        int trendDays = 7,
        string? teamCode = null,
        CancellationToken cancellationToken = default);

    Task<EnergyHistoryDto> GetEnergyHistoryAsync(
        string teamCode,
        string granularity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<EnergyComparisonDto> GetEnergyComparisonAsync(
        string granularity,
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminDomainDto>> GetAdminDomainsAsync(CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminDomainPageAsync(
        Guid pageId,
        AdminDomainPageUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ResetAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminDepartmentDto>> GetAdminDepartmentsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> CreateAdminDepartmentAsync(
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminDepartmentAsync(
        Guid departmentId,
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminDepartmentGrantsAsync(
        Guid departmentId,
        AdminDepartmentGrantsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询账号列表。超管返回全部，部门管理员只返回本部门。
    /// </summary>
    /// <param name="keyword">关键字，匹配账号或姓名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号列表。</returns>
    Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询当前操作者可分配的角色。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>角色列表。</returns>
    Task<IReadOnlyList<AdminRoleDto>> GetAdminRolesAsync(CancellationToken cancellationToken = default);

    Task<bool> CreateAdminUserAsync(
        AdminUserCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminUserAsync(
        Guid userId,
        AdminUserUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminUserDepartmentAsync(
        Guid userId,
        AdminUserDepartmentRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminUserRolesAsync(
        Guid userId,
        AdminUserRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ResetAdminUserPasswordAsync(
        Guid userId,
        AdminUserResetPasswordRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> SetAdminUsersEnabledAsync(
        AdminUserBatchStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> HandoverSuperAdminAsync(
        SuperAdminHandoverRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询审计日志。
    /// </summary>
    /// <param name="from">起始时间（含）。</param>
    /// <param name="to">截止时间（不含）。</param>
    /// <param name="userName">操作人账号。</param>
    /// <param name="targetType">对象类型。</param>
    /// <param name="result">结果：success / failed。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>一页审计记录。</returns>
    Task<AuditLogPageDto> GetAuditLogsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构造审计导出的下载地址。导出走浏览器直接下载，不经 WASM 中转，
    /// 避免把几万行 CSV 读进内存再拼接。
    /// </summary>
    /// <param name="from">起始时间（含）。</param>
    /// <param name="to">截止时间（不含）。</param>
    /// <param name="userName">操作人账号。</param>
    /// <param name="targetType">对象类型。</param>
    /// <param name="result">结果。</param>
    /// <returns>带查询串的相对地址。</returns>
    string BuildAuditExportUrl(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result);

    /// <summary>
    /// 查询目录同步状态与未处理的待处理项。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步状态。</returns>
    Task<DirectorySyncStatusDto> GetDirectorySyncStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 手工触发一次目录全量同步。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    Task<DirectorySyncRunDto> RunDirectorySyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一条目录同步待处理项标记为已处理。
    /// </summary>
    /// <param name="issueId">待处理项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 true。</returns>
    Task<bool> ResolveDirectorySyncIssueAsync(Guid issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询可用的外部登录方式（OIDC 提供方）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提供方列表；未配置时为空。</returns>
    Task<IReadOnlyList<OidcProviderDto>> GetOidcProvidersAsync(CancellationToken cancellationToken = default);
}

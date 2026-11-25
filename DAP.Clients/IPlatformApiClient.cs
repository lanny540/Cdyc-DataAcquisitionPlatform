using DAP.Core.Shared.Contracts;

namespace DAP.Clients;

/// <summary>
/// 平台业务接口的统一契约。
///
/// Web（Blazor WASM）与桌面（Avalonia）共用同一份实现，差异只在 <see cref="HttpClient"/>
/// 的基地址与凭据传递方式（浏览器自动带 Cookie，桌面端由 CookieContainer 持有）。
/// 原先该接口与实现放在 <c>DAP.Presentation.BlazorWeb.Client</c>，桌面端要调用就得
/// 反向引用展示层，因此抽到独立的 <c>DAP.Clients</c>。
/// </summary>
public interface IPlatformApiClient
{
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询最新采集记录。
    /// </summary>
    /// <param name="limit">返回记录数量，服务端上限 500。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按采集时间倒序的记录。</returns>
    Task<IReadOnlyList<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 查询能耗查看页使用的班组清单，不暴露排班导入元数据。
    /// </summary>
    Task<IReadOnlyList<ShiftTeamDto>> GetEnergyTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询只读排班日历使用的班组清单。
    /// </summary>
    Task<IReadOnlyList<ShiftTeamDto>> GetShiftScheduleTeamsAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 查询园区能源总览。
    /// </summary>
    /// <param name="period">统计期间，取值为 month 或 year。</param>
    /// <param name="demo">是否请求示例数据。仅在无实测数据的开发环境使用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>园区能源总览。</returns>
    Task<ParkEnergyOverviewDto> GetParkEnergyOverviewAsync(
        string period,
        bool demo = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询能源字典，供采集点打标时选择介质、用途与设备系统。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字典清单。</returns>
    Task<EnergyDictionaryDto> GetEnergyDictionaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询能源计量点台账。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>计量点清单。</returns>
    Task<IReadOnlyList<EnergyMeterDto>> GetEnergyMetersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建或更新能源计量点。
    /// </summary>
    /// <param name="request">计量点信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的计量点。</returns>
    Task<EnergyMeterDto> UpsertEnergyMeterAsync(
        EnergyMeterUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除能源计量点。
    /// </summary>
    /// <param name="id">计量点标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 true。</returns>
    Task<bool> DeleteEnergyMeterAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建或更新能源组织节点。
    /// </summary>
    /// <param name="request">节点信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的节点。</returns>
    Task<EnergyOrgUnitDto> UpsertEnergyOrgUnitAsync(
        EnergyOrgUnitUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改介质折标煤系数。
    /// </summary>
    /// <param name="mediumId">介质标识。</param>
    /// <param name="coalFactor">折标煤系数，传空表示清除。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的字典项。</returns>
    Task<EnergyMediumDto> UpdateEnergyMediumFactorAsync(
        Guid mediumId,
        decimal? coalFactor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminDomainDto>> GetAdminDomainsAsync(CancellationToken cancellationToken = default);

    Task<bool> CreateAdminDomainAsync(
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminDomainAsync(
        Guid domainId,
        AdminDomainUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAdminDomainAsync(Guid domainId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default);

    Task<bool> UpdateAdminDomainPageAsync(
        Guid pageId,
        AdminDomainPageUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ResetAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>获取启用中的大屏导航卡（根路由枢纽页渲染用），按显示顺序排列。</summary>
    Task<IReadOnlyList<ScreenHubCardDto>> GetScreenHubCardsAsync(CancellationToken cancellationToken = default);

    /// <summary>获取全部大屏导航卡（含停用，管理列表用）。</summary>
    Task<IReadOnlyList<ScreenHubCardDto>> GetScreenHubAdminCardsAsync(CancellationToken cancellationToken = default);

    /// <summary>新建大屏导航卡。</summary>
    Task<bool> CreateScreenHubCardAsync(
        ScreenHubCardUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>修改大屏导航卡。</summary>
    Task<bool> UpdateScreenHubCardAsync(
        Guid cardId,
        ScreenHubCardUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>删除大屏导航卡。</summary>
    Task<bool> DeleteScreenHubCardAsync(Guid cardId, CancellationToken cancellationToken = default);

    /// <summary>上移 / 下移大屏导航卡（direction：up / down）。</summary>
    Task<bool> MoveScreenHubCardAsync(Guid cardId, string direction, CancellationToken cancellationToken = default);

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

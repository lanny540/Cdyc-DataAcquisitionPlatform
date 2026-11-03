using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Client.Services;

public sealed class PlatformApiClient(HttpClient httpClient) : IPlatformApiClient
{
    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        var overview =
            await httpClient.GetFromJsonAsync<DashboardOverviewDto>("/api/dashboard/overview", cancellationToken);
        return overview ?? throw new InvalidOperationException("服务端未返回平台概览数据。");
    }

    public async Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        var points =
            await httpClient.GetFromJsonAsync<IReadOnlyList<CollectionPointDto>>("/api/collection-points",
                cancellationToken);
        return points ?? [];
    }

    public async Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/collection-points", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<CollectionPointDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("服务端未返回保存后的采集点数据。");
    }

    public async Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync($"/api/collection-points/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ManagedDataDefinitionDto>>("/api/managed-data-definitions",
                cancellationToken);
        return definitions ?? [];
    }

    public async Task<ManagedDataDefinitionDetailsDto> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        var details =
            await httpClient.GetFromJsonAsync<ManagedDataDefinitionDetailsDto>(
                $"/api/managed-data-definitions/{id}?historyLimit={Math.Max(1, historyLimit)}",
                cancellationToken);
        return details ?? throw new InvalidOperationException("服务端未返回后台数据节点详情。");
    }

    public async Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/managed-data-definitions", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<ManagedDataDefinitionDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回保存后的后台数据定义。");
    }

    public async Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync($"/api/managed-data-definitions/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task<ManagedDataExecutionResultDto> DebugReadManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync($"/api/managed-data-definitions/{id}/debug-read", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<ManagedDataExecutionResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回后台数据调试读取结果。");
    }

    public async Task<ManagedDataExecutionResultDto> CollectManagedDataDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync($"/api/managed-data-definitions/{id}/collect", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<ManagedDataExecutionResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回后台数据采集结果。");
    }

    public async Task<IReadOnlyList<ServerConnectionStatusDto>> GetServerConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var statuses =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ServerConnectionStatusDto>>(
                "/api/server-connections",
                cancellationToken);
        return statuses ?? [];
    }

    public async Task<ServerConnectionStatusDto> CheckServerConnectionStatusAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var status =
            await httpClient.GetFromJsonAsync<ServerConnectionStatusDto>(
                $"/api/server-connections/{Uri.EscapeDataString(key)}/status",
                cancellationToken);
        return status ?? throw new InvalidOperationException("服务端未返回服务器连接状态。");
    }

    public async Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/managed-data-definitions/test-connection", request,
                cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<ManagedDataConnectionTestResultDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回连接测试结果。");
    }

    public async Task<HistoryApiQueryResponse> GetHistoryCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/history-api/current-value", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<HistoryApiQueryResponse>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回 History 最新值测试结果。");
    }

    public async Task<HistoryApiQueryResponse> GetHistoryRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/history-api/raw-data", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<HistoryApiQueryResponse>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回 History 时间段数据测试结果。");
    }

    public async Task<IReadOnlyList<AcquisitionServerTypeDescriptor>> GetAcquisitionServerTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var types = await httpClient.GetFromJsonAsync<IReadOnlyList<AcquisitionServerTypeDescriptor>>(
            "/api/acquisition-servers/types", cancellationToken);
        return types ?? [];
    }

    public async Task<IReadOnlyList<AcquisitionServerDto>> GetAcquisitionServersAsync(
        CancellationToken cancellationToken = default)
    {
        var servers = await httpClient.GetFromJsonAsync<IReadOnlyList<AcquisitionServerDto>>(
            "/api/acquisition-servers", cancellationToken);
        return servers ?? [];
    }

    public async Task<AcquisitionServerDto> UpsertAcquisitionServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync("/api/acquisition-servers", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<AcquisitionServerDto>(cancellationToken) ??
               throw new InvalidOperationException("服务端未返回保存后的采集服务器配置。");
    }

    public async Task<bool> DeleteAcquisitionServerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync($"/api/acquisition-servers/{id}", cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task<AcquisitionServerProbeResultDto> TestAcquisitionServerAsync(
        Guid? id,
        AcquisitionServerProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = id.HasValue ? $"/api/acquisition-servers/{id.Value}/test" : "/api/acquisition-servers/test";

        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync(route, request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<AcquisitionServerProbeResultDto>(cancellationToken) ??
               throw new InvalidOperationException("服务端未返回采集服务器测试结果。");
    }

    public async Task<ShiftScheduleStatusDto> GetShiftScheduleStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ShiftScheduleStatusDto>(
                   "/api/shift-schedules/status", cancellationToken)
               ?? new ShiftScheduleStatusDto(false, [], null, null, 0, null, null);
    }

    public async Task<ShiftScheduleValidationDto> GetShiftScheduleValidationAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ShiftScheduleValidationDto>(
                   "/api/shift-schedules/validation", cancellationToken)
               ?? new ShiftScheduleValidationDto(false, 0, 0, 0, 0, "暂无数据。", []);
    }

    public async Task<ShiftScheduleImportResultDto> ImportShiftScheduleAsync(
        Stream excelStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(excelStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        content.Add(fileContent, "file", fileName);

        using HttpResponseMessage response =
            await httpClient.PostAsync("/api/shift-schedules/import", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 校验失败时服务端会返回结构化的校验结论，前端需要据此逐条展示问题。
            ShiftScheduleImportResultDto? failure =
                await TryReadImportResultAsync(response, cancellationToken);

            if (failure is not null)
            {
                return failure;
            }

            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<ShiftScheduleImportResultDto>(cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回排班导入结果。");
    }

    public async Task<bool> ClearShiftSchedulesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.DeleteAsync("/api/shift-schedules", cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<DailyShiftScheduleDto>> GetShiftScheduleCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        var route = $"/api/shift-schedules/calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(teamCode))
        {
            route += $"&teamCode={Uri.EscapeDataString(teamCode)}";
        }

        return await httpClient.GetFromJsonAsync<IReadOnlyList<DailyShiftScheduleDto>>(route, cancellationToken) ?? [];
    }

    public async Task<TeamShiftScheduleDto?> GetTeamShiftScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var route =
            $"/api/shift-schedules/teams/{Uri.EscapeDataString(teamCode)}" +
            $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

        using HttpResponseMessage response = await httpClient.GetAsync(route, cancellationToken);

        // 该班组在区间内没有排班属于正常情况，返回 null 而不是抛错。
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TeamShiftScheduleDto>(cancellationToken);
    }

    public async Task<EnergyDashboardDto> GetEnergyDashboardAsync(
        int trendDays = 7,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        var route = $"/api/energy-metrics/dashboard?trendDays={Math.Clamp(trendDays, 1, 60)}";
        if (!string.IsNullOrWhiteSpace(teamCode))
        {
            route += $"&teamCode={Uri.EscapeDataString(teamCode)}";
        }

        return await httpClient.GetFromJsonAsync<EnergyDashboardDto>(route, cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回能耗仪表板数据。");
    }

    public async Task<EnergyHistoryDto> GetEnergyHistoryAsync(
        string teamCode,
        string granularity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var route =
            $"/api/energy-metrics/history?teamCode={Uri.EscapeDataString(teamCode)}" +
            $"&granularity={Uri.EscapeDataString(granularity)}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

        return await httpClient.GetFromJsonAsync<EnergyHistoryDto>(route, cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回历史指标数据。");
    }

    public async Task<EnergyComparisonDto> GetEnergyComparisonAsync(
        string granularity,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var route = $"/api/energy-metrics/comparison?granularity={Uri.EscapeDataString(granularity)}" +
                    $"&date={date:yyyy-MM-dd}";

        return await httpClient.GetFromJsonAsync<EnergyComparisonDto>(route, cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回班组对比数据。");
    }

    /// <summary>
    /// 尝试把导入失败响应解析为结构化校验结论，解析不了则返回 null 交由调用方回退到通用错误。
    /// </summary>
    private static async Task<ShiftScheduleImportResultDto?> TryReadImportResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ShiftScheduleImportResultDto>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 非 2xx 响应统一转为中文 <see cref="InvalidOperationException"/>。
    ///
    /// **为什么不能直接用 <c>EnsureSuccessStatusCode()</c>**：它抛出的
    /// <see cref="HttpRequestException"/> 消息是英文框架文案（如
    /// <c>Response status code does not indicate success: 400 (Bad Request)</c>）。
    /// 页面兜底 catch 会把它拼进用户提示，等于把英文错误码直接暴露给用户。
    /// 端点返回体里的 <c>{ "message": "中文原因" }</c> 才是可展示的内容。
    /// </summary>
    public async Task<IReadOnlyList<AdminDomainDto>> GetAdminDomainsAsync(
        CancellationToken cancellationToken = default)
    {
        var domains =
            await httpClient.GetFromJsonAsync<IReadOnlyList<AdminDomainDto>>("/api/admin/domains",
                cancellationToken);
        return domains ?? [];
    }

    public async Task<bool> UpdateAdminDomainPageAsync(
        Guid pageId,
        AdminDomainPageUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/domains/pages/{pageId}", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> ResetAdminDomainPageAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/admin/domains/pages/{pageId}/reset", content: null, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<IReadOnlyList<AdminDepartmentDto>> GetAdminDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var departments =
            await httpClient.GetFromJsonAsync<IReadOnlyList<AdminDepartmentDto>>("/api/admin/departments",
                cancellationToken);
        return departments ?? [];
    }

    public async Task<bool> CreateAdminDepartmentAsync(
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/admin/departments", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> UpdateAdminDepartmentAsync(
        Guid departmentId,
        AdminDepartmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/departments/{departmentId}", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> UpdateAdminDepartmentGrantsAsync(
        Guid departmentId,
        AdminDepartmentGrantsRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/departments/{departmentId}/grants", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(keyword)
            ? "/api/admin/users"
            : $"/api/admin/users?keyword={Uri.EscapeDataString(keyword.Trim())}";

        var users = await httpClient.GetFromJsonAsync<IReadOnlyList<AdminUserDto>>(url, cancellationToken);
        return users ?? [];
    }

    public async Task<IReadOnlyList<AdminRoleDto>> GetAdminRolesAsync(
        CancellationToken cancellationToken = default)
    {
        var roles = await httpClient.GetFromJsonAsync<IReadOnlyList<AdminRoleDto>>(
            "/api/admin/users/roles", cancellationToken);
        return roles ?? [];
    }

    public async Task<bool> CreateAdminUserAsync(
        AdminUserCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/admin/users", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> UpdateAdminUserAsync(
        Guid userId,
        AdminUserUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/users/{userId}", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> UpdateAdminUserDepartmentAsync(
        Guid userId,
        AdminUserDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/users/{userId}/department", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> UpdateAdminUserRolesAsync(
        Guid userId,
        AdminUserRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/admin/users/{userId}/roles", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> ResetAdminUserPasswordAsync(
        Guid userId,
        AdminUserResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/admin/users/{userId}/reset-password", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> SetAdminUsersEnabledAsync(
        AdminUserBatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/admin/users/batch-status", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<bool> HandoverSuperAdminAsync(
        SuperAdminHandoverRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/admin/users/handover", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    public async Task<AuditLogPageDto> GetAuditLogsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildAuditQuery(from, to, userName, targetType, result);
        query.Add($"page={page}");
        query.Add($"pageSize={pageSize}");

        var url = $"/api/audit-logs?{string.Join('&', query)}";
        var payload = await httpClient.GetFromJsonAsync<AuditLogPageDto>(url, cancellationToken);

        return payload ?? new AuditLogPageDto([], 0, page, pageSize, []);
    }

    public string BuildAuditExportUrl(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result)
    {
        var query = BuildAuditQuery(from, to, userName, targetType, result);
        return query.Count == 0
            ? "/api/audit-logs/export"
            : $"/api/audit-logs/export?{string.Join('&', query)}";
    }

    public async Task<DirectorySyncStatusDto> GetDirectorySyncStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await httpClient.GetFromJsonAsync<DirectorySyncStatusDto>(
            "/api/admin/directory", cancellationToken);

        return status ?? throw new InvalidOperationException("服务端未返回目录同步状态。");
    }

    public async Task<DirectorySyncRunDto> RunDirectorySyncAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.PostAsync("/api/admin/directory/sync", null, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<DirectorySyncRunDto>(cancellationToken);

        return result ?? throw new InvalidOperationException("服务端未返回同步结果。");
    }

    public async Task<bool> ResolveDirectorySyncIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/admin/directory/issues/{issueId}/resolve", null, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OidcProviderDto>> GetOidcProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = await httpClient.GetFromJsonAsync<IReadOnlyList<OidcProviderDto>>(
            "/api/auth/providers", cancellationToken);

        return providers ?? [];
    }

    private static List<string> BuildAuditQuery(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? userName,
        string? targetType,
        string? result)
    {
        List<string> query = [];

        if (from is { } fromValue)
        {
            query.Add($"from={Uri.EscapeDataString(fromValue.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (to is { } toValue)
        {
            query.Add($"to={Uri.EscapeDataString(toValue.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            query.Add($"userName={Uri.EscapeDataString(userName.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query.Add($"targetType={Uri.EscapeDataString(targetType.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            query.Add($"result={Uri.EscapeDataString(result.Trim())}");
        }

        return query;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"请求失败（{(int) response.StatusCode}）。";
        }

        try
        {
            using JsonDocument document = System.Text.Json.JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? payload;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 非 JSON 响应直接回显原文。
        }

        return payload;
    }
}

using System.Globalization;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Repositories;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义后台数据定义执行能力。
/// </summary>
public interface IManagedDataExecutionService
{
    /// <summary>
    /// 对指定数据定义执行调试读取。
    /// </summary>
    Task<ManagedDataExecutionResultDto> DebugReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对指定数据定义执行采集并入库。
    /// </summary>
    Task<ManagedDataExecutionResultDto> CollectAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供后台数据定义执行实现。
/// </summary>
public sealed class ManagedDataExecutionService(
    IManagedDataDefinitionRepository managedDataDefinitionRepository,
    IHistoryApiProxyService historyApiProxyService,
    IDataAcquisitionPlatformService platformService,
    CollectionPointStateSynchronizer collectionPoints,
    IAcquisitionServerConnectionResolver serverConnectionResolver,
    ILogger<ManagedDataExecutionService> logger) : IManagedDataExecutionService
{
    public async Task<ManagedDataExecutionResultDto> DebugReadAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        ManagedDataDefinition definition = await GetDefinitionAsync(id, cancellationToken);
        return await ExecuteReadAsync(definition, "DebugRead", false, cancellationToken);
    }

    public async Task<ManagedDataExecutionResultDto> CollectAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        ManagedDataDefinition definition = await GetDefinitionAsync(id, cancellationToken);
        if (!definition.IsEnabled)
        {
            return CreateFailureResult(
                definition,
                "Collect",
                definition.AcquisitionType,
                definition.ConnectionAddress,
                "{}",
                "当前数据定义已停用，无法执行采集入库。");
        }

        return await ExecuteReadAsync(definition, "Collect", true, cancellationToken);
    }

    private async Task<ManagedDataExecutionResultDto> ExecuteReadAsync(
        ManagedDataDefinition definition,
        string executionMode,
        bool persistRecord,
        CancellationToken cancellationToken)
    {
        try
        {
            return NormalizeAcquisitionType(definition.AcquisitionType) switch
            {
                "Historian API" => await ExecuteHistorianAsync(definition, executionMode, persistRecord,
                    cancellationToken),
                _ => CreateFailureResult(
                    definition,
                    executionMode,
                    definition.AcquisitionType,
                    definition.ConnectionAddress,
                    "{}",
                    $"采集方式 {definition.AcquisitionType} 的执行器暂未实现。")
            };
        }
        catch (OperationCanceledException)
        {
            // 上游 HttpClient 超时会以 TaskCanceledException 抛出，
            // 直接透传会把 "A task was canceled" 这类框架文案暴露到界面。
            logger.LogWarning("执行后台数据定义 {Code} 超时。", definition.Code);
            return CreateFailureResult(
                definition,
                executionMode,
                definition.AcquisitionType,
                definition.ConnectionAddress,
                "{}",
                "采集服务器响应超时，请检查网络连通性或调大超时配置。");
        }
        catch (Exception ex)
        {
            // 兜底分支绝不透传 ex.Message：网络类异常的原始文案是英文框架消息，
            // 还可能顺带泄漏内网地址。中文结论给界面，细节只进日志。
            logger.LogWarning(ex, "执行后台数据定义 {Code} 失败。", definition.Code);
            return CreateFailureResult(
                definition,
                executionMode,
                definition.AcquisitionType,
                definition.ConnectionAddress,
                "{}",
                ex is HttpRequestException
                    ? "无法连接采集服务器，请检查网络连通性与服务器地址配置。"
                    : "采集执行失败，请查看服务端日志。");
        }
    }

    private async Task<ManagedDataExecutionResultDto> ExecuteHistorianAsync(
        ManagedDataDefinition definition,
        string executionMode,
        bool persistRecord,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> configuration = ParseConfiguration(definition.ConfigurationJson);
        var queryMode = GetValueOrDefault(configuration, "queryMode", "CurrentValue");
        HistoryApiConnectionOptions connectionOptions =
            await ResolveHistorianConnectionOptionsAsync(definition, cancellationToken);

        HistoryApiQueryResponse response = queryMode switch
        {
            "Raw" => await historyApiProxyService.GetRawDataAsync(
                new HistoryRawDataQueryRequest(
                    definition.Identifier,
                    DateTimeOffset.UtcNow.AddHours(-1),
                    DateTimeOffset.UtcNow,
                    0,
                    100),
                connectionOptions,
                cancellationToken),
            "CurrentValueAndRaw" => await ExecuteHistorianCurrentOrRawFallbackAsync(
                definition,
                connectionOptions,
                cancellationToken),
            _ => await historyApiProxyService.GetCurrentValueAsync(
                new HistoryCurrentValueQueryRequest(definition.Identifier),
                connectionOptions,
                cancellationToken)
        };

        if (!response.Success)
        {
            return CreateFailureResult(
                definition,
                executionMode,
                response.QueryType,
                response.RequestUrl,
                response.PayloadJson,
                response.ErrorMessage ?? "上游接口调用失败。");
        }

        ParsedHistorianValue? sample = TryExtractHistorianValue(response.PayloadJson, definition.Identifier);
        if (sample is null)
        {
            return CreateFailureResult(
                definition,
                executionMode,
                response.QueryType,
                response.RequestUrl,
                response.PayloadJson,
                "已成功调用上游接口，但未能从返回结果中解析出数值样本。");
        }

        CollectionDataRecordDto? savedRecord = null;
        var statusMessage = $"调试读取成功，解析出数值 {sample.Value.ToString(CultureInfo.InvariantCulture)}。";

        if (persistRecord)
        {
            // 采集点状态属于数据面：后台调度器没有操作者，不能走需要配置权限点的
            // UpsertCollectionPointAsync（那条路径会被 ServiceAuditGuard 拒绝）。
            await collectionPoints.SyncAsync(
                definition.Code,
                definition.Name,
                definition.AcquisitionType,
                definition.ConnectionAddress,
                definition.IsEnabled,
                "在线",
                null,
                DateTimeOffset.UtcNow,
                cancellationToken);

            savedRecord = await platformService.IngestCollectionDataAsync(
                new IngestCollectionDataRequest(
                    definition.Code,
                    definition.Name,
                    sample.Value,
                    definition.Unit,
                    sample.SampledAt ?? DateTimeOffset.UtcNow),
                cancellationToken);

            statusMessage = $"采集成功，已入库数值 {sample.Value.ToString(CultureInfo.InvariantCulture)}。";
        }

        return new ManagedDataExecutionResultDto(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.AcquisitionType,
            executionMode,
            true,
            response.QueryType,
            response.RequestUrl,
            definition.Identifier,
            sample.Value,
            definition.Unit,
            sample.SampledAt,
            response.PayloadJson,
            statusMessage,
            DateTimeOffset.UtcNow,
            savedRecord);
    }

    private async Task<HistoryApiQueryResponse> ExecuteHistorianCurrentOrRawFallbackAsync(
        ManagedDataDefinition definition,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken)
    {
        HistoryApiQueryResponse currentResponse = await historyApiProxyService.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest(definition.Identifier),
            connectionOptions,
            cancellationToken);

        if (currentResponse.Success)
        {
            return currentResponse;
        }

        return await historyApiProxyService.GetRawDataAsync(
            new HistoryRawDataQueryRequest(
                definition.Identifier,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow,
                0,
                100),
            connectionOptions,
            cancellationToken);
    }

    private async Task<ManagedDataDefinition> GetDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        ManagedDataDefinition? definition = await managedDataDefinitionRepository.GetByIdAsync(id, cancellationToken);
        return definition ?? throw new InvalidOperationException("指定的后台数据定义不存在。");
    }

    private static string NormalizeAcquisitionType(string acquisitionType)
    {
        return string.IsNullOrWhiteSpace(acquisitionType) ? string.Empty : acquisitionType.Trim();
    }

    private static Dictionary<string, string> ParseConfiguration(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configurationJson);

            return rawDictionary?.ToDictionary(
                item => item.Key,
                item => item.Value.ValueKind == JsonValueKind.String
                    ? item.Value.GetString() ?? string.Empty
                    : item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ParsedHistorianValue? TryExtractHistorianValue(string payloadJson, string expectedIdentifier)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(payloadJson);
        return TryExtractHistorianValue(document.RootElement, expectedIdentifier);
    }

    private static ParsedHistorianValue? TryExtractHistorianValue(JsonElement element, string expectedIdentifier)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => TryExtractHistorianValueFromObject(element, expectedIdentifier),
            JsonValueKind.Array => TryExtractHistorianValueFromChildren(element.EnumerateArray(), expectedIdentifier),
            _ => null
        };
    }

    private static ParsedHistorianValue? TryExtractHistorianValueFromObject(
        JsonElement element,
        string expectedIdentifier)
    {
        if (TryCreateSampleFromObject(element, expectedIdentifier, out ParsedHistorianValue? sample))
        {
            return sample;
        }

        return TryExtractHistorianValueFromChildren(
            element.EnumerateObject().Select(static property => property.Value),
            expectedIdentifier);
    }

    private static ParsedHistorianValue? TryExtractHistorianValueFromChildren(
        IEnumerable<JsonElement> elements,
        string expectedIdentifier)
    {
        foreach (JsonElement child in elements)
        {
            ParsedHistorianValue? nested = TryExtractHistorianValue(child, expectedIdentifier);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TryCreateSampleFromObject(
        JsonElement element,
        string expectedIdentifier,
        out ParsedHistorianValue? sample)
    {
        sample = null;

        if (!IdentifierMatches(element, expectedIdentifier))
        {
            return false;
        }

        var value = TryGetDecimalProperty(element, "value")
                    ?? TryGetDecimalProperty(element, "currentValue")
                    ?? TryGetDecimalProperty(element, "Value")
                    ?? TryGetDecimalProperty(element, "CurrentValue");

        if (value is null)
        {
            return false;
        }

        DateTimeOffset? sampledAt =
            TryGetDateTimeOffsetProperty(element, "timestamp")
            ?? TryGetDateTimeOffsetProperty(element, "time")
            ?? TryGetDateTimeOffsetProperty(element, "datetime")
            ?? TryGetDateTimeOffsetProperty(element, "collectedAt")
            ?? TryGetDateTimeOffsetProperty(element, "Timestamp")
            ?? TryGetDateTimeOffsetProperty(element, "Time");

        sample = new ParsedHistorianValue(value.Value, sampledAt);
        return true;
    }

    private static bool IdentifierMatches(JsonElement element, string expectedIdentifier)
    {
        string[] candidateKeys = ["tagName", "tagname", "name", "Name", "TagName"];

        foreach (var key in candidateKeys)
        {
            if (TryGetStringProperty(element, key, out var value) &&
                !string.IsNullOrWhiteSpace(value) &&
                !string.IsNullOrWhiteSpace(expectedIdentifier) &&
                !value.Equals(expectedIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static decimal? TryGetDecimalProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static string GetValueOrDefault(IReadOnlyDictionary<string, string> dictionary, string key, string fallback)
    {
        return dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private async Task<HistoryApiConnectionOptions> ResolveHistorianConnectionOptionsAsync(
        ManagedDataDefinition definition,
        CancellationToken cancellationToken)
    {
        ServerConnectionSettings? settings = await serverConnectionResolver.ResolveAsync(
            definition.ServerConnectionId, AcquisitionServerCatalog.Historian, cancellationToken);

        if (settings is null)
        {
            throw new InvalidOperationException(
                "未在采集服务器配置中找到可用的 GE Historian 服务器，请先在采集服务器页面新增并启用。");
        }

        return settings.ToHistorianOptions();
    }

    private static ManagedDataExecutionResultDto CreateFailureResult(
        ManagedDataDefinition definition,
        string executionMode,
        string sourceQueryType,
        string requestTarget,
        string payloadJson,
        string errorMessage)
    {
        return new ManagedDataExecutionResultDto(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.AcquisitionType,
            executionMode,
            false,
            sourceQueryType,
            requestTarget,
            definition.Identifier,
            null,
            definition.Unit,
            null,
            string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            errorMessage,
            DateTimeOffset.UtcNow,
            null,
            errorMessage);
    }

    private sealed record ParsedHistorianValue(decimal Value, DateTimeOffset? SampledAt);
}

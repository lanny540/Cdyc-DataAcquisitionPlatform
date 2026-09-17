using System.Globalization;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
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
    ILogger<ManagedDataExecutionService> logger) : IManagedDataExecutionService
{
    public async Task<ManagedDataExecutionResultDto> DebugReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ManagedDataDefinition definition = await GetDefinitionAsync(id, cancellationToken);
        return await ExecuteReadAsync(definition, "DebugRead", persistRecord: false, cancellationToken);
    }

    public async Task<ManagedDataExecutionResultDto> CollectAsync(Guid id, CancellationToken cancellationToken = default)
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

        return await ExecuteReadAsync(definition, "Collect", persistRecord: true, cancellationToken);
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "执行后台数据定义 {Code} 失败。", definition.Code);
            return CreateFailureResult(
                definition,
                executionMode,
                definition.AcquisitionType,
                definition.ConnectionAddress,
                "{}",
                ex.Message);
        }
    }

    private async Task<ManagedDataExecutionResultDto> ExecuteHistorianAsync(
        ManagedDataDefinition definition,
        string executionMode,
        bool persistRecord,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> configuration = ParseConfiguration(definition.ConfigurationJson);
        string queryMode = GetValueOrDefault(configuration, "queryMode", "CurrentValue");

        HistoryApiQueryResponse response = queryMode switch
        {
            "Raw" => await historyApiProxyService.GetRawDataAsync(
                new HistoryRawDataQueryRequest(
                    definition.Identifier,
                    DateTimeOffset.UtcNow.AddHours(-1),
                    DateTimeOffset.UtcNow,
                    0,
                    100),
                definition.ConnectionAddress,
                cancellationToken),
            "CurrentValueAndRaw" => await ExecuteHistorianCurrentOrRawFallbackAsync(definition, cancellationToken),
            _ => await historyApiProxyService.GetCurrentValueAsync(
                new HistoryCurrentValueQueryRequest(definition.Identifier),
                definition.ConnectionAddress,
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
        string statusMessage = $"调试读取成功，解析出数值 {sample.Value.ToString(CultureInfo.InvariantCulture)}。";

        if (persistRecord)
        {
            await platformService.UpsertCollectionPointAsync(
                new CollectionPointUpsertRequest(
                    null,
                    definition.Code,
                    definition.Name,
                    definition.AcquisitionType,
                    definition.ConnectionAddress,
                    definition.IsEnabled,
                    "Server"),
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
        CancellationToken cancellationToken)
    {
        HistoryApiQueryResponse currentResponse = await historyApiProxyService.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest(definition.Identifier),
            definition.ConnectionAddress,
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
            definition.ConnectionAddress,
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
            Dictionary<string, JsonElement>? rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configurationJson);

            return rawDictionary?.ToDictionary(
                item => item.Key,
                item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.ToString(),
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
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryCreateSampleFromObject(element, expectedIdentifier, out ParsedHistorianValue? sample))
                {
                    return sample;
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ParsedHistorianValue? nested = TryExtractHistorianValue(property.Value, expectedIdentifier);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ParsedHistorianValue? nested = TryExtractHistorianValue(item, expectedIdentifier);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                break;
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

        decimal? value = TryGetDecimalProperty(element, "value")
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

        foreach (string key in candidateKeys)
        {
            if (TryGetStringProperty(element, key, out string? value) &&
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

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out decimal number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal stringValue))
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
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static string GetValueOrDefault(IReadOnlyDictionary<string, string> dictionary, string key, string fallback)
    {
        return dictionary.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
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

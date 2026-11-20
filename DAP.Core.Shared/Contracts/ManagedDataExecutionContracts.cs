namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示后台数据定义执行结果。
/// </summary>
/// <param name="ManagedDataDefinitionId">后台数据定义标识。</param>
/// <param name="Code">数据编码。</param>
/// <param name="Name">数据名称。</param>
/// <param name="AcquisitionType">采集方式。</param>
/// <param name="ExecutionMode">执行模式，例如 DebugRead 或 Collect。</param>
/// <param name="Success">执行是否成功。</param>
/// <param name="SourceQueryType">底层查询类型，例如 CurrentValue、Raw。</param>
/// <param name="RequestTarget">实际访问目标。</param>
/// <param name="Identifier">数据标识。</param>
/// <param name="ParsedValue">解析出的数值。</param>
/// <param name="Unit">单位。</param>
/// <param name="SampledAt">样本时间。</param>
/// <param name="PayloadJson">原始 JSON 返回。</param>
/// <param name="StatusMessage">状态说明。</param>
/// <param name="ExecutedAt">执行时间。</param>
/// <param name="SavedRecord">已入库记录。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record ManagedDataExecutionResultDto(
    Guid ManagedDataDefinitionId,
    string Code,
    string Name,
    string AcquisitionType,
    string ExecutionMode,
    bool Success,
    string SourceQueryType,
    string RequestTarget,
    string Identifier,
    decimal? ParsedValue,
    string Unit,
    DateTimeOffset? SampledAt,
    string PayloadJson,
    string StatusMessage,
    DateTimeOffset ExecutedAt,
    CollectionDataRecordDto? SavedRecord = null,
    string? ErrorMessage = null);

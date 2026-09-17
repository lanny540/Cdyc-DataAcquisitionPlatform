namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示查询 History 数据库最新值的请求。
/// </summary>
/// <param name="TagNames">标签名称，支持逗号分隔多个标签。</param>
public sealed record HistoryCurrentValueQueryRequest(string TagNames);

/// <summary>
/// 表示查询 History 数据库时间段原始数据的请求。
/// </summary>
/// <param name="TagName">标签名称。</param>
/// <param name="StartTime">开始时间。</param>
/// <param name="EndTime">结束时间。</param>
/// <param name="StartIndex">起始偏移量。</param>
/// <param name="Count">返回条数。</param>
public sealed record HistoryRawDataQueryRequest(
    string TagName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int StartIndex,
    int Count);

/// <summary>
/// 表示 History 数据库测试查询的响应。
/// </summary>
/// <param name="Success">查询是否成功。</param>
/// <param name="QueryType">查询类型。</param>
/// <param name="RequestUrl">实际访问的上游地址。</param>
/// <param name="StatusCode">上游返回的状态码。</param>
/// <param name="PayloadJson">上游返回的原始 JSON 文本。</param>
/// <param name="ExecutedAt">执行时间。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record HistoryApiQueryResponse(
    bool Success,
    string QueryType,
    string RequestUrl,
    int StatusCode,
    string PayloadJson,
    DateTimeOffset ExecutedAt,
    string? ErrorMessage = null);

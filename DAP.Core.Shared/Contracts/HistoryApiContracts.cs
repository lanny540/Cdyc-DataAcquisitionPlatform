namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示查询 History 数据库最新值的请求。
/// </summary>
/// <param name="TagNames">标签名称，支持逗号分隔多个标签。</param>
/// <param name="ServerConnectionId">采集服务器标识，为空时使用第一个启用的 Historian 服务器。</param>
public sealed record HistoryCurrentValueQueryRequest(string TagNames, Guid? ServerConnectionId = null);

/// <summary>
/// 表示查询 History 数据库时间段原始数据的请求。
/// </summary>
/// <param name="TagName">标签名称。</param>
/// <param name="StartTime">开始时间。</param>
/// <param name="EndTime">结束时间。</param>
/// <param name="StartIndex">起始偏移量。</param>
/// <param name="Count">返回条数。</param>
/// <param name="ServerConnectionId">采集服务器标识，为空时使用第一个启用的 Historian 服务器。</param>
public sealed record HistoryRawDataQueryRequest(
    string TagName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int StartIndex,
    int Count,
    Guid? ServerConnectionId = null)
{
    /// <summary>
    /// 单次原始数据查询的返回条数硬上限，超出时应分页查询。
    /// </summary>
    public const int MaxCount = 10000;

    /// <summary>
    /// 校验查询参数，供 API 与交互页面共用，避免只在客户端限制。
    /// </summary>
    /// <returns>参数合法时返回 null，否则返回可直接展示的中文错误。</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TagName)) return "标签名称不能为空。";
        if (EndTime <= StartTime) return "结束时间必须晚于开始时间。";
        if (StartIndex < 0 || Count <= 0) return "起始偏移量不能小于 0，返回条数必须大于 0。";
        if (Count > MaxCount) return $"返回条数不能超过 {MaxCount}，请分页查询。";

        return null;
    }
}

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
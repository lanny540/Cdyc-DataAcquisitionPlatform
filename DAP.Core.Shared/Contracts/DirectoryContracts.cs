namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示一条目录同步待处理项。
/// </summary>
/// <param name="Id">标识。</param>
/// <param name="IssueType">类型：department-unmatched 或 user-name-conflict。</param>
/// <param name="ExternalCode">部门外部编码，或冲突时的账号名。</param>
/// <param name="SampleUserName">样例账号名。</param>
/// <param name="Occurrences">累计出现次数。</param>
/// <param name="FirstSeenAt">首次出现时间。</param>
/// <param name="LastSeenAt">最近出现时间。</param>
public sealed record DirectorySyncIssueDto(
    Guid Id,
    string IssueType,
    string ExternalCode,
    string? SampleUserName,
    int Occurrences,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// 表示目录同步状态。
/// </summary>
/// <param name="Enabled">是否启用目录认证与同步。</param>
/// <param name="LastSyncedAt">最近一次同步时间；从未同步时为 null。</param>
/// <param name="LastResult">最近一次同步结果：success 或 failed。</param>
/// <param name="LastSummary">最近一次同步的中文摘要。</param>
/// <param name="Issues">未处理的待处理项。</param>
public sealed record DirectorySyncStatusDto(
    bool Enabled,
    DateTimeOffset? LastSyncedAt,
    string? LastResult,
    string? LastSummary,
    IReadOnlyList<DirectorySyncIssueDto> Issues);

/// <summary>
/// 表示一次同步的结果。
/// </summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Message">中文摘要，可直接展示。</param>
/// <param name="Created">新建账号数。</param>
/// <param name="Updated">更新账号数。</param>
/// <param name="Disabled">停用账号数。</param>
/// <param name="Unchanged">无变化账号数。</param>
/// <param name="Issues">本轮新增或命中的待处理项数。</param>
/// <param name="KeptManualDisable">因管理员手工停用而保持停用的账号数。</param>
public sealed record DirectorySyncRunDto(
    bool Succeeded,
    string Message,
    int Created,
    int Updated,
    int Disabled,
    int Unchanged,
    int Issues,
    int KeptManualDisable = 0);

/// <summary>
/// 表示登录页上可用的一个 OIDC 提供方。
/// </summary>
/// <param name="Code">提供方编码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Href">发起登录的地址。</param>
public sealed record OidcProviderDto(string Code, string DisplayName, string Href);

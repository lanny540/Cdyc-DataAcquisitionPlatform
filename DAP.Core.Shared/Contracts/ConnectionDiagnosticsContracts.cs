namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示单个服务器的连接状态。
/// </summary>
/// <param name="Key">服务器键。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Address">服务器地址。</param>
/// <param name="IsConfigured">是否已配置。</param>
/// <param name="IsReachable">是否可达。</param>
/// <param name="UseMockResponses">业务执行是否启用 Mock。</param>
/// <param name="CredentialsConfigured">是否已配置凭据。</param>
/// <param name="StatusMessage">状态说明。</param>
/// <param name="CheckedAt">检查时间。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record ServerConnectionStatusDto(
    string Key,
    string DisplayName,
    string Address,
    bool IsConfigured,
    bool IsReachable,
    bool UseMockResponses,
    bool CredentialsConfigured,
    string StatusMessage,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null);

/// <summary>
/// 表示针对后台数据定义草稿的连接测试结果。
/// </summary>
/// <param name="AcquisitionType">采集方式。</param>
/// <param name="Address">测试地址。</param>
/// <param name="Success">是否成功。</param>
/// <param name="StatusMessage">状态说明。</param>
/// <param name="CheckedAt">检查时间。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record ManagedDataConnectionTestResultDto(
    string AcquisitionType,
    string Address,
    bool Success,
    string StatusMessage,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null);

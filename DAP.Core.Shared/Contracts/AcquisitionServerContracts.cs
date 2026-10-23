namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示采集服务器配置字段的控件类型。
/// </summary>
public static class AcquisitionServerFieldTypes
{
    /// <summary>单行文本。</summary>
    public const string Text = "text";

    /// <summary>密码文本，保存与回显时需要掩码。</summary>
    public const string Password = "password";

    /// <summary>数值文本。</summary>
    public const string Number = "number";

    /// <summary>开关。</summary>
    public const string Boolean = "boolean";

    /// <summary>下拉选择。</summary>
    public const string Select = "select";
}

/// <summary>
/// 表示采集服务器某个配置字段的界面与校验元描述。
/// </summary>
/// <param name="Key">字段键，与配置 JSON 的键一致。</param>
/// <param name="Label">界面标签。</param>
/// <param name="FieldType">控件类型，取值见 <see cref="AcquisitionServerFieldTypes"/>。</param>
/// <param name="IsRequired">是否必填。</param>
/// <param name="Placeholder">占位提示。</param>
/// <param name="HelperText">补充说明。</param>
/// <param name="DefaultValue">创建时的默认值。</param>
/// <param name="Options">下拉可选项。</param>
/// <param name="Group">所属分组名，用于在界面上分区展示。</param>
public sealed record AcquisitionServerFieldDescriptor(
    string Key,
    string Label,
    string FieldType = AcquisitionServerFieldTypes.Text,
    bool IsRequired = false,
    string? Placeholder = null,
    string? HelperText = null,
    string? DefaultValue = null,
    IReadOnlyCollection<string>? Options = null,
    string? Group = null);

/// <summary>
/// 表示一种采集服务器类型的完整描述，包含服务器级字段与节点级字段。
/// </summary>
/// <param name="ServerType">服务器类型键，同时作为采集方式写入节点。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">用途说明。</param>
/// <param name="AddressLabel">地址字段标签。</param>
/// <param name="AddressPlaceholder">地址占位提示。</param>
/// <param name="DefaultAddress">默认地址。</param>
/// <param name="IdentifierLabel">节点数据标识的标签，例如 Tag 名称。</param>
/// <param name="IdentifierPlaceholder">节点数据标识的占位提示。</param>
/// <param name="IdentifierHelperText">节点数据标识的补充说明。</param>
/// <param name="SupportsApiProbe">是否支持调用接口读取返回值。</param>
/// <param name="ApiProbePathLabel">接口测试路径的标签，为空表示该类型不支持自定义路径。</param>
/// <param name="ApiProbePathPlaceholder">接口测试路径的占位提示。</param>
/// <param name="DefaultApiProbePath">接口测试的默认路径。</param>
/// <param name="ServerFields">服务器级配置字段。</param>
/// <param name="NodeFields">节点级配置字段。</param>
public sealed record AcquisitionServerTypeDescriptor(
    string ServerType,
    string DisplayName,
    string Description,
    string AddressLabel,
    string AddressPlaceholder,
    string? DefaultAddress,
    string IdentifierLabel,
    string? IdentifierPlaceholder,
    string? IdentifierHelperText,
    bool SupportsApiProbe,
    string? ApiProbePathLabel,
    string? ApiProbePathPlaceholder,
    string? DefaultApiProbePath,
    IReadOnlyCollection<AcquisitionServerFieldDescriptor> ServerFields,
    IReadOnlyCollection<AcquisitionServerFieldDescriptor> NodeFields);

/// <summary>
/// 表示采集服务器配置项，其中密钥字段以掩码回显。
/// </summary>
/// <param name="Key">配置键。</param>
/// <param name="Value">配置值，密钥字段为掩码或占位提示。</param>
/// <param name="IsSecret">是否为密钥字段。</param>
/// <param name="HasStoredValue">数据库中是否已存在该字段值。</param>
public sealed record AcquisitionServerConfigValueDto(
    string Key,
    string Value,
    bool IsSecret,
    bool HasStoredValue);

/// <summary>
/// 表示一个采集服务器配置。
/// </summary>
/// <param name="Id">服务器标识。</param>
/// <param name="Code">服务器编码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">补充说明。</param>
/// <param name="ServerType">服务器类型键。</param>
/// <param name="ServerTypeName">服务器类型显示名称。</param>
/// <param name="Address">服务器基础地址，仅到主机端口与上下文路径。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Configuration">配置项，密钥字段已掩码。</param>
/// <param name="RelatedNodeCount">关联的采集节点数量。</param>
/// <param name="LastProbeStatus">最近一次连通性测试结果。</param>
/// <param name="LastProbeMessage">最近一次测试的说明。</param>
/// <param name="LastProbedAt">最近一次测试时间。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
public sealed record AcquisitionServerDto(
    Guid Id,
    string Code,
    string DisplayName,
    string Description,
    string ServerType,
    string ServerTypeName,
    string Address,
    bool IsEnabled,
    IReadOnlyCollection<AcquisitionServerConfigValueDto> Configuration,
    int RelatedNodeCount,
    string LastProbeStatus,
    string? LastProbeMessage,
    DateTimeOffset? LastProbedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 表示采集服务器新增或更新请求。
/// </summary>
/// <param name="Id">服务器标识，为空时表示新增。</param>
/// <param name="Code">服务器编码，留空时按类型与地址自动生成。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">补充说明。</param>
/// <param name="ServerType">服务器类型键。</param>
/// <param name="Address">服务器基础地址。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Configuration">配置项，密钥字段可传掩码占位以表示保持原值。</param>
public sealed record AcquisitionServerUpsertRequest(
    Guid? Id,
    string Code,
    string DisplayName,
    string Description,
    string ServerType,
    string Address,
    bool IsEnabled,
    IReadOnlyDictionary<string, string> Configuration);

/// <summary>
/// 表示服务器连通性或接口读取的测试结果。
/// </summary>
/// <param name="Success">是否成功。</param>
/// <param name="ProbeKind">测试方式，例如网络探测、接口读取。</param>
/// <param name="RequestTarget">实际访问的地址。</param>
/// <param name="StatusCode">上游返回状态码，网络层失败时为 0。</param>
/// <param name="StatusMessage">结果说明。</param>
/// <param name="PayloadJson">上游返回的原始报文。</param>
/// <param name="ParsedValue">解析出的数值，解析失败时为空。</param>
/// <param name="ElapsedMilliseconds">耗时毫秒数。</param>
/// <param name="CheckedAt">测试时间。</param>
/// <param name="ErrorMessage">失败原因。</param>
public sealed record AcquisitionServerProbeResultDto(
    bool Success,
    string ProbeKind,
    string RequestTarget,
    int StatusCode,
    string StatusMessage,
    string PayloadJson,
    string? ParsedValue,
    long ElapsedMilliseconds,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null);

/// <summary>
/// 表示采集服务器连通性测试请求。
/// </summary>
/// <param name="ServerType">服务器类型键。</param>
/// <param name="Address">服务器基础地址。</param>
/// <param name="Configuration">服务器级配置。</param>
/// <param name="Identifier">节点数据标识，接口读取时使用。</param>
/// <param name="RequestPath">接口请求路径。</param>
/// <param name="NodeConfiguration">节点级配置。</param>
public sealed record AcquisitionServerProbeRequest(
    string ServerType,
    string Address,
    IReadOnlyDictionary<string, string> Configuration,
    string? Identifier = null,
    string? RequestPath = null,
    IReadOnlyDictionary<string, string>? NodeConfiguration = null)
{
    /// <summary>
    /// 转换为服务器保存请求，复用同一套配置校验与掩码还原逻辑。
    /// </summary>
    /// <param name="id">服务器标识，草稿测试时可为空。</param>
    /// <returns>服务器保存请求。</returns>
    public AcquisitionServerUpsertRequest ToUpsertRequest(Guid? id)
    {
        return new AcquisitionServerUpsertRequest(
            id,
            string.Empty,
            string.Empty,
            string.Empty,
            ServerType ?? string.Empty,
            Address ?? string.Empty,
            true,
            Configuration ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}

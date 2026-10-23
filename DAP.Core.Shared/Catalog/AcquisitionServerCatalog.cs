using DAP.Core.Shared.Contracts;

namespace DAP.Core.Shared.Catalog;

/// <summary>
/// 定义平台支持的采集服务器类型目录。
/// 服务器级字段在此集中登记，节点只需补齐该类型的必要采集信息。
/// </summary>
public static class AcquisitionServerCatalog
{
    /// <summary>GE Historian 数据库。</summary>
    public const string Historian = "Historian API";

    /// <summary>InfluxDB 时序数据库。</summary>
    public const string InfluxDb = "InfluxDB";

    /// <summary>Modbus 工业协议。</summary>
    public const string Modbus = "Modbus";

    /// <summary>MQTT 消息总线。</summary>
    public const string Mqtt = "MQTT";

    /// <summary>OPC DA 经典接口。</summary>
    public const string OpcDa = "OPC DA";

    /// <summary>通用 HTTP 接口。</summary>
    public const string HttpApi = "HTTP API";

    private static readonly HashSet<string> SupportedAddressSchemes =
        new(StringComparer.OrdinalIgnoreCase) {"http", "https", "tcp", "mqtt"};

    private static readonly IReadOnlyList<AcquisitionServerTypeDescriptor> AllDescriptors =
    [
        new(
            Historian,
            "GE Historian",
            "GE Proficy Historian 时序数据库，通过 REST API 按 Tag 读取最新值或时间段原始数据。",
            "服务器基础地址",
            "例如 http://10.9.3.54:8080（只填到端口，不要带 /historian-rest-api 之类的接口路径）",
            "http://10.9.3.54:8080",
            "Tag 名称",
            "例如 P1P2DCS-MS-/ASSETS/BROKE/LIS4108.MES",
            "Historian 中唯一的点位标识，大小写与连字符需与服务器一致。",
            true,
            "最新值接口路径",
            "例如 /historian-rest-api/v1/datapoints/currentvalue",
            "/historian-rest-api/v1/datapoints/currentvalue",
            [
                new AcquisitionServerFieldDescriptor("clientId", "ClientId", AcquisitionServerFieldTypes.Text, true,
                    "admin",
                    "用于 OAuth2 客户端凭证换取访问令牌。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("clientSecret", "ClientSecret",
                    AcquisitionServerFieldTypes.Password, true,
                    HelperText: "访问密钥，保存后仅掩码回显。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("oauthTokenPath", "Token 接口路径", AcquisitionServerFieldTypes.Text,
                    false,
                    "/uaa/oauth/token",
                    "换取访问令牌的相对路径。",
                    "/uaa/oauth/token", Group: "接口"),
                new AcquisitionServerFieldDescriptor("currentValuePath", "最新值接口路径", AcquisitionServerFieldTypes.Text,
                    false,
                    "/historian-rest-api/v1/datapoints/currentvalue",
                    "读取最新值的相对路径。",
                    "/historian-rest-api/v1/datapoints/currentvalue", Group: "接口"),
                new AcquisitionServerFieldDescriptor("rawDataPathPrefix", "原始数据路径前缀", AcquisitionServerFieldTypes.Text,
                    false,
                    "/historian-rest-api/v1/datapoints/raw",
                    "读取时间段数据的相对路径。",
                    "/historian-rest-api/v1/datapoints/raw", Group: "接口"),
                new AcquisitionServerFieldDescriptor("useMockResponses", "使用 Mock 响应",
                    AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "开启后不访问真实 Historian，仅用于前端联调。",
                    DefaultValue: "false", Group: "调试"),
                new AcquisitionServerFieldDescriptor("timeoutSeconds", "超时秒数", AcquisitionServerFieldTypes.Number,
                    false,
                    "30",
                    "单次接口调用超时时间。",
                    "30", Group: "请求"),
                new AcquisitionServerFieldDescriptor("ignoreCertificateErrors", "忽略证书错误",
                    AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "Historian 使用自签名证书时开启。",
                    DefaultValue: "true", Group: "请求")
            ],
            [
                new AcquisitionServerFieldDescriptor("queryMode", "查询模式", AcquisitionServerFieldTypes.Select, false,
                    HelperText: "最新值读取固定使用 CurrentValue。",
                    DefaultValue: "CurrentValue",
                    Options: ["CurrentValue", "Raw", "CurrentValueAndRaw"])
            ]),
        new(
            InfluxDb,
            "InfluxDB",
            "InfluxDB 时序数据库，按 Bucket 与 Measurement 组织点位，支持 Token 或用户名密码鉴权。",
            "服务器基础地址",
            "例如 http://10.9.3.60:8086（只填到端口）",
            "http://10.9.3.60:8086",
            "Measurement",
            "例如 steam_flow",
            "InfluxDB 中的 measurement 名称。",
            true,
            "健康检查路径",
            "例如 /health",
            "/health",
            [
                new AcquisitionServerFieldDescriptor("organization", "Organization", AcquisitionServerFieldTypes.Text,
                    true,
                    HelperText: "InfluxDB 组织名，写入和查询都需要。", Group: "基础"),
                new AcquisitionServerFieldDescriptor("token", "Token", AcquisitionServerFieldTypes.Password, false,
                    HelperText: "API Token，与用户名密码二选一。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("username", "用户名", AcquisitionServerFieldTypes.Text, false,
                    HelperText: "基础认证用户名。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("password", "密码", AcquisitionServerFieldTypes.Password, false,
                    HelperText: "基础认证密码。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("healthPath", "健康检查路径", AcquisitionServerFieldTypes.Text, false,
                    "/health",
                    "用于连通性测试的相对路径。",
                    "/health", Group: "接口"),
                new AcquisitionServerFieldDescriptor("queryPath", "查询路径前缀", AcquisitionServerFieldTypes.Text, false,
                    "/api/v2/query",
                    "Flux 查询接口路径。",
                    "/api/v2/query", Group: "接口"),
                new AcquisitionServerFieldDescriptor("useMockResponses", "使用 Mock 响应",
                    AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "开启后不访问真实 InfluxDB，仅用于前端联调。",
                    DefaultValue: "false", Group: "调试"),
                new AcquisitionServerFieldDescriptor("timeoutSeconds", "超时秒数", AcquisitionServerFieldTypes.Number,
                    false,
                    "15",
                    "单次请求超时时间。",
                    "15", Group: "请求"),
                new AcquisitionServerFieldDescriptor("ignoreCertificateErrors", "忽略证书错误",
                    AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "自签名证书场景下开启。",
                    DefaultValue: "false", Group: "请求")
            ],
            [
                new AcquisitionServerFieldDescriptor("bucket", "Bucket", AcquisitionServerFieldTypes.Text, true,
                    HelperText: "数据所在的 bucket 名称。"),
                new AcquisitionServerFieldDescriptor("field", "Field", AcquisitionServerFieldTypes.Text, false,
                    "value",
                    "measurement 下的字段键。",
                    "value")
            ]),
        new(
            Modbus,
            "Modbus",
            "Modbus TCP 或 RTU 工业协议，按寄存器地址读取数值。",
            "连接地址",
            "例如 192.168.1.10:502（TCP）或 COM3（RTU）",
            "192.168.1.10:502",
            "寄存器地址",
            "例如 40001",
            "Modbus 寄存器地址，支持 4xxxx 保持寄存器等常用写法。",
            false,
            null,
            null,
            null,
            [
                new AcquisitionServerFieldDescriptor("protocolVariant", "协议变体", AcquisitionServerFieldTypes.Select,
                    false,
                    HelperText: "串口 RTU 无法在 Web 端探测，建议使用 TCP。",
                    DefaultValue: "TCP",
                    Options: ["TCP", "RTU"], Group: "基础"),
                new AcquisitionServerFieldDescriptor("baudRate", "波特率", AcquisitionServerFieldTypes.Number, false,
                    "9600",
                    "仅 RTU 串口需要。",
                    "9600", Group: "基础"),
                new AcquisitionServerFieldDescriptor("unitId", "默认从站地址", AcquisitionServerFieldTypes.Number, false,
                    "1",
                    "节点未单独指定时使用的从站地址。",
                    "1", Group: "基础")
            ],
            [
                new AcquisitionServerFieldDescriptor("slaveId", "从站地址", AcquisitionServerFieldTypes.Number, false,
                    "1",
                    "留空时使用服务器上的默认从站地址。"),
                new AcquisitionServerFieldDescriptor("registerDataType", "寄存器数据类型", AcquisitionServerFieldTypes.Select,
                    false,
                    HelperText: "按设备手册选择对应类型。",
                    DefaultValue: "Int16",
                    Options: ["Int16", "UInt16", "Int32", "UInt32", "Float32", "Float64", "Bool"]),
                new AcquisitionServerFieldDescriptor("scale", "比例系数", AcquisitionServerFieldTypes.Number, false,
                    "1",
                    "原始值乘以该系数得到工程值。",
                    "1")
            ]),
        new(
            Mqtt,
            "MQTT",
            "MQTT 消息总线，按 Topic 订阅采集点的最新消息。",
            "Broker 地址",
            "例如 mqtt://broker.local:1883",
            "mqtt://broker.local:1883",
            "Topic",
            "例如 factory/steam/flow",
            "订阅该 Topic 即可获得该采集点的最新值。",
            false,
            null,
            null,
            null,
            [
                new AcquisitionServerFieldDescriptor("clientId", "ClientId", AcquisitionServerFieldTypes.Text, false,
                    "dap-collector",
                    "MQTT 客户端标识。",
                    "dap-collector", Group: "基础"),
                new AcquisitionServerFieldDescriptor("username", "用户名", AcquisitionServerFieldTypes.Text, false,
                    HelperText: "可选。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("password", "密码", AcquisitionServerFieldTypes.Password, false,
                    HelperText: "可选。", Group: "认证")
            ],
            [
                new AcquisitionServerFieldDescriptor("qos", "QoS", AcquisitionServerFieldTypes.Select, false,
                    HelperText: "订阅服务质量。",
                    DefaultValue: "0",
                    Options: ["0", "1", "2"]),
                new AcquisitionServerFieldDescriptor("retain", "保留消息", AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "是否订阅 retain 消息。",
                    DefaultValue: "false")
            ]),
        new(
            OpcDa,
            "OPC DA",
            "OPC DA 经典接口，通过 OPC Server 浏览采集点。",
            "OPC Server 地址",
            "例如 opcda://compressor/station-03",
            "opcda://compressor/station-03",
            "Item ID",
            "例如 Channel1.Device1.Tag001",
            "OPC Server 中可浏览到的 Item 标识。",
            false,
            null,
            null,
            null,
            [
                new AcquisitionServerFieldDescriptor("progId", "ProgID", AcquisitionServerFieldTypes.Text, false,
                    "OPC.Server",
                    "OPC Server 的 ProgID。",
                    "OPC.Server", Group: "基础"),
                new AcquisitionServerFieldDescriptor("clsId", "CLSID", AcquisitionServerFieldTypes.Text, false,
                    HelperText: "OPC Server 的 CLSID。", Group: "基础")
            ],
            [
                new AcquisitionServerFieldDescriptor("groupName", "组名", AcquisitionServerFieldTypes.Text, false,
                    HelperText: "OPC 分组名称，可留空。")
            ]),
        new(
            HttpApi,
            "HTTP API",
            "通用 HTTP 接口，需配置完整请求路径，支持调用测试并读取返回值。",
            "API 基地址",
            "例如 http://api.local:8080（只填到端口，不要带接口路径）",
            "http://api.local:8080",
            "数据标识",
            "例如 steam_flow",
            "返回报文中定位该采集点数值所使用的字段名或 JSON 路径。",
            true,
            "接口请求路径",
            "例如 /api/metrics/current（相对基地址的完整路径）",
            "/",
            [
                new AcquisitionServerFieldDescriptor("httpMethod", "请求方法", AcquisitionServerFieldTypes.Select, false,
                    HelperText: "接口测试使用的 HTTP 方法。",
                    DefaultValue: "GET",
                    Options: ["GET", "POST"], Group: "请求"),
                new AcquisitionServerFieldDescriptor("apiKey", "API Key", AcquisitionServerFieldTypes.Password, false,
                    HelperText: "将以 X-Api-Key 请求头发送。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("authHeaderName", "认证请求头名称", AcquisitionServerFieldTypes.Text,
                    false,
                    "Authorization",
                    "填写完整的请求头名称。",
                    "Authorization", Group: "认证"),
                new AcquisitionServerFieldDescriptor("authToken", "认证令牌", AcquisitionServerFieldTypes.Password, false,
                    HelperText: "将以 Bearer 方式填充到认证请求头。", Group: "认证"),
                new AcquisitionServerFieldDescriptor("requestBody", "请求体模板", AcquisitionServerFieldTypes.Text, false,
                    HelperText: "POST 时的 JSON 请求体，可用 {identifier} 占位。", Group: "请求"),
                new AcquisitionServerFieldDescriptor("responseValuePath", "取值路径", AcquisitionServerFieldTypes.Text,
                    false,
                    "data.value",
                    "从返回报文中取值的 JSON 路径。", Group: "解析"),
                new AcquisitionServerFieldDescriptor("timeoutSeconds", "超时秒数", AcquisitionServerFieldTypes.Number,
                    false,
                    "15",
                    "单次请求超时时间。",
                    "15", Group: "请求"),
                new AcquisitionServerFieldDescriptor("ignoreCertificateErrors", "忽略证书错误",
                    AcquisitionServerFieldTypes.Boolean, false,
                    HelperText: "自签名证书场景下开启。",
                    DefaultValue: "false", Group: "请求")
            ],
            [
                new AcquisitionServerFieldDescriptor("apiPath", "接口请求路径", AcquisitionServerFieldTypes.Text, true,
                    "/api/metrics/current",
                    "相对服务器基地址的完整路径，接口测试会直接访问该地址。")
            ])
    ];

    /// <summary>
    /// 获取全部采集服务器类型描述。
    /// </summary>
    public static IReadOnlyList<AcquisitionServerTypeDescriptor> Descriptors => AllDescriptors;

    /// <summary>
    /// 按类型键查找服务器类型描述。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <returns>类型描述，未注册时返回 <see langword="null"/>。</returns>
    public static AcquisitionServerTypeDescriptor? Find(string? serverType)
    {
        if (string.IsNullOrWhiteSpace(serverType))
        {
            return null;
        }

        var normalized = serverType.Trim();
        return AllDescriptors.FirstOrDefault(descriptor =>
            string.Equals(descriptor.ServerType, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断服务器类型是否已注册。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <returns>已注册返回 <see langword="true"/>。</returns>
    public static bool IsKnownType(string? serverType)
    {
        return Find(serverType) is not null;
    }

    /// <summary>
    /// 获取指定类型的服务器级配置字段。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <returns>字段描述集合。</returns>
    public static IReadOnlyCollection<AcquisitionServerFieldDescriptor> GetServerFields(string serverType)
    {
        return Find(serverType)?.ServerFields ?? [];
    }

    /// <summary>
    /// 获取指定类型某个服务器级字段的登记默认值。
    /// 采集服务器信息统一保存在数据表中，运行时缺失的字段回落到这里登记的默认值。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="key">字段键。</param>
    /// <returns>默认值，未登记时返回空字符串。</returns>
    public static string GetServerFieldDefault(string serverType, string key)
    {
        return GetServerFields(serverType)
                   .FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
                   ?.DefaultValue ??
               string.Empty;
    }

    /// <summary>
    /// 获取指定类型的节点级配置字段。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <returns>字段描述集合。</returns>
    public static IReadOnlyCollection<AcquisitionServerFieldDescriptor> GetNodeFields(string serverType)
    {
        return Find(serverType)?.NodeFields ?? [];
    }

    /// <summary>
    /// 判断配置键是否属于该类型的服务器级密钥字段。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="key">配置键。</param>
    /// <returns>是密钥字段返回 <see langword="true"/>。</returns>
    public static bool IsServerSecret(string serverType, string key)
    {
        return GetServerFields(serverType).Any(field =>
            field.FieldType == AcquisitionServerFieldTypes.Password &&
            string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 校验服务器配置，返回全部错误说明。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="address">服务器基础地址。</param>
    /// <param name="configuration">配置项。</param>
    /// <returns>错误说明集合，通过时为空集合。</returns>
    public static IReadOnlyCollection<string> ValidateServer(
        string serverType,
        string? address,
        IReadOnlyDictionary<string, string> configuration)
    {
        AcquisitionServerTypeDescriptor? descriptor = Find(serverType);
        if (descriptor is null)
        {
            return [$"不支持的采集服务器类型：{serverType}。"];
        }

        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(address))
        {
            errors.Add($"{descriptor.AddressLabel}不能为空。");
        }
        else
        {
            errors.AddRange(ValidateAddress(address, descriptor));
        }

        foreach (AcquisitionServerFieldDescriptor field in descriptor.ServerFields.Where(field => field.IsRequired))
        {
            if (!configuration.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field.Label}不能为空。");
            }
        }

        return errors;
    }

    /// <summary>
    /// 校验节点采集信息，返回全部错误说明。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="identifier">节点数据标识。</param>
    /// <param name="nodeConfiguration">节点级配置。</param>
    /// <returns>错误说明集合，通过时为空集合。</returns>
    public static IReadOnlyCollection<string> ValidateNode(
        string serverType,
        string? identifier,
        IReadOnlyDictionary<string, string> nodeConfiguration)
    {
        AcquisitionServerTypeDescriptor? descriptor = Find(serverType);
        if (descriptor is null)
        {
            return [$"不支持的采集服务器类型：{serverType}。"];
        }

        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(identifier))
        {
            errors.Add($"{descriptor.IdentifierLabel}不能为空。");
        }

        foreach (AcquisitionServerFieldDescriptor field in descriptor.NodeFields.Where(field => field.IsRequired))
        {
            if (!nodeConfiguration.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field.Label}不能为空。");
            }
        }

        return errors;
    }

    /// <summary>
    /// 规范化服务器基础地址，去除末尾斜杠。
    /// </summary>
    /// <param name="address">原始地址。</param>
    /// <returns>规范化后的地址。</returns>
    public static string NormalizeAddress(string? address)
    {
        return string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim().TrimEnd('/');
    }

    /// <summary>
    /// 合并请求配置与默认值，忽略空值键。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="configuration">请求配置。</param>
    /// <returns>合并后的配置。</returns>
    public static Dictionary<string, string> MergeWithDefaults(
        string serverType,
        IReadOnlyDictionary<string, string>? configuration)
    {
        Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);

        foreach (AcquisitionServerFieldDescriptor field in GetServerFields(serverType))
        {
            if (!string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                merged[field.Key] = field.DefaultValue;
            }
        }

        if (configuration is not null)
        {
            foreach (KeyValuePair<string, string> item in configuration)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    merged[item.Key.Trim()] = item.Value ?? string.Empty;
                }
            }
        }

        return merged;
    }

    private static IEnumerable<string> ValidateAddress(string address, AcquisitionServerTypeDescriptor descriptor)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            yield return $"{descriptor.AddressLabel}不是合法的绝对地址。";
            yield break;
        }

        if (!SupportedAddressSchemes.Contains(uri.Scheme))
        {
            yield return $"{descriptor.AddressLabel}仅支持 http、https、tcp 或 mqtt 协议，当前为 {uri.Scheme}。";
        }

        // 地址只应到主机、端口与上下文路径；填入接口路径会在运行时与接口路径重复拼接。
        // 若地址尾部与该类型登记的接口路径前缀重复，直接拦截以避免 404。
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0 && LooksLikeInterfacePath(path))
        {
            yield return
                $"{descriptor.AddressLabel}只填到主机和端口，不要包含接口路径；接口路径请在下方接口配置中单独填写。";
        }
    }

    private static bool LooksLikeInterfacePath(string path)
    {
        // 多段路径一定是误填；单段路径中包含 api、rest、v1 等接口特征时同样视为误填。
        if (path.Contains('/'))
        {
            return true;
        }

        string[] apiPrefixHints = ["api", "rest", "service", "endpoint", "query"];
        return apiPrefixHints.Any(hint =>
                   path.Contains(hint, StringComparison.OrdinalIgnoreCase)) ||
               (path.StartsWith("v", StringComparison.OrdinalIgnoreCase) &&
                path.Length > 1 &&
                path.Skip(1).All(char.IsDigit));
    }
}

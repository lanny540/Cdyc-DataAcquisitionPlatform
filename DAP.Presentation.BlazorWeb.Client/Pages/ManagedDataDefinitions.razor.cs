using System.Text.Json;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示后台数据定义管理页面的交互逻辑。
/// </summary>
public partial class ManagedDataDefinitions
{
    private const string AllTypeFilter = "全部";
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private readonly List<ManagedDataDefinitionDto> _definitions = [];
    private readonly string[] _acquisitionTypeOptions = ["Modbus", "Historian API", "MQTT", "OPC DA", "HTTP API"];
    private Guid? _editingId;
    private string _searchTerm = string.Empty;
    private string _typeFilter = AllTypeFilter;
    private bool _isLoading;
    private bool _isDeleting;
    private bool _isEditorOpen;
    private Guid? _executingDefinitionId;
    private bool _isCollectExecution;
    private bool _isTestingConnection;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private ManagedDataDefinitionDto? _pendingDeleteDefinition;
    private ManagedDataExecutionResultDto? _lastExecutionResult;
    private ManagedDataConnectionTestResultDto? _lastConnectionTestResult;
    private Guid _editorRenderKey = Guid.NewGuid();

    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _acquisitionType = "Modbus";
    private string _department = string.Empty;
    private string _processCode = string.Empty;
    private string _dataCategory = "电力数据";
    private string _unit = string.Empty;
    private string _description = string.Empty;
    private int _collectionIntervalSeconds = 60;
    private bool _isEnabled = true;

    private string _modbusConnectionAddress = "COM3";
    private string _modbusProtocolVariant = "RTU";
    private string _modbusSlaveId = "1";
    private string _modbusRegisterAddress = string.Empty;
    private string _modbusBaudRate = "9600";

    private string _historianBaseAddress = string.Empty;
    private string _historianTagName = string.Empty;
    private string _historianQueryMode = "CurrentValue";
    private string _historianClientId = string.Empty;
    private string _historianClientSecret = string.Empty;
    private bool _historianUseMockResponses;

    private string _mqttBrokerAddress = string.Empty;
    private string _mqttTopic = string.Empty;
    private string _mqttQoS = "0";

    private string _opcServerAddress = string.Empty;
    private string _opcItemId = string.Empty;
    private string _opcGroupName = string.Empty;

    private string _httpApiBaseAddress = string.Empty;
    private string _httpMetricIdentifier = string.Empty;
    private string _httpPath = string.Empty;

    private List<EditableKeyValueRow> _businessTagRows = [];
    private List<EditableKeyValueRow> _extraConfigurationRows = [];

    private IEnumerable<ManagedDataDefinitionDto> FilteredDefinitions => _definitions
        .Where(item => string.IsNullOrWhiteSpace(_typeFilter) ||
                       string.Equals(_typeFilter, AllTypeFilter, StringComparison.Ordinal) ||
                       string.Equals(item.AcquisitionType, _typeFilter, StringComparison.Ordinal))
        .Where(item => string.IsNullOrWhiteSpace(_searchTerm) ||
                       item.Code.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Name.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Department.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.ProcessCode.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.DataCategory.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Identifier.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.AcquisitionType)
        .ThenBy(item => item.Code);

    private int EnabledDefinitionCount => _definitions.Count(item => item.IsEnabled);

    private int HistorianDefinitionCount => _definitions.Count(item =>
        string.Equals(item.AcquisitionType, "Historian API", StringComparison.Ordinal));

    private int ModbusDefinitionCount => _definitions.Count(item =>
        string.Equals(item.AcquisitionType, "Modbus", StringComparison.Ordinal));

    private string EditorTitle => _editingId.HasValue ? "编辑后台数据定义" : "新增后台数据定义";

    private string EditorDescription => _editingId.HasValue
        ? "正在修改已有的数据定义，保存后会直接更新数据库中的公共字段和协议配置。"
        : "创建新的后台数据定义，公共标识字段与协议配置会一起保存到平台数据库。";

    private string SaveButtonText => _editingId.HasValue ? "保存定义" : "创建定义";

    private bool IsExecuting => _executingDefinitionId.HasValue;
    private bool IsBusy => IsExecuting || _isTestingConnection;

    private Color LatestExecutionColor => _lastExecutionResult?.Success == true ? Color.Success : Color.Error;
    private Color LatestConnectionTestColor => _lastConnectionTestResult?.Success == true ? Color.Success : Color.Error;

    private string ConfigurationPreviewJson => BuildJson(
        CreateProtocolConfigurationDictionary().Concat(CreateCustomDictionary(_extraConfigurationRows)));

    private string BusinessTagsPreviewJson => BuildJson(CreateCustomDictionary(_businessTagRows));

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

        try
        {
            _definitions.Clear();
            _definitions.AddRange(await PlatformApiClient.GetManagedDataDefinitionsAsync());
        }
        catch (Exception ex)
        {
            _message = $"后台数据定义加载失败：{ex.Message}";
            _messageSeverity = Severity.Warning;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task EditAsync(ManagedDataDefinitionDto definition)
    {
        ResetEditor();

        _editingId = definition.Id;
        _code = definition.Code;
        _name = definition.Name;
        _acquisitionType = definition.AcquisitionType;
        _department = definition.Department;
        _processCode = definition.ProcessCode;
        _dataCategory = definition.DataCategory;
        _unit = definition.Unit;
        _description = definition.Description;
        _collectionIntervalSeconds = definition.CollectionIntervalSeconds;
        _isEnabled = definition.IsEnabled;

        Dictionary<string, string> configuration = ParseJsonDictionary(definition.ConfigurationJson);
        Dictionary<string, string> businessTags = ParseJsonDictionary(definition.BusinessTagsJson);

        PopulateProtocolFields(definition, configuration);
        _businessTagRows = businessTags.Select(item => new EditableKeyValueRow(item.Key, item.Value)).ToList();
        _extraConfigurationRows = CreateRemainingConfigurationRows(configuration);

        _message = $"已载入 {definition.Code}，可以继续修改协议配置和业务标识。";
        _messageSeverity = Severity.Info;
        ResetEditorRenderKey();
        _isEditorOpen = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveAsync()
    {
        try
        {
            ManagedDataDefinitionUpsertRequest? request = BuildCurrentRequest();
            if (request is null)
            {
                return;
            }

            ManagedDataDefinitionDto savedDefinition =
                await PlatformApiClient.UpsertManagedDataDefinitionAsync(request);

            _message = $"后台数据定义 {savedDefinition.Code} 已保存。";
            _messageSeverity = Severity.Success;
            _isEditorOpen = false;
            ResetEditor();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"后台数据定义保存失败：{ex.Message}";
            _messageSeverity = Severity.Error;
        }
    }

    private async Task TestConnectionAsync()
    {
        ManagedDataDefinitionUpsertRequest? request = BuildCurrentRequest();
        if (request is null)
        {
            return;
        }

        _isTestingConnection = true;
        _message = null;

        try
        {
            _lastConnectionTestResult = await PlatformApiClient.TestManagedDataConnectionAsync(request);
            SetMessage(
                _lastConnectionTestResult.StatusMessage,
                _lastConnectionTestResult.Success ? Severity.Success : Severity.Warning);
        }
        catch (Exception ex)
        {
            SetMessage($"连接测试失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _isTestingConnection = false;
        }
    }

    private void RequestDelete(ManagedDataDefinitionDto definition)
    {
        _pendingDeleteDefinition = definition;
    }

    private void CancelDelete()
    {
        if (_isDeleting)
        {
            return;
        }

        _pendingDeleteDefinition = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteDefinition is null)
        {
            return;
        }

        ManagedDataDefinitionDto definition = _pendingDeleteDefinition;
        _isDeleting = true;

        try
        {
            var deleted = await PlatformApiClient.DeleteManagedDataDefinitionAsync(definition.Id);
            if (!deleted)
            {
                SetMessage($"后台数据定义 {definition.Code} 不存在或已被删除。", Severity.Warning);
                return;
            }

            if (_editingId == definition.Id)
            {
                ResetEditor();
                _isEditorOpen = false;
            }

            SetMessage($"后台数据定义 {definition.Code} 已删除。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"后台数据定义删除失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _isDeleting = false;
            _pendingDeleteDefinition = null;
        }
    }

    private void OpenCreateDrawer()
    {
        ResetEditor();
        _message = null;
        _isEditorOpen = true;
    }

    private async Task DebugReadAsync(ManagedDataDefinitionDto definition)
    {
        await ExecuteDefinitionAsync(definition, false);
    }

    private async Task CollectAsync(ManagedDataDefinitionDto definition)
    {
        await ExecuteDefinitionAsync(definition, true);
    }

    private void OpenDetails(ManagedDataDefinitionDto definition)
    {
        NavigationManager.NavigateTo($"/managed-data-definitions/{definition.Id}");
    }

    private void CancelEditor()
    {
        ResetEditor();
        _isEditorOpen = false;
    }

    private void CloseEditor()
    {
        _isEditorOpen = false;
    }

    private void ResetEditor()
    {
        _editingId = null;
        _code = string.Empty;
        _name = string.Empty;
        _acquisitionType = "Modbus";
        _department = string.Empty;
        _processCode = string.Empty;
        _dataCategory = "电力数据";
        _unit = string.Empty;
        _description = string.Empty;
        _collectionIntervalSeconds = 60;
        _isEnabled = true;

        _modbusConnectionAddress = "COM3";
        _modbusProtocolVariant = "RTU";
        _modbusSlaveId = "1";
        _modbusRegisterAddress = string.Empty;
        _modbusBaudRate = "9600";

        _historianBaseAddress = string.Empty;
        _historianTagName = string.Empty;
        _historianQueryMode = "CurrentValue";
        _historianClientId = string.Empty;
        _historianClientSecret = string.Empty;
        _historianUseMockResponses = false;

        _mqttBrokerAddress = string.Empty;
        _mqttTopic = string.Empty;
        _mqttQoS = "0";

        _opcServerAddress = string.Empty;
        _opcItemId = string.Empty;
        _opcGroupName = string.Empty;

        _httpApiBaseAddress = string.Empty;
        _httpMetricIdentifier = string.Empty;
        _httpPath = string.Empty;

        _businessTagRows = [];
        _extraConfigurationRows = [];
        _lastConnectionTestResult = null;
        ResetEditorRenderKey();
    }

    private void AddBusinessTagRow()
    {
        _businessTagRows.Add(new EditableKeyValueRow());
    }

    private void RemoveBusinessTagRow(EditableKeyValueRow row)
    {
        _businessTagRows.Remove(row);
    }

    private void AddExtraConfigurationRow()
    {
        _extraConfigurationRows.Add(new EditableKeyValueRow());
    }

    private void RemoveExtraConfigurationRow(EditableKeyValueRow row)
    {
        _extraConfigurationRows.Remove(row);
    }

    private async Task ExecuteDefinitionAsync(ManagedDataDefinitionDto definition, bool isCollect)
    {
        _executingDefinitionId = definition.Id;
        _isCollectExecution = isCollect;
        _message = null;

        try
        {
            _lastExecutionResult = isCollect
                ? await PlatformApiClient.CollectManagedDataDefinitionAsync(definition.Id)
                : await PlatformApiClient.DebugReadManagedDataDefinitionAsync(definition.Id);

            if (isCollect && _lastExecutionResult.Success)
            {
                await LoadAsync();
            }

            SetMessage(
                _lastExecutionResult.Success
                    ? _lastExecutionResult.StatusMessage
                    : $"执行失败：{_lastExecutionResult.ErrorMessage ?? _lastExecutionResult.StatusMessage}",
                _lastExecutionResult.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"执行失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _executingDefinitionId = null;
            _isCollectExecution = false;
        }
    }

    private ManagedDataDefinitionUpsertRequest? BuildCurrentRequest()
    {
        var connectionAddress = ResolveConnectionAddress();
        var identifier = ResolveIdentifier();

        if (string.IsNullOrWhiteSpace(connectionAddress) || string.IsNullOrWhiteSpace(identifier))
        {
            SetMessage("当前采集方式下的连接地址和数据标识不能为空。", Severity.Warning);
            return null;
        }

        if (_collectionIntervalSeconds <= 0)
        {
            SetMessage("采集频次必须大于 0 秒。", Severity.Warning);
            return null;
        }

        return new ManagedDataDefinitionUpsertRequest(
            _editingId,
            _code,
            _name,
            _acquisitionType,
            connectionAddress,
            identifier,
            _department,
            _processCode,
            _dataCategory,
            _unit,
            _description,
            ConfigurationPreviewJson,
            BusinessTagsPreviewJson,
            _collectionIntervalSeconds,
            _isEnabled);
    }

    private bool IsExecutingDefinition(Guid id, bool isCollect)
    {
        return _executingDefinitionId == id && _isCollectExecution == isCollect;
    }

    private static string GetIntervalDisplayText(int intervalSeconds)
    {
        return intervalSeconds >= 60 && intervalSeconds % 60 == 0
            ? $"每 {intervalSeconds / 60} 分钟执行 1 次"
            : $"每 {intervalSeconds} 秒执行 1 次";
    }

    private Color GetTypeChipColor(string acquisitionType)
    {
        return acquisitionType switch
        {
            "Modbus" => Color.Primary,
            "Historian API" => Color.Info,
            "MQTT" => Color.Secondary,
            "OPC DA" => Color.Warning,
            "HTTP API" => Color.Success,
            _ => Color.Default
        };
    }

    private Dictionary<string, string> CreateProtocolConfigurationDictionary()
    {
        return _acquisitionType switch
        {
            "Modbus" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["protocolVariant"] = _modbusProtocolVariant,
                ["slaveId"] = _modbusSlaveId,
                ["registerAddress"] = _modbusRegisterAddress,
                ["baudRate"] = _modbusBaudRate,
                ["deviceAddress"] = _modbusConnectionAddress
            },
            "Historian API" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tagName"] = _historianTagName,
                ["queryMode"] = _historianQueryMode,
                ["baseAddress"] = _historianBaseAddress,
                ["clientId"] = _historianClientId,
                ["clientSecret"] = _historianClientSecret,
                ["useMockResponses"] = _historianUseMockResponses.ToString()
            },
            "MQTT" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["topic"] = _mqttTopic,
                ["qos"] = _mqttQoS,
                ["brokerAddress"] = _mqttBrokerAddress
            },
            "OPC DA" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["itemId"] = _opcItemId,
                ["groupName"] = _opcGroupName,
                ["serverAddress"] = _opcServerAddress
            },
            "HTTP API" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["metricIdentifier"] = _httpMetricIdentifier,
                ["path"] = _httpPath,
                ["baseAddress"] = _httpApiBaseAddress
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private Dictionary<string, string> CreateCustomDictionary(IEnumerable<EditableKeyValueRow> rows)
    {
        Dictionary<string, string> dictionary = new(StringComparer.OrdinalIgnoreCase);

        foreach (EditableKeyValueRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            dictionary[row.Key.Trim()] = row.Value.Trim();
        }

        return dictionary;
    }

    private List<EditableKeyValueRow> CreateRemainingConfigurationRows(Dictionary<string, string> configuration)
    {
        string[] reservedKeys = _acquisitionType switch
        {
            "Modbus" => ["protocolVariant", "slaveId", "registerAddress", "baudRate", "deviceAddress"],
            "Historian API" => ["tagName", "queryMode", "baseAddress", "clientId", "clientSecret", "useMockResponses"],
            "MQTT" => ["topic", "qos", "brokerAddress"],
            "OPC DA" => ["itemId", "groupName", "serverAddress"],
            "HTTP API" => ["metricIdentifier", "path", "baseAddress"],
            _ => []
        };

        return configuration
            .Where(item => !reservedKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
            .Select(item => new EditableKeyValueRow(item.Key, item.Value))
            .ToList();
    }

    private void PopulateProtocolFields(ManagedDataDefinitionDto definition, Dictionary<string, string> configuration)
    {
        _modbusConnectionAddress = GetValueOrDefault(configuration, "deviceAddress", definition.ConnectionAddress);
        _modbusProtocolVariant = GetValueOrDefault(configuration, "protocolVariant", "RTU");
        _modbusSlaveId = GetValueOrDefault(configuration, "slaveId", "1");
        _modbusRegisterAddress = GetValueOrDefault(configuration, "registerAddress", definition.Identifier);
        _modbusBaudRate = GetValueOrDefault(configuration, "baudRate", "9600");

        _historianBaseAddress = GetValueOrDefault(configuration, "baseAddress", definition.ConnectionAddress);
        _historianTagName = GetValueOrDefault(configuration, "tagName", definition.Identifier);
        _historianQueryMode = GetValueOrDefault(configuration, "queryMode", "CurrentValue");
        _historianClientId = GetValueOrDefault(configuration, "clientId", string.Empty);
        _historianClientSecret = GetValueOrDefault(configuration, "clientSecret", string.Empty);
        _historianUseMockResponses = bool.TryParse(
            GetValueOrDefault(configuration, "useMockResponses", "False"),
            out var useMockResponses) && useMockResponses;

        _mqttBrokerAddress = GetValueOrDefault(configuration, "brokerAddress", definition.ConnectionAddress);
        _mqttTopic = GetValueOrDefault(configuration, "topic", definition.Identifier);
        _mqttQoS = GetValueOrDefault(configuration, "qos", "0");

        _opcServerAddress = GetValueOrDefault(configuration, "serverAddress", definition.ConnectionAddress);
        _opcItemId = GetValueOrDefault(configuration, "itemId", definition.Identifier);
        _opcGroupName = GetValueOrDefault(configuration, "groupName", string.Empty);

        _httpApiBaseAddress = GetValueOrDefault(configuration, "baseAddress", definition.ConnectionAddress);
        _httpMetricIdentifier = GetValueOrDefault(configuration, "metricIdentifier", definition.Identifier);
        _httpPath = GetValueOrDefault(configuration, "path", string.Empty);
    }

    private string ResolveConnectionAddress()
    {
        return _acquisitionType switch
        {
            "Modbus" => _modbusConnectionAddress.Trim(),
            "Historian API" => _historianBaseAddress.Trim(),
            "MQTT" => _mqttBrokerAddress.Trim(),
            "OPC DA" => _opcServerAddress.Trim(),
            "HTTP API" => _httpApiBaseAddress.Trim(),
            _ => string.Empty
        };
    }

    private string ResolveIdentifier()
    {
        return _acquisitionType switch
        {
            "Modbus" => _modbusRegisterAddress.Trim(),
            "Historian API" => _historianTagName.Trim(),
            "MQTT" => _mqttTopic.Trim(),
            "OPC DA" => _opcItemId.Trim(),
            "HTTP API" => _httpMetricIdentifier.Trim(),
            _ => string.Empty
        };
    }

    private static string BuildJson(IEnumerable<KeyValuePair<string, string>> items)
    {
        Dictionary<string, string> dictionary = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            dictionary[item.Key.Trim()] = item.Value.Trim();
        }

        return dictionary.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(dictionary.OrderBy(item => item.Key).ToDictionary(), JsonSerializerOptions);
    }

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

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

    private void SetMessage(string message, Severity severity)
    {
        _message = message;
        _messageSeverity = severity;
    }

    private void ResetEditorRenderKey()
    {
        _editorRenderKey = Guid.NewGuid();
    }

    private static string GetValueOrDefault(IReadOnlyDictionary<string, string> dictionary, string key, string fallback)
    {
        return dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private sealed class EditableKeyValueRow
    {
        public EditableKeyValueRow()
        {
        }

        public EditableKeyValueRow(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}

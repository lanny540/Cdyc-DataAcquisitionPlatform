using System.Text.Json;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// 采集节点整页编辑视图，供节点列表页与节点详情页共用。
/// 与采集服务器页一致：不再使用抽屉弹窗，由父页面按编辑状态条件渲染整页表单。
/// 表单以「先选采集服务器、再补点位信息」为前提，字段由 <see cref="AcquisitionServerCatalog"/> 驱动。
/// </summary>
public partial class ManagedDataDefinitionEditor : ComponentBase, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    /// <summary>正在编辑的节点标识，为空表示新增。</summary>
    [Parameter]
    public Guid? DefinitionId { get; set; }

    /// <summary>保存成功回调，参数为保存后的节点；父页面收到后应关闭编辑视图并刷新列表。</summary>
    [Parameter]
    public EventCallback<ManagedDataDefinitionDto> OnSaved { get; set; }

    /// <summary>取消编辑回调；父页面收到后应关闭编辑视图回到列表。</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    private readonly List<AcquisitionServerDto> _servers = [];
    private readonly List<AcquisitionServerTypeDescriptor> _serverTypes = [];
    private readonly List<EditableKeyValueRow> _businessTagRows = [];
    private readonly List<EditableKeyValueRow> _extraConfigurationRows = [];
    private readonly Dictionary<string, string> _nodeConfiguration = new(StringComparer.OrdinalIgnoreCase);

    private Guid? _definitionId;
    private bool _initialized;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isTestingConnection;
    private string? _validationMessage;
    private ManagedDataConnectionTestResultDto? _lastConnectionTestResult;

    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _department = string.Empty;
    private string _processCode = string.Empty;
    private string _dataCategory = "电力数据";
    private string _unit = string.Empty;
    private string _description = string.Empty;
    private int _collectionIntervalSeconds = 60;
    private bool _isEnabled = true;

    private Guid? _selectedServerId;
    private string _identifier = string.Empty;

    private AcquisitionServerDto? SelectedServer => _selectedServerId.HasValue
        ? _servers.FirstOrDefault(item => item.Id == _selectedServerId.Value)
        : null;

    private AcquisitionServerTypeDescriptor? CurrentServerType => SelectedServer is null
        ? null
        : _serverTypes.FirstOrDefault(item =>
            string.Equals(item.ServerType, SelectedServer.ServerType, StringComparison.Ordinal));

    private IEnumerable<AcquisitionServerFieldDescriptor> CurrentNodeFields =>
        CurrentServerType?.NodeFields ?? [];

    private string EditorTitle => _definitionId.HasValue ? "编辑采集节点" : "新增采集节点";

    private string EditorDescription => _definitionId.HasValue
        ? "选择采集服务器后，只需修改该节点自身的采集信息。"
        : "先选择采集服务器，再补充点位标识等必要信息即可完成配置。";

    private string SaveButtonText => _definitionId.HasValue ? "保存节点" : "创建节点";

    private bool IsBusy => _isLoading || _isSaving || _isTestingConnection;

    private string ConfigurationPreviewJson => BuildJson(
        CreateCustomDictionary(_nodeConfiguration).Concat(CreateCustomDictionary(_extraConfigurationRows)));

    private string BusinessTagsPreviewJson => BuildJson(CreateCustomDictionary(_businessTagRows));

    /// <summary>
    /// 首次渲染或编辑目标变化时载入最新表单内容。
    /// 组件由父页面按编辑状态条件渲染：进入编辑视图即重建实例，离开即销毁。
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_initialized && _definitionId == DefinitionId)
        {
            return;
        }

        _definitionId = DefinitionId;
        _initialized = true;
        await LoadCatalogAsync();
        await LoadEditorStateAsync();
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            if (_servers.Count == 0)
            {
                _servers.AddRange(await PlatformApiClient.GetAcquisitionServersAsync());
            }

            if (_serverTypes.Count == 0)
            {
                _serverTypes.AddRange(await PlatformApiClient.GetAcquisitionServerTypesAsync());
            }
        }
        catch (Exception ex)
        {
            _validationMessage = $"采集服务器列表加载失败：{UserFacingErrors.Describe(ex)}";
        }
    }

    private async Task LoadEditorStateAsync()
    {
        _validationMessage = null;
        _lastConnectionTestResult = null;
        _isLoading = true;

        try
        {
            await LoadDefinitionAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadDefinitionAsync()
    {
        ResetFields();

        if (!_definitionId.HasValue)
        {
            return;
        }

        try
        {
            ManagedDataDefinitionDetailsDto details =
                await PlatformApiClient.GetManagedDataDefinitionDetailsAsync(_definitionId.Value, 1);

            ManagedDataDefinitionDto definition = details.Definition;
            _code = definition.Code;
            _name = definition.Name;
            _department = definition.Department;
            _processCode = definition.ProcessCode;
            _dataCategory = definition.DataCategory;
            _unit = definition.Unit;
            _description = definition.Description;
            _collectionIntervalSeconds = definition.CollectionIntervalSeconds;
            _isEnabled = definition.IsEnabled;
            _identifier = definition.Identifier;

            _selectedServerId = definition.ServerConnectionId ??
                                _servers.FirstOrDefault(item =>
                                        string.Equals(item.ServerType, definition.AcquisitionType,
                                            StringComparison.Ordinal) &&
                                        string.Equals(item.Address, definition.ConnectionAddress,
                                            StringComparison.OrdinalIgnoreCase))
                                    ?.Id;

            Dictionary<string, string> nodeConfiguration = ParseJsonDictionary(definition.ConfigurationJson);
            _businessTagRows.Clear();
            _businessTagRows.AddRange(
                ParseJsonDictionary(definition.BusinessTagsJson)
                    .Select(item => new EditableKeyValueRow(item.Key, item.Value)));
            _extraConfigurationRows.Clear();
            _extraConfigurationRows.AddRange(
                CreateRemainingConfigurationRows(nodeConfiguration));
        }
        catch (Exception ex)
        {
            _validationMessage = $"节点配置载入失败：{UserFacingErrors.Describe(ex)}";
        }
    }

    private string GetNodeConfigValue(string key)
    {
        return _nodeConfiguration.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private void SetNodeConfigValue(string key, string value)
    {
        _nodeConfiguration[key] = value;
    }

    private void OnServerChanged(Guid? serverId)
    {
        _selectedServerId = serverId;
        _nodeConfiguration.Clear();
        _lastConnectionTestResult = null;

        // 切换服务器时套用该类型的节点字段默认值。
        foreach (AcquisitionServerFieldDescriptor field in CurrentNodeFields)
        {
            if (!string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                _nodeConfiguration[field.Key] = field.DefaultValue;
            }
        }
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

    private void ResetEditor()
    {
        ResetFields();
        _validationMessage = null;
        _lastConnectionTestResult = null;
    }

    private void ResetFields()
    {
        _code = string.Empty;
        _name = string.Empty;
        _department = string.Empty;
        _processCode = string.Empty;
        _dataCategory = "电力数据";
        _unit = string.Empty;
        _description = string.Empty;
        _collectionIntervalSeconds = 60;
        _isEnabled = true;
        _identifier = string.Empty;
        _selectedServerId = null;
        _nodeConfiguration.Clear();
        _businessTagRows.Clear();
        _extraConfigurationRows.Clear();
    }

    private async Task SaveAsync()
    {
        ManagedDataDefinitionUpsertRequest? request = BuildCurrentRequest();
        if (request is null)
        {
            return;
        }

        _isSaving = true;

        try
        {
            ManagedDataDefinitionDto saved =
                await PlatformApiClient.UpsertManagedDataDefinitionAsync(request);

            await OnSaved.InvokeAsync(saved);
        }
        catch (Exception ex)
        {
            _validationMessage = $"采集节点保存失败：{UserFacingErrors.Describe(ex)}";
        }
        finally
        {
            _isSaving = false;
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
        _validationMessage = null;

        try
        {
            _lastConnectionTestResult = await PlatformApiClient.TestManagedDataConnectionAsync(request);
        }
        catch (Exception ex)
        {
            _validationMessage = $"连接测试失败：{UserFacingErrors.Describe(ex)}";
        }
        finally
        {
            _isTestingConnection = false;
        }
    }

    /// <summary>取消编辑，由父页面关闭编辑视图回到列表。</summary>
    private Task Cancel()
    {
        return OnCancel.InvokeAsync();
    }

    private ManagedDataDefinitionUpsertRequest? BuildCurrentRequest()
    {
        AcquisitionServerDto? server = SelectedServer;
        if (server is null)
        {
            _validationMessage = "请先选择采集服务器，节点配置将复用服务器地址与鉴权信息。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_identifier))
        {
            _validationMessage = $"{CurrentServerType?.IdentifierLabel ?? "数据标识"}不能为空。";
            return null;
        }

        // 与服务端 ValidateManagedDataDefinitionRequest 的必填项保持一致，
        // 能在表单层直接给出原因的就不留到 400 校验错误。
        if (string.IsNullOrWhiteSpace(_name))
        {
            _validationMessage = "数据名称不能为空。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_department))
        {
            _validationMessage = "归属部门不能为空。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_processCode))
        {
            _validationMessage = "工程编号（工序）不能为空。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_dataCategory))
        {
            _validationMessage = "数据类别不能为空。";
            return null;
        }

        if (_collectionIntervalSeconds <= 0)
        {
            _validationMessage = "采集频次必须大于 0 秒。";
            return null;
        }

        _validationMessage = null;

        return new ManagedDataDefinitionUpsertRequest(
            _definitionId,
            _code,
            _name,
            server.ServerType,
            server.Address,
            _identifier,
            _department,
            _processCode,
            _dataCategory,
            _unit,
            _description,
            ConfigurationPreviewJson,
            BusinessTagsPreviewJson,
            _collectionIntervalSeconds,
            _isEnabled,
            server.Id);
    }

    /// <summary>
    /// 把不属于当前服务器类型专属字段的配置项归入「扩展协议字段」，避免编辑时被专属字段吞掉。
    /// </summary>
    private List<EditableKeyValueRow> CreateRemainingConfigurationRows(
        Dictionary<string, string> configuration)
    {
        HashSet<string> reservedKeys = CurrentNodeFields
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return configuration
            .Where(item => !reservedKeys.Contains(item.Key))
            .Select(item => new EditableKeyValueRow(item.Key, item.Value))
            .ToList();
    }

    private static Color GetServerTypeChipColor(string serverType)
    {
        return serverType switch
        {
            AcquisitionServerCatalog.Historian => Color.Info,
            AcquisitionServerCatalog.InfluxDb => Color.Secondary,
            AcquisitionServerCatalog.Modbus => Color.Primary,
            AcquisitionServerCatalog.Mqtt => Color.Success,
            AcquisitionServerCatalog.OpcDa => Color.Warning,
            AcquisitionServerCatalog.HttpApi => Color.Tertiary,
            _ => Color.Default
        };
    }

    private static Dictionary<string, string> CreateCustomDictionary(IEnumerable<EditableKeyValueRow> rows)
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

    private static Dictionary<string, string> CreateCustomDictionary(
        IReadOnlyDictionary<string, string> source)
    {
        Dictionary<string, string> dictionary = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> item in source)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            dictionary[item.Key.Trim()] = item.Value.Trim();
        }

        return dictionary;
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
            : JsonSerializer.Serialize(dictionary.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(),
                JsonSerializerOptions);
    }

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            Dictionary<string, JsonElement>? rawDictionary =
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
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

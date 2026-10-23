using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示采集服务器配置页面的交互逻辑。
/// </summary>
public partial class AcquisitionServers
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private readonly List<AcquisitionServerDto> _servers = [];
    private readonly List<AcquisitionServerTypeDescriptor> _serverTypes = [];
    private readonly Dictionary<string, string> _configurationDraft = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _secretFields = new(StringComparer.OrdinalIgnoreCase);

    private Guid? _editingId;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isTesting;
    private bool _isEditorOpen;
    private bool _isDeleting;
    private bool _isDeleteConfirmOpen;
    private AcquisitionServerDto? _pendingDeleteServer;
    private AcquisitionServerProbeResultDto? _lastProbeResult;
    private Guid _testingServerId = Guid.Empty;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private string _code = string.Empty;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _serverType = AcquisitionServerCatalog.Historian;
    private string _address = string.Empty;
    private bool _isEnabled = true;
    private string _searchTerm = string.Empty;
    private string _typeFilter = "全部";

    private AcquisitionServerTypeDescriptor? CurrentType =>
        _serverTypes.FirstOrDefault(item =>
            string.Equals(item.ServerType, _serverType, StringComparison.Ordinal));

    private IEnumerable<AcquisitionServerDto> FilteredServers => _servers
        .Where(item => _typeFilter == "全部" ||
                       string.Equals(item.ServerType, _typeFilter, StringComparison.Ordinal))
        .Where(item => string.IsNullOrWhiteSpace(_searchTerm) ||
                       item.DisplayName.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Code.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase) ||
                       item.Address.Contains(_searchTerm.Trim(), StringComparison.OrdinalIgnoreCase));

    private int EnabledCount => _servers.Count(item => item.IsEnabled);

    private int BoundNodeCount => _servers.Sum(item => item.RelatedNodeCount);

    private int ReachableCount => _servers.Count(item => item.LastProbeStatus == "成功");

    private IEnumerable<string> TypeFilterOptions => _serverTypes
        .Select(item => item.ServerType)
        .Prepend("全部");

    private bool IsBusy => _isSaving || _isTesting;

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
            _servers.Clear();
            _servers.AddRange(await PlatformApiClient.GetAcquisitionServersAsync());

            if (_serverTypes.Count == 0)
            {
                _serverTypes.AddRange(await PlatformApiClient.GetAcquisitionServerTypesAsync());
            }
        }
        catch (Exception ex)
        {
            SetMessage($"采集服务器配置加载失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OpenCreateDrawer()
    {
        ResetEditor();
        _isEditorOpen = true;
    }

    private async Task EditAsync(AcquisitionServerDto server)
    {
        ResetEditor();

        _editingId = server.Id;
        _code = server.Code;
        _displayName = server.DisplayName;
        _description = server.Description;
        _serverType = server.ServerType;
        _address = server.Address;
        _isEnabled = server.IsEnabled;

        _configurationDraft.Clear();
        _secretFields.Clear();

        foreach (AcquisitionServerConfigValueDto configValue in server.Configuration)
        {
            _configurationDraft[configValue.Key] = configValue.Value;
            if (configValue.IsSecret)
            {
                _secretFields.Add(configValue.Key);
            }
        }

        _lastProbeResult = null;
        _isEditorOpen = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        _message = null;

        try
        {
            AcquisitionServerDto saved = await PlatformApiClient.UpsertAcquisitionServerAsync(
                BuildRequest());

            SetMessage(
                _editingId.HasValue
                    ? $"采集服务器 {saved.DisplayName} 已更新。"
                    : $"采集服务器 {saved.DisplayName} 已创建，节点配置时可直接选择。",
                Severity.Success);

            _isEditorOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"采集服务器保存失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task TestDraftAsync()
    {
        _isTesting = true;
        _lastProbeResult = null;
        _message = null;

        try
        {
            AcquisitionServerProbeResultDto result = await PlatformApiClient.TestAcquisitionServerAsync(
                _editingId,
                new AcquisitionServerProbeRequest(
                    _serverType,
                    _address.Trim(),
                    new Dictionary<string, string>(_configurationDraft, StringComparer.OrdinalIgnoreCase),
                    _configurationDraft.TryGetValue("apiPath", out var apiPath) ? apiPath : null));

            _lastProbeResult = result;
            SetMessage(
                result.StatusMessage,
                result.Success ? Severity.Success : Severity.Warning);
        }
        catch (Exception ex)
        {
            SetMessage($"连通性测试失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isTesting = false;
        }
    }

    private async Task TestSavedServerAsync(AcquisitionServerDto server)
    {
        _testingServerId = server.Id;
        _message = null;

        try
        {
            AcquisitionServerProbeResultDto result = await PlatformApiClient.TestAcquisitionServerAsync(
                server.Id,
                new AcquisitionServerProbeRequest(
                    server.ServerType,
                    server.Address,
                    BuildConfigurationFromServer(server)));

            _lastProbeResult = result;
            SetMessage(
                $"{server.DisplayName}：{result.StatusMessage}",
                result.Success ? Severity.Success : Severity.Warning);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"{server.DisplayName} 连通性测试失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _testingServerId = Guid.Empty;
        }
    }

    private void RequestDelete(AcquisitionServerDto server)
    {
        _pendingDeleteServer = server;
        _isDeleteConfirmOpen = true;
    }

    private void CancelDelete()
    {
        _pendingDeleteServer = null;
        _isDeleteConfirmOpen = false;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteServer is null)
        {
            return;
        }

        AcquisitionServerDto server = _pendingDeleteServer;
        _isDeleting = true;

        try
        {
            await PlatformApiClient.DeleteAcquisitionServerAsync(server.Id);
            SetMessage($"采集服务器 {server.DisplayName} 已删除。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"采集服务器删除失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isDeleting = false;
            _pendingDeleteServer = null;
            _isDeleteConfirmOpen = false;
        }
    }

    private void CloseEditor()
    {
        _isEditorOpen = false;
    }

    private void CancelEditor()
    {
        ResetEditor();
        _isEditorOpen = false;
    }

    private void ResetEditor()
    {
        _editingId = null;
        _code = string.Empty;
        _displayName = string.Empty;
        _description = string.Empty;
        _serverType = AcquisitionServerCatalog.Historian;
        _address = string.Empty;
        _isEnabled = true;

        _configurationDraft.Clear();
        _secretFields.Clear();
        ApplyDefaultsForType(_serverType);
        _lastProbeResult = null;
    }

    private void OnServerTypeChanged(string value)
    {
        _serverType = value;
        _configurationDraft.Clear();
        _secretFields.Clear();
        ApplyDefaultsForType(_serverType);
        _lastProbeResult = null;
        _address = CurrentType?.DefaultAddress ?? string.Empty;
    }

    private void ApplyDefaultsForType(string serverType)
    {
        foreach (AcquisitionServerFieldDescriptor field in AcquisitionServerCatalog.GetServerFields(serverType))
        {
            if (string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                continue;
            }

            _configurationDraft[field.Key] = field.FieldType == AcquisitionServerFieldTypes.Boolean
                ? field.DefaultValue
                : field.DefaultValue;

            if (field.FieldType == AcquisitionServerFieldTypes.Password)
            {
                _secretFields.Add(field.Key);
            }
        }
    }

    private void SetConfigValue(string key, string value)
    {
        _configurationDraft[key] = value;
    }

    private string GetConfigValue(string key)
    {
        return _configurationDraft.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private bool IsSecretField(string key)
    {
        return _secretFields.Contains(key);
    }

    private AcquisitionServerUpsertRequest BuildRequest()
    {
        return new AcquisitionServerUpsertRequest(
            _editingId,
            _code,
            _displayName,
            _description,
            _serverType,
            _address,
            _isEnabled,
            new Dictionary<string, string>(_configurationDraft, StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> BuildConfigurationFromServer(AcquisitionServerDto server)
    {
        Dictionary<string, string> configuration = new(StringComparer.OrdinalIgnoreCase);

        foreach (AcquisitionServerConfigValueDto configValue in server.Configuration)
        {
            if (configValue.IsSecret && !configValue.HasStoredValue)
            {
                continue;
            }

            configuration[configValue.Key] = configValue.Value;
        }

        return configuration;
    }

    private static Color GetTypeChipColor(string serverType)
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

    private static Color GetProbeColor(string status)
    {
        return status switch
        {
            "成功" => Color.Success,
            "失败" => Color.Error,
            _ => Color.Default
        };
    }

    private static string GetProbeText(string status)
    {
        return string.IsNullOrWhiteSpace(status) ? "未测试" : status;
    }

    private void SetMessage(string message, Severity severity)
    {
        _message = message;
        _messageSeverity = severity;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdycDataAcquisitionPlatform.Core.Shared.Contracts;
using CdycDataAcquisitionPlatform.Presentation.AvaloniaApp.Services;

namespace CdycDataAcquisitionPlatform.Presentation.AvaloniaApp.ViewModels.Pages;

public partial class SettingsPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _settingsTitle = "本地采集点配置与服务端同步";

    [ObservableProperty]
    private Guid _editingLocalId;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _protocol = "Modbus TCP";

    [ObservableProperty]
    private string _endpoint = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _statusMessage = "可在本地维护采集点后同步到服务端。";

    [ObservableProperty]
    private LocalCollectionPointDto? _selectedLocalPoint;

    public ObservableCollection<LocalCollectionPointDto> LocalCollectionPoints { get; } = [];

    public ObservableCollection<CollectionPointDto> ServerCollectionPoints { get; } = [];

    public IAsyncRelayCommand LoadLocalCommand { get; }

    public IAsyncRelayCommand LoadServerCommand { get; }

    public IAsyncRelayCommand SaveLocalCommand { get; }

    public IAsyncRelayCommand SyncCommand { get; }

    public IRelayCommand CreateNewCommand { get; }

    private readonly PlatformApiClient _platformApiClient;
    private readonly SqliteLocalCollectionPointStore _localCollectionPointStore;

    public SettingsPageViewModel(
        PlatformApiClient platformApiClient,
        SqliteLocalCollectionPointStore localCollectionPointStore)
    {
        _platformApiClient = platformApiClient;
        _localCollectionPointStore = localCollectionPointStore;

        LoadLocalCommand = new AsyncRelayCommand(LoadLocalAsync);
        LoadServerCommand = new AsyncRelayCommand(LoadServerAsync);
        SaveLocalCommand = new AsyncRelayCommand(SaveLocalAsync);
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        CreateNewCommand = new RelayCommand(CreateNew);

        _ = InitializeAsync();
    }

    partial void OnSelectedLocalPointChanged(LocalCollectionPointDto? value)
    {
        if (value is null)
        {
            return;
        }

        EditingLocalId = value.LocalId;
        Code = value.Code;
        Name = value.Name;
        Protocol = value.Protocol;
        Endpoint = value.Endpoint;
        IsEnabled = value.IsEnabled;
        StatusMessage = $"已载入本地点位 {value.Code}，可直接修改后再次保存。";
    }

    private async Task InitializeAsync()
    {
        await LoadLocalAsync();
        await LoadServerAsync();
    }

    private async Task LoadLocalAsync()
    {
        var items = await _localCollectionPointStore.GetAllAsync();

        LocalCollectionPoints.Clear();
        foreach (var item in items)
        {
            LocalCollectionPoints.Add(item);
        }

        StatusMessage = $"本地 SQLite 中共有 {LocalCollectionPoints.Count} 个采集点。";
    }

    private async Task LoadServerAsync()
    {
        try
        {
            var items = await _platformApiClient.GetCollectionPointsAsync();
            ServerCollectionPoints.Clear();

            foreach (var item in items)
            {
                ServerCollectionPoints.Add(item);
            }

            StatusMessage = $"服务端当前共有 {ServerCollectionPoints.Count} 个采集点。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"服务端采集点加载失败：{ex.Message}";
        }
    }

    private async Task SaveLocalAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) ||
            string.IsNullOrWhiteSpace(Name) ||
            string.IsNullOrWhiteSpace(Protocol) ||
            string.IsNullOrWhiteSpace(Endpoint))
        {
            StatusMessage = "编码、名称、协议和端点不能为空。";
            return;
        }

        var savedPoint = await _localCollectionPointStore.UpsertAsync(new LocalCollectionPointDto(
            EditingLocalId,
            Code,
            Name,
            Protocol,
            Endpoint,
            IsEnabled,
            "待同步",
            DateTimeOffset.UtcNow));

        EditingLocalId = savedPoint.LocalId;
        await LoadLocalAsync();
        StatusMessage = $"本地采集点 {savedPoint.Code} 已保存到 SQLite。";
    }

    private async Task SyncAsync()
    {
        try
        {
            var localPoints = await _localCollectionPointStore.GetAllAsync();
            var response = await _platformApiClient.SyncLocalCollectionPointsAsync(localPoints);

            if (response is null)
            {
                StatusMessage = "服务端未返回同步结果。";
                return;
            }

            await _localCollectionPointStore.MarkAsSyncedAsync(response.SyncedIds);
            await LoadLocalAsync();
            await LoadServerAsync();

            StatusMessage = $"同步完成：新增 {response.CreatedCount} 个，更新 {response.UpdatedCount} 个。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"同步到服务端失败：{ex.Message}";
        }
    }

    private void CreateNew()
    {
        EditingLocalId = Guid.Empty;
        SelectedLocalPoint = null;
        Code = string.Empty;
        Name = string.Empty;
        Protocol = "Modbus TCP";
        Endpoint = string.Empty;
        IsEnabled = true;
        StatusMessage = "已切换到新建模式，可录入新的本地采集点。";
    }
}

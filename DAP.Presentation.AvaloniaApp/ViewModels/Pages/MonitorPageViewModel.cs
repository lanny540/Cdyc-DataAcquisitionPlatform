using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

public partial class MonitorPageViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "正在加载监控数据...";

    public ObservableCollection<CollectionDataRecordDto> CollectionData { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    private readonly PlatformApiClient _platformApiClient;

    public MonitorPageViewModel(PlatformApiClient platformApiClient)
    {
        _platformApiClient = platformApiClient;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "正在刷新监控数据...";

        try
        {
            IReadOnlyCollection<CollectionDataRecordDto> records = await _platformApiClient.GetCollectionDataAsync(100);

            CollectionData.Clear();
            foreach (CollectionDataRecordDto record in records.OrderByDescending(r => r.CollectedAt))
            {
                CollectionData.Add(record);
            }

            StatusMessage = $"刷新成功，共 {CollectionData.Count} 条记录。最近刷新时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

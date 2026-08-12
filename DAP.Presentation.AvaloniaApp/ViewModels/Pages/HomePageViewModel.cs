using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

public partial class HomePageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _welcomeMessage = "服务端采集概览";

    [ObservableProperty]
    private int _totalCollectionPoints;

    [ObservableProperty]
    private int _onlineCollectionPoints;

    [ObservableProperty]
    private int _offlineCollectionPoints;

    [ObservableProperty]
    private int _localSourcedCollectionPoints;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "正在等待刷新。";

    public ObservableCollection<CollectionDataRecordDto> LatestRecords { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    private readonly PlatformApiClient _platformApiClient;

    public HomePageViewModel(PlatformApiClient platformApiClient)
    {
        _platformApiClient = platformApiClient;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "正在从服务端刷新概览数据...";

        try
        {
            var overview = await _platformApiClient.GetDashboardOverviewAsync();
            var latestRecords = overview?.LatestRecords ?? Array.Empty<CollectionDataRecordDto>();

            TotalCollectionPoints = overview?.TotalCollectionPoints ?? 0;
            OnlineCollectionPoints = overview?.OnlineCollectionPoints ?? 0;
            OfflineCollectionPoints = overview?.OfflineCollectionPoints ?? 0;
            LocalSourcedCollectionPoints = overview?.LocalSourcedCollectionPoints ?? 0;

            LatestRecords.Clear();
            foreach (var record in latestRecords.OrderByDescending(item => item.CollectedAt))
            {
                LatestRecords.Add(record);
            }

            StatusMessage = $"最近刷新时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"服务端概览加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

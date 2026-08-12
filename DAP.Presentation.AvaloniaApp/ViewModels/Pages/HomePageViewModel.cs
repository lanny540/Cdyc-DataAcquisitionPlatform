using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

public partial class HomePageViewModel : ViewModelBase
{
    [ObservableProperty] private string _welcomeMessage = "Active sprints";

    [ObservableProperty] private string _boardSubtitle = "用更现代的调度工作台承载采集平台的任务流与运行状态。";

    [ObservableProperty] private string _focusTaskTitle = "Robin / 网关采集频率校准";

    [ObservableProperty] private string _focusTaskDescription =
        "右侧属性区使用 mock 数据模拟当前选中的工作项，包括进度、负责人、风险和最近采样摘要。";

    [ObservableProperty] private string _focusTaskTicket = "DAP-142";

    [ObservableProperty] private string _focusTaskStage = "In progress";

    [ObservableProperty] private int _focusTaskProgress = 68;

    [ObservableProperty] private string _focusTaskAccentBrush = "#14B8A6";

    [ObservableProperty] private string _focusTaskOwnerName = "Robin";

    [ObservableProperty] private string _focusTaskOwnerRole = "体验设计与交互联调";

    [ObservableProperty] private string _focusTaskAssigneeInitials = "RB";

    [ObservableProperty] private SprintTaskCardModel? _selectedTask;

    [ObservableProperty] private int _totalCollectionPoints;

    [ObservableProperty] private int _onlineCollectionPoints;

    [ObservableProperty] private int _offlineCollectionPoints;

    [ObservableProperty] private int _localSourcedCollectionPoints;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _statusMessage = "正在等待刷新。";

    public ObservableCollection<SprintLaneModel> SprintLanes { get; } = [];

    public ObservableCollection<MemberAvatarModel> ActiveMembers { get; } = [];

    public ObservableCollection<InspectorMetricModel> InspectorMetrics { get; } = [];

    public ObservableCollection<ActivityLogModel> ActivityLogs { get; } = [];

    public ObservableCollection<CollectionDataRecordDto> LatestRecords { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<SprintTaskCardModel?> SelectTaskCommand { get; }

    private readonly PlatformApiClient _platformApiClient;

    public HomePageViewModel(PlatformApiClient platformApiClient)
    {
        _platformApiClient = platformApiClient;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        SelectTaskCommand = new RelayCommand<SprintTaskCardModel?>(SelectTask);

        _ = LoadAsync();
    }

    partial void OnSelectedTaskChanged(SprintTaskCardModel? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (SprintTaskCardModel task in SprintLanes.SelectMany(lane => lane.Tasks))
        {
            task.IsSelected = ReferenceEquals(task, value);
        }

        FocusTaskTitle = value.Title;
        FocusTaskDescription = value.Description;
        FocusTaskTicket = value.Ticket;
        FocusTaskStage = value.Stage;
        FocusTaskProgress = value.Progress;
        FocusTaskAccentBrush = value.AccentBrush;
        FocusTaskOwnerName = value.OwnerName;
        FocusTaskOwnerRole = value.OwnerRole;
        FocusTaskAssigneeInitials = value.AssigneeInitials;

        ReplaceItems(InspectorMetrics, value.InspectorMetrics);
        ReplaceItems(ActivityLogs, value.ActivityLogs);
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "正在从服务端刷新概览数据...";

        try
        {
            DashboardOverviewDto? overview = await _platformApiClient.GetDashboardOverviewAsync();
            IReadOnlyCollection<CollectionDataRecordDto> latestRecords =
                overview?.LatestRecords ?? Array.Empty<CollectionDataRecordDto>();

            TotalCollectionPoints = overview?.TotalCollectionPoints ?? 0;
            OnlineCollectionPoints = overview?.OnlineCollectionPoints ?? 0;
            OfflineCollectionPoints = overview?.OfflineCollectionPoints ?? 0;
            LocalSourcedCollectionPoints = overview?.LocalSourcedCollectionPoints ?? 0;

            LatestRecords.Clear();
            foreach (CollectionDataRecordDto record in latestRecords.OrderByDescending(item => item.CollectedAt))
            {
                LatestRecords.Add(record);
            }

            if (overview?.SprintLanes is not null)
            {
                SprintLanes.Clear();
                foreach (SprintLaneDto laneDto in overview.SprintLanes)
                {
                    var lane = new SprintLaneModel
                    {
                        Title = laneDto.Title,
                        Summary = laneDto.Summary,
                        Tasks = new ObservableCollection<SprintTaskCardModel>(laneDto.Tasks.Select(taskDto =>
                            new SprintTaskCardModel
                            {
                                Title = taskDto.Title,
                                Description = taskDto.Description,
                                Stage = taskDto.Stage,
                                Tag = taskDto.Tag,
                                TagBackground = taskDto.TagBackground,
                                TagForeground = taskDto.TagForeground,
                                Ticket = taskDto.Ticket,
                                Metric = taskDto.Metric,
                                AccentBrush = taskDto.AccentBrush,
                                Progress = taskDto.Progress,
                                AssigneeInitials = taskDto.AssigneeInitials,
                                OwnerName = taskDto.OwnerName,
                                OwnerRole = taskDto.OwnerRole,
                                InspectorMetrics = taskDto.InspectorMetrics
                                    .Select(m => new InspectorMetricModel(m.Label, m.Value, m.AccentBrush)).ToList(),
                                ActivityLogs = taskDto.ActivityLogs
                                    .Select(a => new ActivityLogModel(a.Time, a.Message, a.AccentBrush)).ToList(),
                                Command = SelectTaskCommand
                            }))
                    };
                    SprintLanes.Add(lane);
                }

                // Select the first task by default
                SelectTask(SprintLanes
                               .SelectMany(lane => lane.Tasks)
                               .FirstOrDefault(task => task.Ticket == "DAP-142")
                           ?? SprintLanes.SelectMany(lane => lane.Tasks).FirstOrDefault());
            }

            if (overview?.ActiveMembers is not null)
            {
                ActiveMembers.Clear();
                foreach (MemberAvatarDto memberDto in overview.ActiveMembers)
                {
                    ActiveMembers.Add(new MemberAvatarModel(memberDto.Initials, memberDto.AccentBrush));
                }
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

    private void SelectTask(SprintTaskCardModel? task)
    {
        if (task is not null)
        {
            SelectedTask = task;
        }
    }

    private void AttachTaskCommands()
    {
        foreach (SprintTaskCardModel task in SprintLanes.SelectMany(lane => lane.Tasks))
        {
            task.Command = SelectTaskCommand;
        }
    }

    private static void ReplaceItems<TItem>(
        ObservableCollection<TItem> target,
        IEnumerable<TItem> source)
    {
        target.Clear();
        foreach (TItem item in source)
        {
            target.Add(item);
        }
    }
}

public sealed class SprintLaneModel
{
    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public ObservableCollection<SprintTaskCardModel> Tasks { get; init; } = [];
}

public partial class SprintTaskCardModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string TagBackground { get; init; } = "#EEF2FF";

    public string TagForeground { get; init; } = "#475569";

    public string Ticket { get; init; } = string.Empty;

    public string Metric { get; init; } = string.Empty;

    public string AccentBrush { get; init; } = "#4F46E5";

    public int Progress { get; init; }

    public string AssigneeInitials { get; init; } = string.Empty;

    public string OwnerName { get; init; } = string.Empty;

    public string OwnerRole { get; init; } = string.Empty;

    public IReadOnlyList<InspectorMetricModel> InspectorMetrics { get; init; } = [];

    public IReadOnlyList<ActivityLogModel> ActivityLogs { get; init; } = [];

    public ICommand? Command { get; set; }
}

public sealed record MemberAvatarModel(string Initials, string AccentBrush);

public sealed record InspectorMetricModel(string Label, string Value, string AccentBrush);

public sealed record ActivityLogModel(string Time, string Message, string AccentBrush);

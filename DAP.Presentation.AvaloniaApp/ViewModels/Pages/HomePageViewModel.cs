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

    public ObservableCollection<SprintLaneModel> SprintLanes { get; } = CreateSprintLanes();

    public ObservableCollection<MemberAvatarModel> ActiveMembers { get; } =
        new(CreateMembers());

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

        AttachTaskCommands();
        SelectTask(SprintLanes
            .SelectMany(lane => lane.Tasks)
            .FirstOrDefault(task => task.Ticket == "DAP-142")
            ?? SprintLanes.SelectMany(lane => lane.Tasks).FirstOrDefault());

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

    private static ObservableCollection<SprintLaneModel> CreateSprintLanes()
    {
        return new ObservableCollection<SprintLaneModel>
        {
            new()
            {
                Title = "TO DO",
                Summary = "待确认 04",
                Tasks =
                [
                    new SprintTaskCardModel
                    {
                        Title = "统一边缘网关采样节拍与缓存写入策略",
                        Description = "统一边缘网关的轮询周期、缓存窗口和异常回退策略，减少波峰时段的数据抖动。",
                        Stage = "To do",
                        Tag = "Planning",
                        TagBackground = "#E9EEFF",
                        TagForeground = "#4F46E5",
                        Ticket = "DAP-101",
                        Metric = "3 项依赖",
                        AccentBrush = "#4F46E5",
                        Progress = 24,
                        AssigneeInitials = "LY",
                        OwnerName = "Lyn",
                        OwnerRole = "边缘采集方案评估",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("刷新周期", "30 min", "#4F46E5"),
                            new InspectorMetricModel("依赖模块", "03", "#4F46E5"),
                            new InspectorMetricModel("阻塞项", "02", "#EF5A6F")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("09:05", "完成采样频率调整方案草图", "#4F46E5"),
                            new ActivityLogModel("09:42", "补充缓存落盘异常处理边界", "#14B8A6"),
                            new ActivityLogModel("10:16", "等待平台接口约束确认", "#F59E0B")
                        ]
                    },
                    new SprintTaskCardModel
                    {
                        Title = "清理历史采集模板并补齐站点映射字段",
                        Description = "整理旧模板中的无效字段，补齐采集点与站点映射的缺失信息，为后续迁移做准备。",
                        Stage = "To do",
                        Tag = "Backlog",
                        TagBackground = "#EEF2FF",
                        TagForeground = "#475569",
                        Ticket = "DAP-117",
                        Metric = "5 个字段",
                        AccentBrush = "#94A3B8",
                        Progress = 18,
                        AssigneeInitials = "WX",
                        OwnerName = "Wes",
                        OwnerRole = "基础数据治理",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("待清理模板", "12", "#94A3B8"),
                            new InspectorMetricModel("站点映射", "05", "#14B8A6"),
                            new InspectorMetricModel("风险项", "01", "#EF5A6F")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("08:52", "完成旧模板字段盘点", "#94A3B8"),
                            new ActivityLogModel("09:37", "补录 5 个站点映射字段", "#14B8A6"),
                            new ActivityLogModel("10:20", "待业务确认废弃模板清单", "#F59E0B")
                        ]
                    }
                ]
            },
            new()
            {
                Title = "IN PROGRESS",
                Summary = "执行中 05",
                Tasks =
                [
                    new SprintTaskCardModel
                    {
                        Title = "将客户端仪表盘切换为设计工作台式布局",
                        Description = "对照参考图重做主工作台，改造导航、卡片层级和右侧 inspector，让信息结构更清晰。",
                        Stage = "In progress",
                        Tag = "Design",
                        TagBackground = "#E7FAF7",
                        TagForeground = "#0F766E",
                        Ticket = "DAP-142",
                        Metric = "68% 完成",
                        AccentBrush = "#14B8A6",
                        Progress = 68,
                        AssigneeInitials = "RB",
                        OwnerName = "Robin",
                        OwnerRole = "体验设计与交互联调",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("刷新周期", "15 min", "#4F46E5"),
                            new InspectorMetricModel("同步批次", "03", "#14B8A6"),
                            new InspectorMetricModel("阻塞项", "01", "#EF5A6F")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("09:18", "完成主窗口三栏壳层重构", "#4F46E5"),
                            new ActivityLogModel("10:02", "为主页补充 mock 任务卡片和详情侧栏", "#14B8A6"),
                            new ActivityLogModel("10:41", "联动主题切换，校准明暗色板", "#F59E0B")
                        ]
                    },
                    new SprintTaskCardModel
                    {
                        Title = "修正断线重连后的本地同步状态展示",
                        Description = "让断线恢复后的本地同步状态与服务端快照重新对齐，避免界面长时间显示旧状态。",
                        Stage = "In progress",
                        Tag = "Refactor",
                        TagBackground = "#FFF4DB",
                        TagForeground = "#B45309",
                        Ticket = "DAP-138",
                        Metric = "2 个分支",
                        AccentBrush = "#F59E0B",
                        Progress = 52,
                        AssigneeInitials = "ST",
                        OwnerName = "Stewie",
                        OwnerRole = "同步状态修正",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("待对齐状态", "07", "#F59E0B"),
                            new InspectorMetricModel("回归用例", "04", "#14B8A6"),
                            new InspectorMetricModel("异常节点", "02", "#EF5A6F")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("09:28", "定位断线恢复后的状态残留问题", "#F59E0B"),
                            new ActivityLogModel("09:55", "补充本地状态覆盖逻辑", "#14B8A6"),
                            new ActivityLogModel("10:32", "准备回归断线恢复链路", "#4F46E5")
                        ]
                    },
                    new SprintTaskCardModel
                    {
                        Title = "下发 Modbus TCP 采集模板到新工位",
                        Description = "将新建工位所需的 Modbus TCP 模板参数打包下发，并验证首轮采集是否稳定。",
                        Stage = "In progress",
                        Tag = "Deploy",
                        TagBackground = "#FFE8EC",
                        TagForeground = "#BE123C",
                        Ticket = "DAP-145",
                        Metric = "1 台设备",
                        AccentBrush = "#EF5A6F",
                        Progress = 41,
                        AssigneeInitials = "QH",
                        OwnerName = "Qin",
                        OwnerRole = "工位上线准备",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("目标工位", "01", "#EF5A6F"),
                            new InspectorMetricModel("已校验寄存器", "08", "#14B8A6"),
                            new InspectorMetricModel("告警项", "01", "#F59E0B")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("08:47", "完成模板参数包导出", "#EF5A6F"),
                            new ActivityLogModel("09:40", "校验寄存器映射前 8 项", "#14B8A6"),
                            new ActivityLogModel("10:26", "等待现场设备重启确认", "#F59E0B")
                        ]
                    }
                ]
            },
            new()
            {
                Title = "REVIEW",
                Summary = "待验收 03",
                Tasks =
                [
                    new SprintTaskCardModel
                    {
                        Title = "补齐服务端总览接口的空值与降级处理",
                        Description = "完善服务端总览接口的空值保护和异常降级，保证桌面端在网络抖动时仍能稳定显示。",
                        Stage = "Review",
                        Tag = "API",
                        TagBackground = "#E8F1FF",
                        TagForeground = "#1D4ED8",
                        Ticket = "DAP-133",
                        Metric = "待联调",
                        AccentBrush = "#3B82F6",
                        Progress = 88,
                        AssigneeInitials = "AN",
                        OwnerName = "Anne",
                        OwnerRole = "接口稳定性校验",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("异常分支", "06", "#3B82F6"),
                            new InspectorMetricModel("降级场景", "03", "#14B8A6"),
                            new InspectorMetricModel("待确认", "01", "#F59E0B")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("09:11", "补齐 LatestRecords 空集合降级", "#3B82F6"),
                            new ActivityLogModel("09:58", "增加接口失败提示文案", "#14B8A6"),
                            new ActivityLogModel("10:44", "等待桌面端联调验证", "#F59E0B")
                        ]
                    },
                    new SprintTaskCardModel
                    {
                        Title = "验证本地 SQLite 同步结果与服务端快照一致性",
                        Description = "对比本地 SQLite 与服务端采集点快照，校验同步后字段与状态是否完全一致。",
                        Stage = "Review",
                        Tag = "QA",
                        TagBackground = "#F3E8FF",
                        TagForeground = "#7E22CE",
                        Ticket = "DAP-136",
                        Metric = "6 条用例",
                        AccentBrush = "#8B5CF6",
                        Progress = 79,
                        AssigneeInitials = "MK",
                        OwnerName = "Mika",
                        OwnerRole = "同步一致性验证",
                        InspectorMetrics =
                        [
                            new InspectorMetricModel("回归用例", "06", "#8B5CF6"),
                            new InspectorMetricModel("通过率", "83%", "#14B8A6"),
                            new InspectorMetricModel("待复核", "01", "#F59E0B")
                        ],
                        ActivityLogs =
                        [
                            new ActivityLogModel("08:58", "完成本地与服务端字段映射核对", "#8B5CF6"),
                            new ActivityLogModel("09:49", "发现 1 条同步状态延迟", "#F59E0B"),
                            new ActivityLogModel("10:35", "回归验证 5 条用例通过", "#14B8A6")
                        ]
                    }
                ]
            }
        };
    }

    private static IReadOnlyList<MemberAvatarModel> CreateMembers()
    {
        return
        [
            new MemberAvatarModel("LY", "#4F46E5"),
            new MemberAvatarModel("RB", "#14B8A6"),
            new MemberAvatarModel("ST", "#F59E0B"),
            new MemberAvatarModel("MK", "#8B5CF6")
        ];
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

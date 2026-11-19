using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Clients;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 班组选项。
/// </summary>
/// <param name="Code">班组编码。</param>
/// <param name="Name">班组名称。</param>
public sealed record TeamOption(string Code, string Name)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return Name;
    }
}

/// <summary>
/// 带班组选择器的能耗页面基类。
///
/// 班组清单来自能耗域只读接口（<c>/api/energy-metrics/teams</c>），
/// 排班仍是班组的唯一定义源，但查看页不再依赖配置级排班状态接口。
///
/// 定时刷新不在这里：它属于「所有能耗页」的共同能力，由
/// <see cref="AutoRefreshPageViewModelBase"/> 提供，
/// 没有班组选择器的横向对比页与报表页也能继承同一份实现。
/// </summary>
public abstract partial class EnergyPageViewModelBase : AutoRefreshPageViewModelBase
{
    private bool _suppressTeamReload;

    /// <summary>
    /// 初始化基类。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    protected EnergyPageViewModelBase(DesktopSession session)
    {
        Session = session;
    }

    /// <summary>
    /// 桌面会话。
    /// </summary>
    protected DesktopSession Session { get; }

    /// <summary>
    /// 平台接口。
    /// </summary>
    protected IPlatformApiClient Api => Session.Api;

    /// <summary>
    /// 可选班组。
    /// </summary>
    public ObservableCollection<TeamOption> Teams { get; } = [];

    /// <summary>
    /// 当前班组。
    /// </summary>
    [ObservableProperty] private TeamOption? _selectedTeam;

    /// <summary>
    /// 是否已导入排班数据。未导入时班组列表为空，页面需要显式说明而不是显示空白下拉框。
    /// </summary>
    [ObservableProperty] private bool _hasTeams;

    /// <summary>
    /// 班组选择变化后自动重新查询。回填选中项时用 <see cref="_suppressTeamReload"/> 抑制一次。
    /// </summary>
    partial void OnSelectedTeamChanged(TeamOption? value)
    {
        if (_suppressTeamReload || value is null) return;

        _ = ReloadAsync();
    }

    /// <summary>
    /// 载入班组清单。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>班组清单。</returns>
    protected async Task<IReadOnlyList<ShiftTeamDto>> LoadTeamsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ShiftTeamDto> teams =
            await Api.GetEnergyTeamsAsync(cancellationToken);

        TeamOption? previous = SelectedTeam;

        _suppressTeamReload = true;

        try
        {
            Teams.Clear();

            foreach (ShiftTeamDto team in teams) Teams.Add(new TeamOption(team.TeamCode, team.TeamName));

            HasTeams = Teams.Count > 0;

            SelectedTeam = previous is not null
                ? Teams.FirstOrDefault(team => team.Code == previous.Code) ?? Teams.FirstOrDefault()
                : Teams.FirstOrDefault();
        }
        finally
        {
            _suppressTeamReload = false;
        }

        return teams;
    }

    /// <summary>
    /// 以指定编码选中班组，用于服务端解析出在岗班组后的回填。
    /// </summary>
    /// <param name="teamCode">班组编码。</param>
    protected void SelectTeam(string teamCode)
    {
        TeamOption? match = Teams.FirstOrDefault(team => team.Code == teamCode);

        if (match is null || ReferenceEquals(match, SelectedTeam)) return;

        _suppressTeamReload = true;

        try
        {
            SelectedTeam = match;
        }
        finally
        {
            _suppressTeamReload = false;
        }
    }
}
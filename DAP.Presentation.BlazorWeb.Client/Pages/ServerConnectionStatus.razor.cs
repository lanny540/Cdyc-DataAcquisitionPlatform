using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示服务器连接状态页面的交互逻辑。
/// </summary>
public partial class ServerConnectionStatus
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private readonly List<ServerConnectionStatusDto> _statuses = [];
    private bool _isLoading;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

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
            _statuses.Clear();
            _statuses.AddRange(await PlatformApiClient.GetServerConnectionStatusesAsync());
        }
        catch (Exception ex)
        {
            _message = $"服务器状态加载失败：{ex.Message}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private Color GetStatusColor(ServerConnectionStatusDto item)
    {
        if (!item.IsConfigured)
        {
            return Color.Default;
        }

        return item.IsReachable ? Color.Success : Color.Warning;
    }

    private string GetStatusText(ServerConnectionStatusDto item)
    {
        if (!item.IsConfigured)
        {
            return "未配置";
        }

        return item.IsReachable ? "可连接" : "不可连接";
    }
}

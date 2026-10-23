using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示服务器连接状态页面的交互逻辑。
/// </summary>
public partial class ServerConnectionStatus : IDisposable
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    [Inject] private ILogger<ServerConnectionStatus> Logger { get; set; } = null!;

    private readonly List<ServerConnectionStatusDto> _statuses = [];
    private CancellationTokenSource? _statusCheckCancellationTokenSource;
    private bool _isLoading;
    private bool _isCheckingStatuses;
    private int _refreshVersion;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private int ConfiguredCount => _statuses.Count(item => item.IsConfigured);

    private int ReachableCount => _statuses.Count(item => item.IsConfigured && item.IsReachable);

    private int CheckingCount => _statuses.Count(item => item.IsChecking);

    private int RelatedDefinitionCount => _statuses.Sum(item => item.RelatedDefinitionCount);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _statusCheckCancellationTokenSource?.Cancel();
        _statusCheckCancellationTokenSource?.Dispose();
        _statusCheckCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _statusCheckCancellationTokenSource.Token;
        var refreshVersion = ++_refreshVersion;

        _isLoading = true;
        _isCheckingStatuses = false;
        _message = null;

        try
        {
            _statuses.Clear();
            _statuses.AddRange(await PlatformApiClient.GetServerConnectionsAsync(cancellationToken));
            _message = _statuses.Count == 0
                ? "数据库中还没有登记可诊断的服务器信息。请先在后台数据管理中维护采集地址。"
                : $"已载入 {_statuses.Count} 台服务器，正在后台异步检测连接状态。";
            _messageSeverity = _statuses.Count == 0 ? Severity.Info : Severity.Success;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _message = $"服务器状态加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }

        if (_statuses.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            _ = CheckStatusesAsync(refreshVersion, cancellationToken);
        }
    }

    private Color GetStatusColor(ServerConnectionStatusDto item)
    {
        if (item.IsChecking)
        {
            return Color.Info;
        }

        if (!item.IsConfigured)
        {
            return Color.Default;
        }

        return item.IsReachable ? Color.Success : Color.Warning;
    }

    private string GetStatusText(ServerConnectionStatusDto item)
    {
        if (item.IsChecking)
        {
            return "检测中";
        }

        if (!item.IsConfigured)
        {
            return "未配置";
        }

        return item.IsReachable ? "可连接" : "不可连接";
    }

    /// <summary>
    /// 按服务器实时状态返回对应的语义强调色类，
    /// 使状态卡的左侧色条、底纹与标题颜色随状态联动。
    /// </summary>
    private static string GetStatusCardClass(ServerConnectionStatusDto item)
    {
        if (item.IsChecking)
        {
            return "platform-accent-info";
        }

        if (!item.IsConfigured)
        {
            return "platform-accent-neutral";
        }

        return item.IsReachable ? "platform-accent-success" : "platform-accent-warning";
    }

    private string GetModeText(ServerConnectionStatusDto item)
    {
        return item.UseMockResponses ? "Mock 响应" : "真实请求";
    }

    private string GetCredentialsText(ServerConnectionStatusDto item)
    {
        return item.CredentialsConfigured ? "已配置" : "未配置";
    }

    private async Task CheckStatusesAsync(int refreshVersion, CancellationToken cancellationToken)
    {
        _isCheckingStatuses = true;
        for (var index = 0; index < _statuses.Count; index++)
        {
            _statuses[index] = _statuses[index] with
            {
                IsChecking = true,
                StatusMessage = "正在执行网络探测..."
            };
        }

        await InvokeAsync(StateHasChanged);

        try
        {
            await Task.WhenAll(_statuses
                .Select(item => CheckStatusAsync(item, refreshVersion, cancellationToken)));

            if (refreshVersion == _refreshVersion && !cancellationToken.IsCancellationRequested)
            {
                _message = $"检测完成：{ReachableCount}/{ConfiguredCount} 台已配置服务器可连接。";
                _messageSeverity = ReachableCount == ConfiguredCount ? Severity.Success : Severity.Warning;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (refreshVersion == _refreshVersion)
            {
                _isCheckingStatuses = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task CheckStatusAsync(
        ServerConnectionStatusDto item,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            ServerConnectionStatusDto checkedStatus =
                await PlatformApiClient.CheckServerConnectionStatusAsync(item.Key, cancellationToken);
            await UpdateStatusAsync(checkedStatus, refreshVersion, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // 客户端调用探测接口失败：英文框架文案不出现在界面上，细节只进日志。
            Logger.LogWarning(ex, "检测服务器 {ServerKey} 状态失败。", item.Key);

            await UpdateStatusAsync(
                item with
                {
                    IsChecking = false,
                    IsReachable = false,
                    StatusMessage = "检测请求失败，请查看服务端日志。",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ErrorMessage = null
                },
                refreshVersion,
                cancellationToken);
        }
    }

    private Task UpdateStatusAsync(
        ServerConnectionStatusDto status,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        return InvokeAsync(() =>
        {
            if (refreshVersion != _refreshVersion || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var index = _statuses.FindIndex(item => item.Key == status.Key);
            if (index >= 0)
            {
                _statuses[index] = status;
                StateHasChanged();
            }
        });
    }

    public void Dispose()
    {
        _statusCheckCancellationTokenSource?.Cancel();
        _statusCheckCancellationTokenSource?.Dispose();
    }
}

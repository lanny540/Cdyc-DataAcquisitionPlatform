using System.Globalization;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示 History 数据库 API 测试页面的交互逻辑。
/// 服务器地址与凭据来自采集服务器配置，页面只选择服务器并发起查询。
/// </summary>
public partial class HistoryApiTest
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private readonly List<AcquisitionServerDto> _historianServers = [];

    private Guid? _selectedServerId;
    private string _currentTagNames = "tag001";
    private string _rawTagName = "tag001";

    private string _rawStartText = DateTimeOffset.UtcNow.AddHours(-1)
        .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private string _rawEndText = DateTimeOffset.UtcNow
        .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private int _rawStartIndex;
    private int _rawCount = 100;
    private bool _isLoadingServers;
    private bool _isCurrentLoading;
    private bool _isRawLoading;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private HistoryApiQueryResponse? _latestResponse;

    private bool IsBusy => _isLoadingServers || _isCurrentLoading || _isRawLoading;

    private AcquisitionServerDto? SelectedServer => _selectedServerId.HasValue
        ? _historianServers.FirstOrDefault(item => item.Id == _selectedServerId.Value)
        : null;

    private string CurrentValuePreview => BuildPreview("currentValuePath", "?tagNames=", _currentTagNames);

    private string RawDataPreview => BuildRawPreview();

    private Color LatestResponseColor => _latestResponse?.Success == true ? Color.Success : Color.Error;

    protected override async Task OnInitializedAsync()
    {
        _isLoadingServers = true;

        try
        {
            IReadOnlyList<AcquisitionServerDto> servers = await PlatformApiClient.GetAcquisitionServersAsync();
            _historianServers.AddRange(servers.Where(item =>
                string.Equals(item.ServerType, AcquisitionServerCatalog.Historian, StringComparison.Ordinal)));

            _selectedServerId = _historianServers.FirstOrDefault()?.Id;

            if (_historianServers.Count == 0)
            {
                SetMessage("尚未配置 GE Historian 采集服务器，请先在采集服务器页面新增并启用。", Severity.Warning);
            }
        }
        catch (Exception ex)
        {
            SetMessage($"采集服务器加载失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isLoadingServers = false;
        }
    }

    private async Task QueryCurrentValueAsync()
    {
        if (!EnsureServerSelected())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentTagNames))
        {
            SetMessage("最新值查询的标签名称不能为空。", Severity.Warning);
            return;
        }

        _isCurrentLoading = true;
        _message = null;

        try
        {
            _latestResponse = await PlatformApiClient.GetHistoryCurrentValueAsync(
                new HistoryCurrentValueQueryRequest(_currentTagNames.Trim(), _selectedServerId));

            SetMessage(
                _latestResponse.Success
                    ? $"已完成最新值查询，HTTP {_latestResponse.StatusCode}。"
                    : $"最新值查询失败：{_latestResponse.ErrorMessage}",
                _latestResponse.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"最新值查询失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isCurrentLoading = false;
        }
    }

    private async Task QueryRawDataAsync()
    {
        if (!EnsureServerSelected())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_rawTagName))
        {
            SetMessage("时间段查询的标签名称不能为空。", Severity.Warning);
            return;
        }

        if (!TryParseUtcTime(_rawStartText, out DateTimeOffset startTime) ||
            !TryParseUtcTime(_rawEndText, out DateTimeOffset endTime))
        {
            SetMessage("时间格式不正确，请输入 ISO-8601 UTC 时间，例如 2026-09-01T00:00:00.000Z。", Severity.Warning);
            return;
        }

        if (endTime <= startTime)
        {
            SetMessage("结束时间必须晚于开始时间。", Severity.Warning);
            return;
        }

        if (_rawStartIndex < 0 || _rawCount <= 0)
        {
            SetMessage("起始偏移量不能小于 0，返回条数必须大于 0。", Severity.Warning);
            return;
        }

        _isRawLoading = true;
        _message = null;

        try
        {
            _latestResponse = await PlatformApiClient.GetHistoryRawDataAsync(
                new HistoryRawDataQueryRequest(
                    _rawTagName.Trim(),
                    startTime,
                    endTime,
                    _rawStartIndex,
                    _rawCount,
                    _selectedServerId));

            SetMessage(
                _latestResponse.Success
                    ? $"已完成时间段原始数据查询，HTTP {_latestResponse.StatusCode}。"
                    : $"时间段原始数据查询失败：{_latestResponse.ErrorMessage}",
                _latestResponse.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"时间段原始数据查询失败：{UserFacingErrors.Describe(ex)}", Severity.Error);
        }
        finally
        {
            _isRawLoading = false;
        }
    }

    private void FillLastHourPreset()
    {
        _rawStartText = DateTimeOffset.UtcNow.AddHours(-1)
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        _rawEndText = DateTimeOffset.UtcNow
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        _rawStartIndex = 0;
        _rawCount = 100;
    }

    private Task OnServerChangedAsync(Guid? serverId)
    {
        _selectedServerId = serverId;
        _latestResponse = null;
        _message = null;
        return Task.CompletedTask;
    }

    private bool EnsureServerSelected()
    {
        if (_historianServers.Count == 0)
        {
            SetMessage("尚未配置 GE Historian 采集服务器，请先在采集服务器页面新增并启用。", Severity.Warning);
            return false;
        }

        if (_selectedServerId is null)
        {
            SetMessage("请先选择要测试的 GE Historian 采集服务器。", Severity.Warning);
            return false;
        }

        return true;
    }

    private void SetMessage(string message, Severity severity)
    {
        _message = message;
        _messageSeverity = severity;
    }

    private string BuildPreview(string configurationKey, string querySeparator, string tagNames)
    {
        var baseAddress = SelectedServer?.Address ?? "（未选择服务器）";
        var path = GetServerConfigValue(configurationKey);
        var tag = Uri.EscapeDataString(tagNames.Trim());

        return $"{baseAddress.TrimEnd('/')}{path}{querySeparator}{tag}";
    }

    private string BuildRawPreview()
    {
        var baseAddress = SelectedServer?.Address ?? "（未选择服务器）";
        var path = GetServerConfigValue("rawDataPathPrefix");

        return $"{baseAddress.TrimEnd('/')}{path}/{Uri.EscapeDataString(_rawTagName.Trim())}/" +
               $"{_rawStartText.Trim()}/{_rawEndText.Trim()}/{_rawStartIndex}/{_rawCount}";
    }

    private string GetServerConfigValue(string key)
    {
        AcquisitionServerConfigValueDto? matchedValue = SelectedServer?.Configuration?
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        var configuredValue = matchedValue?.Value ?? string.Empty;

        return configuredValue.Length > 0
            ? configuredValue
            : AcquisitionServerCatalog.GetServerFieldDefault(AcquisitionServerCatalog.Historian, key);
    }

    private static bool TryParseUtcTime(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
    }
}

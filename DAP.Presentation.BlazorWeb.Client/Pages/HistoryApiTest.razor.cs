using System.Globalization;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示 History 数据库 API 测试页面的交互逻辑。
/// </summary>
public partial class HistoryApiTest
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private string _currentTagNames = "tag001";
    private string _rawTagName = "tag001";
    private string _rawStartText = DateTimeOffset.UtcNow.AddHours(-1)
        .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private string _rawEndText = DateTimeOffset.UtcNow
        .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private int _rawStartIndex;
    private int _rawCount = 100;
    private bool _isCurrentLoading;
    private bool _isRawLoading;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private HistoryApiQueryResponse? _latestResponse;

    private bool IsBusy => _isCurrentLoading || _isRawLoading;

    private string CurrentValuePreview =>
        $"/historian-rest-api/v1/datapoints/currentvalue?tagNames={Uri.EscapeDataString(_currentTagNames.Trim())}";

    private string RawDataPreview =>
        $"/historian-rest-api/v1/datapoints/raw/{Uri.EscapeDataString(_rawTagName.Trim())}/{_rawStartText.Trim()}/{_rawEndText.Trim()}/{_rawStartIndex}/{_rawCount}";

    private Color LatestResponseColor => _latestResponse?.Success == true ? Color.Success : Color.Error;

    private async Task QueryCurrentValueAsync()
    {
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
                new HistoryCurrentValueQueryRequest(_currentTagNames.Trim()));

            SetMessage(
                _latestResponse.Success
                    ? $"已完成最新值查询，HTTP {_latestResponse.StatusCode}。"
                    : $"最新值查询失败：{_latestResponse.ErrorMessage}",
                _latestResponse.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"最新值查询失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _isCurrentLoading = false;
        }
    }

    private async Task QueryRawDataAsync()
    {
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
                new HistoryRawDataQueryRequest(_rawTagName.Trim(), startTime, endTime, _rawStartIndex, _rawCount));

            SetMessage(
                _latestResponse.Success
                    ? $"已完成时间段原始数据查询，HTTP {_latestResponse.StatusCode}。"
                    : $"时间段原始数据查询失败：{_latestResponse.ErrorMessage}",
                _latestResponse.Success ? Severity.Success : Severity.Error);
        }
        catch (Exception ex)
        {
            SetMessage($"时间段原始数据查询失败：{ex.Message}", Severity.Error);
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

    private void SetMessage(string message, Severity severity)
    {
        _message = message;
        _messageSeverity = severity;
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

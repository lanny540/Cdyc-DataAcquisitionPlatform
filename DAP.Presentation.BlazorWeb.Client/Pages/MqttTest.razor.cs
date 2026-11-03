using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示 MQTT 测试页的交互逻辑。
///
/// 复用采集服务器的探测接口：服务端对 MQTT 类型只做 TCP 层可达性验证，
/// 因此页面明确标注「网络层探测」，不宣称完成了订阅或发布。
/// </summary>
public partial class MqttTest
{
    private const string DefaultAddress = "mqtt://broker.local:1883";
    private const string DefaultClientId = "dap-web-probe";

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private string _address = DefaultAddress;
    private string _clientId = DefaultClientId;
    private string _username = string.Empty;
    private string _password = string.Empty;

    private bool _isTesting;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private AcquisitionServerProbeResultDto? _result;

    private string ResultLabel => _result is null ? string.Empty : _result.Success ? "探测成功" : "探测失败";

    private Color ResultColor => _result?.Success == true ? Color.Success : Color.Error;

    private Severity ResultSeverity => _result?.Success == true ? Severity.Success : Severity.Error;

    private string ElapsedLabel => _result is null ? "--" : $"{_result.ElapsedMilliseconds} ms";

    /// <summary>
    /// 时间格式化放在代码后置：Razor 里的格式化表达式容易被原样输出。
    /// </summary>
    private string CheckedAtLabel => _result is null
        ? "--"
        : $"检测时间 {_result.CheckedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(_address))
        {
            _message = "请填写 Broker 地址，例如 mqtt://broker.local:1883。";
            _messageSeverity = Severity.Warning;
            return;
        }

        _isTesting = true;
        _message = null;

        try
        {
            var configuration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_clientId))
            {
                configuration["clientId"] = _clientId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(_username))
            {
                configuration["username"] = _username.Trim();
            }

            if (!string.IsNullOrWhiteSpace(_password))
            {
                configuration["password"] = _password;
            }

            // 传 null 表示草稿测试：服务端按请求体直接发起探测，不落库。
            _result = await PlatformApiClient.TestAcquisitionServerAsync(
                null,
                new AcquisitionServerProbeRequest(AcquisitionServerCatalog.Mqtt, _address.Trim(), configuration));
        }
        catch (Exception ex)
        {
            _result = null;
            _message = $"测试未完成：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isTesting = false;
        }
    }

    private void Reset()
    {
        _address = DefaultAddress;
        _clientId = DefaultClientId;
        _username = string.Empty;
        _password = string.Empty;
        _result = null;
        _message = null;
    }
}

using DAP.Core.Shared.Contracts;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Services;

/// <summary>
/// 组装一次性执行类操作（调试读取、采集入库、连接测试、连通性测试）的 toast 提示内容并推送。
/// 结果以浮动提示呈现，不再依赖页面内嵌消息条，避免长页面下需要滚动到顶部才能看到反馈。
/// 各页面内嵌的详细结果卡（解析值、状态码、原始 JSON 等）保留作为持久记录。
/// </summary>
public static class PlatformExecutionToasts
{
    /// <summary>失败提示的展示时长（毫秒）：失败原因较长，需要留足阅读时间。</summary>
    public const int FailureVisibleDurationMilliseconds = 8000;

    /// <summary>组装调试读取或采集入库的结果提示。</summary>
    public static (string Message, Severity Severity) ForManagedDataExecution(
        ManagedDataExecutionResultDto result,
        bool isCollect)
    {
        string action = isCollect ? "入库" : "调试";

        if (!result.Success)
        {
            return (
                $"{result.Code} {action}失败：{DescribeFailure(result.ErrorMessage, result.StatusMessage)}",
                Severity.Error);
        }

        string detail = result.ParsedValue.HasValue
            ? $"解析值 {result.ParsedValue.Value:0.###}{FormatUnit(result.Unit)}"
            : result.StatusMessage;

        return ($"{result.Code} {action}成功，{detail}", Severity.Success);
    }

    /// <summary>组装采集节点连接测试的结果提示。</summary>
    public static (string Message, Severity Severity) ForConnectionTest(
        ManagedDataConnectionTestResultDto result)
    {
        if (!result.Success)
        {
            return (
                $"连接测试失败：{DescribeFailure(result.ErrorMessage, result.StatusMessage)}",
                Severity.Warning);
        }

        string detail = string.IsNullOrWhiteSpace(result.ParsedValue)
            ? string.Empty
            : $"（解析值 {result.ParsedValue}）";

        return ($"连接测试成功：{result.StatusMessage}{detail}", Severity.Success);
    }

    /// <summary>组装采集服务器连通性测试的结果提示。</summary>
    public static (string Message, Severity Severity) ForServerProbe(
        AcquisitionServerProbeResultDto result,
        string? serverName = null)
    {
        string target = string.IsNullOrWhiteSpace(serverName) ? "采集服务器" : serverName;

        if (!result.Success)
        {
            return (
                $"{target} 连通性测试失败：{DescribeFailure(result.ErrorMessage, result.StatusMessage)}",
                Severity.Warning);
        }

        string detail = string.IsNullOrWhiteSpace(result.ParsedValue)
            ? string.Empty
            : $"（解析值 {result.ParsedValue}）";

        return ($"{target} 连通性测试成功：{result.StatusMessage}{detail}", Severity.Success);
    }

    /// <summary>推送一条 toast：成功提示走全局默认时长，失败提示延长展示便于阅读。</summary>
    public static void Publish(ISnackbar snackbar, string message, Severity severity)
    {
        snackbar.Add(message, severity, options =>
        {
            if (severity is not Severity.Success)
            {
                options.VisibleStateDuration = FailureVisibleDurationMilliseconds;
            }
        });
    }

    /// <summary>失败时优先展示明确的错误原因，缺失时回退到状态说明。</summary>
    private static string DescribeFailure(string? errorMessage, string statusMessage)
    {
        return string.IsNullOrWhiteSpace(errorMessage) ? statusMessage : errorMessage;
    }

    /// <summary>单位为空时不追加空格，避免提示尾部出现悬空分隔。</summary>
    private static string FormatUnit(string unit)
    {
        return string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
    }
}

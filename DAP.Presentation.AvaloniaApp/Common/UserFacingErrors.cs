namespace DAP.Presentation.AvaloniaApp;

/// <summary>
/// 把异常转换为可以直接展示给用户的中文文案（与 Web 端规则一致）。
///
/// **为什么兜底 catch 绝不能拼 <c>ex.Message</c>**：
/// 网络类异常的消息是英文框架文案（如 <c>No such host is known.</c>、
/// <c>The operation was canceled.</c>），拼进状态栏等于把英文暴露给用户。
/// 按类型分支：超时 → 中文结论；业务异常（中文消息）→ 原样展示；
/// 网络不可达 → 中文结论；其余 → 统一提示，细节只进日志。
/// </summary>
public static class UserFacingErrors
{
    /// <summary>
    /// 返回适合展示给用户的错误描述。
    /// </summary>
    /// <param name="ex">捕获到的异常。</param>
    /// <param name="fallback">未识别异常类型时的兜底文案。</param>
    public static string Describe(Exception ex, string fallback = "操作失败，请查看服务端日志。")
    {
        return ex switch
        {
            OperationCanceledException => "操作超时，请稍后重试。",
            // 业务异常的消息是中文，框架异常（EF 并发、DI 解析等）是英文，
            // 用「有没有汉字」区分，避免英文技术细节出现在状态栏。
            InvalidOperationException when HasChinese(ex.Message) => ex.Message,
            InvalidOperationException => fallback,
            HttpRequestException => "网络请求失败，请检查网络连接后重试。",
            _ => fallback
        };
    }

    private static bool HasChinese(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }
}

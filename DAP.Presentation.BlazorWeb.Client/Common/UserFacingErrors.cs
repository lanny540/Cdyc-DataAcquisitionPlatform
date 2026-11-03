namespace DAP.Presentation.BlazorWeb.Client;

/// <summary>
/// 把异常转换为可以直接展示给用户的中文文案。
///
/// **为什么兜底 catch 绝不能拼 <c>ex.Message</c>**：
/// 网络类异常的消息是英文框架文案（如 <c>No such host is known. (host:443)</c>、
/// <c>The operation was canceled.</c>），拼进提示等于把英文和可能的内网地址
/// 直接暴露给用户。按四类分支处理：
/// <list type="bullet">
/// <item><see cref="OperationCanceledException"/>（超时）→ 统一中文结论；</item>
/// <item><see cref="InvalidOperationException"/>（服务端业务错误）→ 消息本身已是中文，可安全展示；</item>
/// <item><see cref="HttpRequestException"/>（网络不可达）→ 只给中文结论；</item>
/// <item>其余异常 → 统一提示，不外泄原始信息，细节只进服务端日志。</item>
/// </list>
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
            // InvalidOperationException 既承载业务错误（中文，可直接展示），
            // 也承载框架错误（EF 的「A second operation was started on this context...」
            // 之类，英文）。用「有没有汉字」区分两者：业务文案一定含中文。
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

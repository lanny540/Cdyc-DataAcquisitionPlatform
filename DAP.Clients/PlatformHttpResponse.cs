using System.Text.Json;

namespace DAP.Clients;

/// <summary>
/// 平台接口响应的统一错误处理。
///
/// 服务端业务错误以 <c>{ "message": "中文原因" }</c> 返回，校验错误以 RFC 7807
/// <c>ValidationProblem</c>（<c>errors</c> 字典）返回，这里把它们取出来抛成
/// <see cref="InvalidOperationException"/>——两端共用的 <c>UserFacingErrors</c> 约定：
/// <see cref="InvalidOperationException"/> 的消息已经是可直接展示的中文，其余类型不外泄。
/// </summary>
internal static class PlatformHttpResponse
{
    /// <summary>
    /// 非 2xx 时抛出带中文原因的异常，2xx 直接返回。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>
    /// 读取服务端返回的中文错误说明；无消息体时退回归一化的状态码描述。
    /// 支持两种响应格式：平台自定义的 <c>{ message }</c>，以及
    /// <c>Results.ValidationProblem</c> 产出的 RFC 7807 校验错误（<c>errors</c> 字典）。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可直接展示的中文消息。</returns>
    public static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return DescribeStatusCode(response.StatusCode);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            // 平台自定义格式：{ "message": "中文原因" }。
            if (root.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString()!.Trim();
            }

            // RFC 7807 校验错误：{ "errors": { "字段": ["原因", ...], ... } }。
            // 校验细节是用户把表单改对的关键线索，必须透出到界面；
            // ProblemDetails 的 title 是英文框架文案，不得展示，缺失时回落状态码描述。
            if (root.TryGetProperty("errors", out JsonElement errors) &&
                errors.ValueKind == JsonValueKind.Object)
            {
                List<string> messages = [];

                foreach (JsonProperty property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in property.Value.EnumerateArray())
                        {
                            CollectValidationMessage(item, messages);
                        }
                    }
                    else
                    {
                        CollectValidationMessage(property.Value, messages);
                    }
                }

                if (messages.Count > 0)
                {
                    return string.Join(" ", messages.Distinct());
                }
            }

            // ProblemDetails 的 detail 为服务端自定义业务说明（应为中文）。
            if (root.TryGetProperty("detail", out JsonElement detail) &&
                detail.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!.Trim();
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应（例如反向代理返回的 HTML 错误页）不原样回显，避免泄漏站点信息。
        }

        return DescribeStatusCode(response.StatusCode);
    }

    /// <summary>
    /// 从校验错误条目中收集可展示的中文消息，忽略非字符串或空白内容。
    /// </summary>
    private static void CollectValidationMessage(JsonElement element, List<string> messages)
    {
        if (element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(element.GetString()))
        {
            messages.Add(element.GetString()!.Trim());
        }
    }

    /// <summary>
    /// 把状态码翻译成中文结论，不拼接框架英文文案。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <returns>中文结论。</returns>
    private static string DescribeStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "未登录或会话已过期，请重新登录。",
            System.Net.HttpStatusCode.Forbidden => "当前账号没有访问该数据的权限。",
            System.Net.HttpStatusCode.NotFound => "请求的数据不存在或已被删除。",
            System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后再试。",
            System.Net.HttpStatusCode.RequestTimeout => "服务端处理超时，请稍后重试。",
            >= System.Net.HttpStatusCode.InternalServerError => "服务端处理失败，请查看服务端日志。",
            _ => $"请求失败（{(int)statusCode}）。"
        };
    }
}

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// CSV 字段转义（RFC 4180 + 公式注入防护）。
///
/// 抽成独立类型是为了能被单测直接钉住：导出文件是要拿出去传阅的，
/// 转义写错的后果不是「显示难看」，而是别人的 Excel 里执行一段命令。
/// </summary>
public static class CsvField
{
    /// <summary>
    /// 转义一个字段。
    /// </summary>
    /// <param name="value">原始值，可空。</param>
    /// <returns>可直接拼进 CSV 的字符串。</returns>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // 以 = + - @ 或制表符、回车开头的值会被 Excel 当成公式执行
        // （`=cmd|'/c calc'!A0` 这类）。前缀单引号是 Excel 认可的字面量标记：
        // 界面上不显示，但能阻止求值。
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = $"'{value}";
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}

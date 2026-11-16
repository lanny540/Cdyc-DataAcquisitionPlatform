namespace DAP.Presentation.AvaloniaApp.Services;

/// <summary>
/// 表示桌面客户端访问服务端 API 的配置。
/// </summary>
public sealed class ApiOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Api";

    /// <summary>
    /// 获取或设置 API 基地址。
    ///
    /// 必须带结尾斜杠：客户端内部一律使用以 <c>/api</c> 开头的相对地址，
    /// 基地址只决定主机与端口。
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5067/";

    /// <summary>
    /// 获取或设置单次请求超时时间（秒）。能耗报表会并发查询全部班组，不宜过短。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

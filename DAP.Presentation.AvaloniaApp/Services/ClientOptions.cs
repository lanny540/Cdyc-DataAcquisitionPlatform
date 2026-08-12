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
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5057/";
}

/// <summary>
/// 表示桌面客户端本地存储配置。
/// </summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "LocalStorage";

    /// <summary>
    /// 获取或设置 SQLite 数据库文件名称。
    /// </summary>
    public string DatabaseFileName { get; set; } = "collection-points.db";
}

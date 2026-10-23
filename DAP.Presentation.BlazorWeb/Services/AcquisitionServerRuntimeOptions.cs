namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 表示采集服务器运行时的进程级参数。
/// 服务器地址、凭据与接口路径一律保存在数据库的采集服务器表中，
/// 这里只保留与具体服务器无关的运行时开关。
/// </summary>
public sealed class AcquisitionServerRuntimeOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "AcquisitionServers";

    /// <summary>
    /// 全局 Mock 开关。为 <see langword="true"/> 时所有采集服务器都不访问真实上游，
    /// 仅用于前端联调与端到端测试；数据库中单个服务器的开关仍然生效。
    /// </summary>
    public bool UseMockResponses { get; set; }

    /// <summary>服务器未单独配置超时时间时使用的默认超时秒数。</summary>
    public int DefaultRequestTimeoutSeconds { get; set; } = 30;
}

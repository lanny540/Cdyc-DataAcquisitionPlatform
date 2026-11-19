namespace DAP.Presentation.AvaloniaApp.Common;

/// <summary>
/// 「刷新频率」下拉项。
/// </summary>
/// <param name="Seconds">间隔秒数。</param>
/// <param name="Label">显示文案。</param>
public sealed record RefreshIntervalOption(int Seconds, string Label)
{
    /// <summary>
    /// 间隔。
    /// </summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(Seconds);

    /// <summary>
    /// 下拉框未设模板时按此文本渲染。
    /// </summary>
    /// <returns>显示文案。</returns>
    public override string ToString()
    {
        return Label;
    }
}
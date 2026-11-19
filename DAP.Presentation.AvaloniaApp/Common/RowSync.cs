using System.Collections.ObjectModel;

namespace DAP.Presentation.AvaloniaApp.Common;

/// <summary>
/// 把「按时间倒序的最新 N 条」同步进已绑定的集合。
///
/// 直接 <c>Clear</c> 再逐条 <c>Add</c> 会让 DataGrid 整表重建：刷新瞬间表格闪一下、
/// 滚动位置被打回顶部，用户正在看的行会跑掉。由于新记录只会出现在头部、
/// 被挤出窗口的旧记录只会从尾部消失，命中这一常见形态时只做 Insert/Remove，
/// 已有行实例得以复用；形态不符（例如服务端补录导致中间缺行）再退回整表重建。
/// </summary>
public static class RowSync
{
    /// <summary>
    /// 同步集合内容。
    /// </summary>
    /// <typeparam name="T">行类型，需具备值相等语义（如 <c>record</c>）。</typeparam>
    /// <param name="target">目标集合（绑定到表格）。</param>
    /// <param name="desired">期望内容，按时间倒序。</param>
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        if (TryApplyHeadShift(target, desired)) return;

        target.Clear();

        foreach (T item in desired) target.Add(item);
    }

    /// <summary>
    /// 尝试只处理「头部插入 + 尾部淘汰」。
    /// </summary>
    /// <returns>已按最小改动完成返回 true；形态不符返回 false。</returns>
    private static bool TryApplyHeadShift<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        if (target.Count == 0)
        {
            foreach (T item in desired) target.Add(item);

            return true;
        }

        if (desired.Count == 0)
        {
            target.Clear();
            return true;
        }

        // 定位上一轮的第一条在最新内容里的位置：它前面就是本轮新增的条数。
        int shift = -1;

        for (int i = 0; i < desired.Count; i++)
            if (EqualityComparer<T>.Default.Equals(desired[i], target[0]))
            {
                shift = i;
                break;
            }

        // 找不到锚点，或新增条数超出「旧集合 + 头部插入」能解释的范围 → 交给整表重建。
        if (shift < 0 || desired.Count - shift > target.Count) return false;

        // 重叠区必须逐条一致，否则说明中间有缺行，不能只动头尾。
        int overlap = Math.Min(target.Count, desired.Count - shift);

        for (int i = 0; i < overlap; i++)
            if (!EqualityComparer<T>.Default.Equals(target[i], desired[shift + i]))
                return false;

        // 倒序插入，保证新增记录在头部的相对顺序与接口返回一致。
        for (int i = shift - 1; i >= 0; i--) target.Insert(0, desired[i]);

        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);

        return true;
    }
}
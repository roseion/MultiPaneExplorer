namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 详细信息视图的列布局（进程内全局一份，所有窗格共享）：列宽与隐藏列。
/// 变更通过 <see cref="Changed"/> 通知各窗格同步；持久化由 MainWindow 读写 SessionStore 完成。
/// </summary>
public static class ColumnLayoutStore
{
    public static Dictionary<string, double> Widths { get; } = new();

    /// <summary>隐藏列的键集合；名称列恒显示。</summary>
    public static HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

    public static event Action? Changed;

    public static void SetWidth(string columnKey, double width)
    {
        Widths[columnKey] = width;
        Changed?.Invoke();
    }

    public static void SetHidden(string columnKey, bool hidden)
    {
        if (hidden ? Hidden.Add(columnKey) : Hidden.Remove(columnKey))
            Changed?.Invoke();
    }

    public static void Reset()
    {
        if (Widths.Count == 0 && Hidden.Count == 0)
            return;
        Widths.Clear();
        Hidden.Clear();
        Changed?.Invoke();
    }

    /// <summary>会话恢复时批量装入，不触发 Changed（此时窗格尚未应用布局）。</summary>
    public static void Seed(IEnumerable<KeyValuePair<string, double>> widths, IEnumerable<string> hidden)
    {
        Widths.Clear();
        Hidden.Clear();
        foreach (var (key, width) in widths)
            Widths[key] = width;
        foreach (var key in hidden)
            Hidden.Add(key);
    }
}

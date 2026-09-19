using System.IO;
using System.Text.Json;

namespace FileOps.Core;

/// <summary>一个标签页的会话状态。</summary>
public sealed class PaneTabState
{
    public string? Path { get; set; }

    /// <summary>视图模式：Details / LargeIcons / List。</summary>
    public string? ViewMode { get; set; }
}

/// <summary>一个窗格的会话状态。</summary>
public sealed class PaneState
{
    public bool ShowTree { get; set; }

    /// <summary>文件树侧栏宽度（拖拽分隔条调节，随会话记忆）。</summary>
    public double TreeWidth { get; set; } = 200;

    public int ActiveTabIndex { get; set; }
    public List<PaneTabState> Tabs { get; set; } = [];
}

/// <summary>整个应用的会话状态。</summary>
public sealed class SessionState
{
    public string Layout { get; set; } = "Two";
    public bool ShowHiddenFiles { get; set; }

    /// <summary>界面整体缩放（Ctrl+= / Ctrl+- 调节，1.0 为原始大小）。</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>详细信息视图的全局列宽（列键→宽度），随会话记忆。</summary>
    public Dictionary<string, double>? ColumnWidths { get; set; }

    /// <summary>详细信息视图隐藏的列（名称列恒显示），随会话记忆。</summary>
    public List<string>? HiddenColumns { get; set; }

    /// <summary>是否显示预览窗格（全局右侧栏）。</summary>
    public bool ShowPreview { get; set; }

    /// <summary>预览窗格宽度。</summary>
    public double PreviewWidth { get; set; } = 260;

    /// <summary>主题：Light / Dark / System（跟随 Windows）。缺省跟随系统。</summary>
    public string Theme { get; set; } = "System";

    public List<PaneState> Panes { get; set; } = [];
}

/// <summary>会话持久化（%APPDATA%\MultiPaneExplorer\session.json）。</summary>
public static class SessionStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiPaneExplorer");
    private static readonly string SessionFilePath = Path.Combine(DirectoryPath, "session.json");

    public static void Save(SessionState state) => Save(state, SessionFilePath);

    public static SessionState? TryLoad() => TryLoad(SessionFilePath);

    /// <summary>保存到指定文件（测试可注入路径）。</summary>
    public static void Save(SessionState state, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>加载会话；文件缺失或损坏时返回 null（按全新会话处理）。</summary>
    public static SessionState? TryLoad(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>收藏夹持久化（%APPDATA%\MultiPaneExplorer\favorites.json）。</summary>
public static class FavoritesStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiPaneExplorer");
    private static readonly string FavoritesFilePath = Path.Combine(DirectoryPath, "favorites.json");

    public static List<string> Load() => Load(FavoritesFilePath);

    public static void Save(IReadOnlyList<string> favorites) => Save(favorites, FavoritesFilePath);

    public static List<string> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<string> favorites, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(favorites, new JsonSerializerOptions { WriteIndented = true }));
    }
}

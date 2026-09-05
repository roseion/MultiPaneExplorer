using System.IO;
using System.Text.Json;

namespace FileOps.Core;

/// <summary>一个标签页的会话状态。</summary>
public sealed class PaneTabState
{
    public string? Path { get; set; }
}

/// <summary>一个窗格的会话状态。</summary>
public sealed class PaneState
{
    public bool ShowTree { get; set; }
    public int ActiveTabIndex { get; set; }
    public List<PaneTabState> Tabs { get; set; } = [];
}

/// <summary>整个应用的会话状态。</summary>
public sealed class SessionState
{
    public string Layout { get; set; } = "Two";
    public bool ShowHiddenFiles { get; set; }
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

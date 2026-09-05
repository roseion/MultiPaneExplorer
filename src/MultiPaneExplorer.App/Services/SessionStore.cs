using System.IO;
using System.Text.Json;

namespace MultiPaneExplorer.App.Services;

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

public static class SessionStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiPaneExplorer");
    private static readonly string SessionFilePath = Path.Combine(DirectoryPath, "session.json");

    public static void Save(SessionState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static SessionState? TryLoad()
    {
        try
        {
            if (!File.Exists(SessionFilePath))
                return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionFilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // 会话文件损坏时按全新会话启动
        }
    }
}

public static class FavoritesStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiPaneExplorer");
    private static readonly string FavoritesFilePath = Path.Combine(DirectoryPath, "favorites.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FavoritesFilePath))
                return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesFilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<string> favorites)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FavoritesFilePath, JsonSerializer.Serialize(favorites, new JsonSerializerOptions { WriteIndented = true }));
    }
}

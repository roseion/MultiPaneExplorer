using System.IO;
using System.Text.Json;

namespace FileOps.Core;

/// <summary>
/// 常用目录访问计数（%APPDATA%\MultiPaneExplorer\frequent.json）。
/// 导航时 Record，收藏菜单"常用"分组取 Top-N；容量超限淘汰计数最小的条目。
/// </summary>
public static class FrequentStore
{
    private const int MaxEntries = 64;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MultiPaneExplorer",
        "frequent.json");

    public static void Record(string path) => Record(path, FilePath);

    public static IReadOnlyList<string> Top(int count) => Top(count, FilePath);

    /// <summary>记录一次访问（测试可注入路径）。</summary>
    public static void Record(string path, string filePath)
    {
        try
        {
            var counts = Load(filePath);
            counts[path] = counts.TryGetValue(path, out var visits) ? visits + 1 : 1;
            if (counts.Count > MaxEntries)
            {
                foreach (var key in counts
                             .OrderBy(pair => pair.Value)
                             .ThenByDescending(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                             .Take(counts.Count - MaxEntries)
                             .Select(pair => pair.Key)
                             .ToList())
                {
                    counts.Remove(key);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(counts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 计数失败不影响导航
        }
    }

    /// <summary>访问最多的前 count 个目录，按次数降序、路径名稳定排序（测试可注入路径）。</summary>
    public static IReadOnlyList<string> Top(int count, string filePath)
    {
        try
        {
            return Load(filePath)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .Select(pair => pair.Key)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static Dictionary<string, long> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return [];
            return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

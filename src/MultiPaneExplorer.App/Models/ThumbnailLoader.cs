using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 大图标视图的异步缩略图加载。常见图片用 WPF 解码（DecodePixelWidth=96，即时释放文件句柄），
/// 视频/PDF 走 Shell 缩略图提供程序（IShellItemImageFactory），其余回退系统 32px 大图标（不放大）。
/// 并发 2，结果冻结后回填 FsEntry.LargeIcon。
/// </summary>
public static class ThumbnailLoader
{
    private const int ThumbnailPixels = 96;

    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico",
    };

    /// <summary>依赖系统缩略图提供程序的类型（视频/PDF），失败回退系统大图标。</summary>
    private static readonly HashSet<string> ShellThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".mpg", ".mpeg", ".ts", ".flv",
        ".pdf",
    };

    /// <summary>入队加载；已完成（LargeIcon 非空）的条目自动跳过。</summary>
    public static void Enqueue(FsEntry entry)
    {
        if (entry.LargeIcon is not null)
            return;
        _ = Task.Run(() => LoadAsync(entry));
    }

    /// <summary>批量入队（目录列表加载/切换视图时）。</summary>
    public static void EnqueueRange(IEnumerable<FsEntry> entries)
    {
        foreach (var entry in entries)
            Enqueue(entry);
    }

    private static async Task LoadAsync(FsEntry entry)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.LargeIcon is not null)
                return;

            var extension = Path.GetExtension(entry.Name);
            var isShellThumbnail = ShellThumbnailExtensions.Contains(extension);
            ImageSource? source;
            if (entry.IsDirectory || (!ImageExtensions.Contains(extension) && !isShellThumbnail))
            {
                source = FileIconCache.GetLarge(entry);
            }
            else
            {
                var key = CacheKey(entry);
                // 缓存值不允许 null：未命中时现算，算不出不缓存（下次导航可重试）
                if (!Cache.TryGetValue(key, out source))
                {
                    source = (isShellThumbnail
                            ? ShellThumbnail.TryGetThumbnail(entry.FullPath, ThumbnailPixels)
                            : DecodeThumbnail(entry))
                        ?? FileIconCache.GetLarge(entry);
                    if (source is not null)
                        Cache[key] = source;
                }
            }

            Interlocked.Increment(ref _loadedCount);
            // PropertyChange 从线程池线程发出，WPF 绑定引擎自动封送回 UI 线程
            entry.LargeIcon = source;
        }
        catch
        {
            // 缩略图失败保持空值：视图回退到名称显示
        }
        finally
        {
            Gate.Release();
        }
    }

    private static int _loadedCount;

    /// <summary>本次进程累计加载成功的大图标/缩略图数量（诊断用）。</summary>
    public static int LoadedCount => _loadedCount;

    private static string CacheKey(FsEntry entry)
    {
        try
        {
            return $"{entry.FullPath}|{File.GetLastWriteTimeUtc(entry.FullPath).Ticks}";
        }
        catch (Exception)
        {
            return entry.FullPath;
        }
    }

    private static ImageSource? DecodeThumbnail(FsEntry entry)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // 立即读完并释放文件句柄
            image.DecodePixelWidth = ThumbnailPixels;
            image.UriSource = new Uri(entry.FullPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // 损坏/不支持的图片回退系统图标
        }
    }
}

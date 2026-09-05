using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileOps.Core;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 按扩展名/目录缓存文件图标（冻结的 BitmapSource 可跨线程共享）。
/// HICON 转换后立即销毁，避免 GDI 句柄泄漏。
/// </summary>
public static class FileIconCache
{
    private const string DirectoryKey = "<dir>";
    private const string NoExtensionKey = "<none>";

    private static readonly ShellIconService Icons = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> LargeCache = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> TreeIconCache = new();

    private const string RecycleBinParsingName = @"::{645FF040-5081-101B-9F08-00AA002F954E}";

    public static ImageSource? Get(FsEntry entry)
    {
        var key = entry.IsDirectory
            ? DirectoryKey
            : NormalizeKey(Path.GetExtension(entry.Name));
        return Cache.GetOrAdd(key, _ => Load(entry.IsDirectory, key, small: true));
    }

    /// <summary>32×32 大图标（大图标视图的非图片文件回退用，不放大）。</summary>
    public static ImageSource? GetLarge(FsEntry entry)
    {
        var key = entry.IsDirectory
            ? DirectoryKey
            : NormalizeKey(Path.GetExtension(entry.Name));
        // 不能用 GetOrAdd 存加载结果：Load 失败返回 null 时 ConcurrentDictionary 会抛异常
        if (!LargeCache.TryGetValue(key, out var source))
        {
            source = Load(entry.IsDirectory, key, small: false);
            if (source is not null)
                LargeCache[key] = source;
        }
        return source;
    }

    private static string NormalizeKey(string extension) =>
        string.IsNullOrEmpty(extension) ? NoExtensionKey : extension.ToLowerInvariant();

    /// <summary>文件树节点图标：盘符用真实盘符图标，回收站用 Shell 回收站图标，目录用文件夹图标，按路径缓存。</summary>
    public static ImageSource? GetTreeIcon(string fullPath, bool isRecycleBin) =>
        TreeIconCache.GetOrAdd(isRecycleBin ? "<bin>" : fullPath, _ => LoadTreeIcon(fullPath, isRecycleBin));

    private static ImageSource? LoadTreeIcon(string fullPath, bool isRecycleBin)
    {
        if (isRecycleBin)
            return FromHandle(Icons.GetPathSmallIcon(RecycleBinParsingName)) ?? Load(true, DirectoryKey, small: true);

        // 盘符根目录（"C:"/"C:\"）：按真实路径取盘符图标
        var root = Path.GetPathRoot(fullPath);
        if (root is not null &&
            string.Equals(root.TrimEnd(Path.DirectorySeparatorChar),
                          fullPath.TrimEnd(Path.DirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase))
        {
            var fromDrive = FromHandle(Icons.GetPathSmallIcon(root));
            if (fromDrive is not null)
                return fromDrive;
        }

        return Load(true, DirectoryKey, small: true);
    }

    private static ImageSource? FromHandle(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
            return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            _ = DestroyIcon(hIcon);
        }
    }

    private static ImageSource? Load(bool isDirectory, string cacheKey, bool small)
    {
        try
        {
            var name = isDirectory ? "文件夹" : cacheKey == NoExtensionKey ? "file.unknown" : "file" + cacheKey;
            var hIcon = small ? Icons.GetSmallIcon(name, isDirectory) : Icons.GetLargeIcon(name, isDirectory);
            if (hIcon == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                _ = DestroyIcon(hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

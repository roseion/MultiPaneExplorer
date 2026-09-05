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
        return LargeCache.GetOrAdd(key, _ => Load(entry.IsDirectory, key, small: false));
    }

    private static string NormalizeKey(string extension) =>
        string.IsNullOrEmpty(extension) ? NoExtensionKey : extension.ToLowerInvariant();

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

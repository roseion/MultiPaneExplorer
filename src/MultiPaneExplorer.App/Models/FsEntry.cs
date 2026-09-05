using System.IO;
using System.Windows.Media;

namespace MultiPaneExplorer.App.Models;

/// <summary>文件列表中的一个条目（文件夹 / 文件 / 驱动器）。</summary>
public sealed class FsEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime ModifiedTime { get; init; }

    /// <summary>类型图标（按扩展名缓存，冻结可跨线程）。</summary>
    public ImageSource? Icon => FileIconCache.Get(this);

    public string Type =>
        IsDirectory ? "文件夹"
        : Path.GetExtension(Name) is { Length: > 0 } extension
            ? $"{extension[1..].ToUpperInvariant()} 文件"
            : "文件";

    public string Size => IsDirectory ? string.Empty : FormatSize(SizeBytes ?? 0);

    public string Modified => ModifiedTime == default ? string.Empty : ModifiedTime.ToString("yyyy-MM-dd HH:mm");

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
    };
}

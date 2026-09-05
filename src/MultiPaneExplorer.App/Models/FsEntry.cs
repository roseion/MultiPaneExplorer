using System.IO;
using System.Windows.Media;

namespace MultiPaneExplorer.App.Models;

/// <summary>文件列表中的一个条目（文件夹 / 文件 / 驱动器 / 回收站项）。</summary>
public sealed class FsEntry : System.ComponentModel.INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime ModifiedTime { get; init; }

    /// <summary>回收站视图条目对应的元数据；普通文件系统条目为 null。</summary>
    public FileOps.Core.RecycleBinEntry? BinEntry { get; init; }

    /// <summary>类型图标（按扩展名缓存，冻结可跨线程）。</summary>
    public ImageSource? Icon => FileIconCache.Get(this);

    private ImageSource? _largeIcon;

    /// <summary>大图标视图用的 96px 缩略图/32px 图标，由 ThumbnailLoader 异步回填。</summary>
    public ImageSource? LargeIcon
    {
        get => _largeIcon;
        set
        {
            if (ReferenceEquals(_largeIcon, value))
                return;
            _largeIcon = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LargeIcon)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

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

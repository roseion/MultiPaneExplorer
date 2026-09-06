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

    /// <summary>创建时间（详细信息视图的可选列）；回收站/驱动器条目无此值。</summary>
    public DateTime CreatedTime { get; init; }

    /// <summary>递归搜索结果相对搜索根目录的子路径（"位置"列）；普通列表为空。</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>驱动器用量比例（"此电脑"页宽卡用）；非驱动器条目为 null。</summary>
    public double? DriveUsedFraction { get; init; }

    /// <summary>驱动器容量说明（"xx GB 可用，共 yy GB"）。</summary>
    public string DriveInfoText { get; init; } = string.Empty;

    /// <summary>驱动器宽卡的用量条宽度（条总宽 236px）。</summary>
    public double DriveBarWidth => DriveUsedFraction is { } fraction
        ? Math.Round(236 * Math.Clamp(fraction, 0, 1))
        : 0;

    /// <summary>回收站视图条目对应的元数据；普通文件系统条目为 null。</summary>
    public FileOps.Core.RecycleBinEntry? BinEntry { get; init; }

    /// <summary>类型图标（按扩展名缓存，冻结可跨线程）。</summary>
    public ImageSource? Icon => FileIconCache.Get(this);

    /// <summary>32px 图标（列表/树按 20px 显示时用它，避免 16px 放大发糊）。</summary>
    public ImageSource? IconLarge => FileIconCache.GetLarge(this);

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

    private bool _isRenaming;

    /// <summary>true 时名称单元格切换为内联编辑框（F2 原位重命名）。</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
                return;
            _isRenaming = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRenaming)));
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

    public string Created => CreatedTime == default ? string.Empty : CreatedTime.ToString("yyyy-MM-dd HH:mm");

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
    };
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 文件树节点：驱动器或目录。子目录懒加载——先放占位节点显示展开箭头，展开时再枚举真实子目录。
/// </summary>
public partial class FsTreeNode : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public string FullPath { get; }
    public string Name { get; }
    public ObservableCollection<FsTreeNode> Children { get; } = new();
    public bool IsDummy { get; }
    public bool ChildrenLoaded { get; private set; }

    /// <summary>节点图标（盘符/回收站/目录，按路径缓存，冻结可跨线程）。</summary>
    public ImageSource? Icon => FileIconCache.GetTreeIcon(FullPath, FullPath == SpecialLocations.RecycleBin);

    /// <summary>驱动器用量比例（0-1）；非驱动器节点为 null。</summary>
    public double? UsedFraction { get; }

    /// <summary>驱动器用量提示（已用/总量）；非驱动器节点为 null。</summary>
    public string? UsageTooltip { get; }

    /// <summary>驱动器有用量信息（模板据此显示用量条）。</summary>
    public bool HasUsageBar => UsedFraction.HasValue;

    /// <summary>用量条填充宽度（模板用，条总宽 110px）。</summary>
    public double UsageBarWidth => UsedFraction is { } fraction
        ? Math.Round(110 * Math.Clamp(fraction, 0, 1))
        : 0;

    private FsTreeNode(string fullPath, string name, bool isDummy)
    {
        FullPath = fullPath;
        Name = name;
        IsDummy = isDummy;
    }

    /// <summary>创建真实节点；探测到还有子目录时先放一个占位节点，用于显示展开箭头。</summary>
    public FsTreeNode(string fullPath, string? name = null)
    {
        FullPath = fullPath;
        Name = name ?? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (TryGetDriveUsage(fullPath, out var fraction, out var tooltip))
        {
            UsedFraction = fraction;
            UsageTooltip = tooltip;
        }
        if (HasSubDirectories(fullPath))
            Children.Add(Dummy);
    }

    /// <summary>盘符根目录（如 C:\）读取用量；非盘符或不可读返回 false。</summary>
    private static bool TryGetDriveUsage(string fullPath, out double fraction, out string tooltip)
    {
        (fraction, tooltip) = (0, string.Empty);
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (root is null
                || root.Length > 3 // 排除 UNC（\\server\share）
                || !string.Equals(
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
                return false;

            var usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            fraction = (double)usedBytes / drive.TotalSize;
            tooltip = $"已用 {usedBytes / 1024.0 / 1024 / 1024:F1} GB / 共 {drive.TotalSize / 1024.0 / 1024 / 1024:F1} GB";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static FsTreeNode Dummy { get; } = new(string.Empty, string.Empty, isDummy: true);

    /// <summary>枚举真实子目录，替换占位节点；重复调用无效果。特殊位置（回收站）没有子层。</summary>
    public void LoadChildren()
    {
        if (ChildrenLoaded)
            return;
        ChildrenLoaded = true;
        Children.Clear();
        if (FullPath == SpecialLocations.RecycleBin)
            return;
        foreach (var directory in EnumerateDirectoriesSafe(FullPath))
            Children.Add(new FsTreeNode(directory));
    }

    /// <summary>在树中定位并选中 fullPath，需要时逐级展开；返回是否定位成功。</summary>
    public bool TryReveal(string fullPath)
    {
        if (!IsAncestorOf(fullPath))
            return false;

        if (string.Equals(Normalize(FullPath), Normalize(fullPath), StringComparison.OrdinalIgnoreCase))
        {
            IsSelected = true;
            return true;
        }

        LoadChildren();
        IsExpanded = true;
        foreach (var child in Children)
        {
            if (child.TryReveal(fullPath))
                return true;
        }

        return false;
    }

    public bool IsAncestorOf(string path)
    {
        if (string.Equals(Normalize(FullPath), Normalize(path), StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = FullPath.EndsWith(Path.DirectorySeparatorChar) ? FullPath : FullPath + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool HasSubDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception)
        {
            return [];
        }
    }
}

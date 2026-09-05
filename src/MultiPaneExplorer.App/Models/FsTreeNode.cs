using System.Collections.ObjectModel;
using System.IO;
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
        if (HasSubDirectories(fullPath))
            Children.Add(Dummy);
    }

    public static FsTreeNode Dummy { get; } = new(string.Empty, string.Empty, isDummy: true);

    /// <summary>枚举真实子目录，替换占位节点；重复调用无效果。</summary>
    public void LoadChildren()
    {
        if (ChildrenLoaded)
            return;
        ChildrenLoaded = true;
        Children.Clear();
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

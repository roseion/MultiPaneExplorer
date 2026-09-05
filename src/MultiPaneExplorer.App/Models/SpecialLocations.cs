namespace MultiPaneExplorer.App.Models;

/// <summary>窗格可导航的特殊位置（非真实文件系统路径）。</summary>
public static class SpecialLocations
{
    /// <summary>回收站视图；仅作为 CurrentPath 的哨兵值，不参与文件系统 IO。</summary>
    public const string RecycleBin = "recyclebin:";
}

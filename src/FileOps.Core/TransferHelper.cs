using System.IO;

namespace FileOps.Core;

/// <summary>拖拽与移动的卷/路径辅助判断。</summary>
public static class TransferHelper
{
    /// <summary>两个路径是否位于同一卷（决定拖拽默认移动还是复制；任一路径无效视为不同卷）。</summary>
    public static bool IsSameVolume(string pathA, string pathB)
    {
        try
        {
            var rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
            var rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
            if (string.IsNullOrEmpty(rootA) || string.IsNullOrEmpty(rootB))
                return false;
            return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}

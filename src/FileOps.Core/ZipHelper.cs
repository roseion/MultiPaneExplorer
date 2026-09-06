using System.IO.Compression;

namespace FileOps.Core;

/// <summary>把若干文件/目录压缩为单个 zip（右键"压缩为 ZIP"）。</summary>
public static class ZipHelper
{
    /// <summary>
    /// 压缩 <paramref name="paths"/> 到 <paramref name="targetDirectory"/> 下的 baseName.zip；
    /// 重名自动追加 (2)/(3)，返回实际创建的 zip 路径。
    /// 文件按文件名入包；目录按"目录名/相对路径"整体入包（保留空目录）。
    /// </summary>
    public static string CreateZip(IReadOnlyList<string> paths, string targetDirectory, string baseName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            throw new ArgumentException("没有要压缩的路径", nameof(paths));
        if (!Directory.Exists(targetDirectory))
            throw new DirectoryNotFoundException($"目标目录不存在：{targetDirectory}");

        var zipPath = FileOperationService.GetAvailableNumberedPath(
            targetDirectory, baseName.Trim() + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
            }
            else if (Directory.Exists(path))
            {
                var root = Path.GetFileName(
                    path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                AddDirectory(archive, path, root);
            }
        }
        return zipPath;
    }

    private static void AddDirectory(ZipArchive archive, string directory, string entryRoot)
    {
        foreach (var sub in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, sub).Replace('\\', '/');
            archive.CreateEntry(entryRoot + "/" + relative + "/");
        }
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryRoot + "/" + relative, CompressionLevel.Optimal);
        }
    }
}

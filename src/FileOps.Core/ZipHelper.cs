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

    /// <summary>
    /// 解压 <paramref name="zipPath"/> 到 <paramref name="destinationRoot"/>（自动创建）。
    /// 保留包内目录结构与空目录；同名冲突自动追加 (2)/(3)，绝不覆盖已有文件；
    /// 防 zip slip：条目路径逃逸出目标目录时收敛到目标目录内。返回目标目录。
    /// </summary>
    public static string ExtractZip(string zipPath, string destinationRoot)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("压缩包不存在", zipPath);

        Directory.CreateDirectory(destinationRoot);
        var rootFull = Path.GetFullPath(destinationRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!target.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                target = Path.Combine(rootFull, Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)));
            }

            if (target.EndsWith(Path.DirectorySeparatorChar) || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target.TrimEnd(Path.DirectorySeparatorChar));
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (File.Exists(target))
                target = FileOperationService.GetAvailableNumberedPath(
                    parent ?? rootFull, Path.GetFileName(target));

            using var input = entry.Open();
            WriteEntryToFile(input, target);
        }
        return rootFull;
    }

    private static void WriteEntryToFile(Stream input, string target)
    {
        using var output = new FileStream(
            target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
        input.CopyTo(output);
    }

    private const int BufferSize = 64 * 1024;
}

using Microsoft.VisualBasic.FileIO;

namespace FileOps.Core;

/// <summary>基于 System.IO 与 Windows Shell（回收站）的文件操作实现。</summary>
public sealed class FileOperationService : IFileOperationService
{
    private const int BufferSize = 64 * 1024;

    public async Task<CopyResult> CopyIntoAsync(
        IEnumerable<string> sourcePaths,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (!Directory.Exists(targetDirectory))
            throw new DirectoryNotFoundException($"目标目录不存在：{targetDirectory}");

        var copied = 0;
        var errors = new List<string>();

        foreach (var source in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(source))
                {
                    var destination = GetAvailablePath(targetDirectory, Path.GetFileName(source));
                    await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
                    copied++;
                }
                else if (Directory.Exists(source))
                {
                    var directoryName = Path.GetFileName(
                        source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var destination = GetAvailablePath(targetDirectory, directoryName);
                    await CopyDirectoryAsync(source, destination, cancellationToken).ConfigureAwait(false);
                    copied++;
                }
                else
                {
                    errors.Add($"源不存在：{source}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or System.Security.SecurityException)
            {
                errors.Add($"复制失败：{source}（{ex.Message}）");
            }
        }

        return new CopyResult(copied, errors);
    }

    public Task<DeleteResult> DeleteToRecycleBinAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var deleted = 0;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    deleted++;
                }
                else if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    deleted++;
                }
                else
                {
                    errors.Add($"源不存在：{path}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException
                                          or System.Security.SecurityException)
            {
                errors.Add($"删除失败：{path}（{ex.Message}）");
            }
        }

        return Task.FromResult(new DeleteResult(deleted, errors));
    }

    public Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("新名称不能为空", nameof(newName));
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"名称包含非法字符：{newName}", nameof(newName));

        var parent = Path.GetDirectoryName(path)
                     ?? throw new ArgumentException($"路径没有父目录：{path}", nameof(path));
        var destination = Path.Combine(parent, newName);

        if (string.Equals(path, destination, StringComparison.Ordinal))
            return Task.FromResult(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"目标名称已存在：{newName}");

        if (File.Exists(path))
            File.Move(path, destination);
        else if (Directory.Exists(path))
            Directory.Move(path, destination);
        else
            throw new FileNotFoundException("源不存在", path);

        return Task.FromResult(destination);
    }

    public Task<string> CreateDirectoryAsync(string parentDirectory, string? name = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(parentDirectory))
            throw new DirectoryNotFoundException($"父目录不存在：{parentDirectory}");

        var baseName = string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name!;
        var destination = GetAvailableNumberedPath(parentDirectory, baseName);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        return Task.FromResult(destination);
    }

    /// <summary>在目标目录内为新建项找不冲突的名称：冲突时用"xx (2)"、"xx (3)"递增。</summary>
    internal static string GetAvailableNumberedPath(string targetDirectory, string name)
    {
        var candidate = Path.Combine(targetDirectory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(targetDirectory, $"{nameWithoutExtension} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>在目标目录内为 name 找一个不冲突的路径；无冲突时原样返回。</summary>
    internal static string GetAvailablePath(string targetDirectory, string name)
    {
        var candidate = Path.Combine(targetDirectory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var suffix = i == 1 ? " - 副本" : $" - 副本 ({i})";
            candidate = Path.Combine(targetDirectory, nameWithoutExtension + suffix + extension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(file, Path.Combine(destination, Path.GetFileName(file)), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(subDirectory, Path.Combine(destination, Path.GetFileName(subDirectory)), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

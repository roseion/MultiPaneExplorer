using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace FileOps.Core;

/// <summary>基于 System.IO 与 Windows Shell（回收站）的文件操作实现。</summary>
public sealed class FileOperationService : IFileOperationService
{
    private const int BufferSize = 64 * 1024;

    public async Task<CopyResult> CopyIntoAsync(
        IEnumerable<string> sourcePaths,
        string targetDirectory,
        CopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (!Directory.Exists(targetDirectory))
            throw new DirectoryNotFoundException($"目标目录不存在：{targetDirectory}");

        var sources = sourcePaths.ToList();
        var state = new CopyState(options, cancellationToken)
        {
            TotalBytes = sources.Sum(SafeSize),
        };

        foreach (var source in sources)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CopyItemAsync(source, targetDirectory, Path.GetFileName(
                        source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        isRoot: true, state).ConfigureAwait(false))
                {
                    state.CopiedCount++;
                }
                else
                {
                    state.SkippedCount++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or System.Security.SecurityException)
            {
                state.Errors.Add($"复制失败：{source}（{ex.Message}）");
            }
        }

        state.ReportProgress(currentFile: string.Empty, final: true);
        return new CopyResult(state.CopiedCount, state.SkippedCount, state.Errors, state.Transferred, state.ReplacedAny);
    }

    /// <summary>测试用：强制走"复制后删除源"的慢路径（模拟跨卷移动）。</summary>
    internal bool ForceSlowMove { get; set; }

    public async Task<CopyResult> MoveIntoAsync(
        IEnumerable<string> sourcePaths,
        string targetDirectory,
        CopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (!Directory.Exists(targetDirectory))
            throw new DirectoryNotFoundException($"目标目录不存在：{targetDirectory}");

        var sources = sourcePaths.ToList();
        var state = new CopyState(options, cancellationToken)
        {
            TotalBytes = sources.Sum(SafeSize),
        };

        foreach (var source in sources)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            var allowFastMove = !ForceSlowMove && TransferHelper.IsSameVolume(source, targetDirectory);
            try
            {
                if (await MoveItemAsync(source, targetDirectory, Path.GetFileName(
                        source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        isRoot: true, allowFastMove, state).ConfigureAwait(false))
                {
                    state.CopiedCount++;
                }
                else
                {
                    state.SkippedCount++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or System.Security.SecurityException)
            {
                state.Errors.Add($"移动失败：{source}（{ex.Message}）");
            }
        }

        state.ReportProgress(currentFile: string.Empty, final: true);
        return new CopyResult(state.CopiedCount, state.SkippedCount, state.Errors, state.Transferred, state.ReplacedAny);
    }

    private async Task<bool> MoveItemAsync(
        string source,
        string targetParent,
        string displayName,
        bool isRoot,
        bool allowFastMove,
        CopyState state)
    {
        // 目录：解析根冲突后逐项移入；Replace 到已存在目录时按"合并"处理
        if (Directory.Exists(source) && !File.Exists(source))
        {
            var (destination, _) = state.ResolveConflict(
                Path.Combine(targetParent, displayName), source, sourceIsDirectory: true);
            if (destination is null)
            {
                state.DoneBytes += SafeSize(source);
                state.ReportProgress(source);
                return false;
            }

            Directory.CreateDirectory(destination);
            if (isRoot)
                state.Transferred.Add(new TransferedItem(source, destination));
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                state.CancellationToken.ThrowIfCancellationRequested();
                await MoveItemAsync(entry, destination, Path.GetFileName(entry),
                        isRoot: false, allowFastMove, state).ConfigureAwait(false);
            }

            DeleteSourceQuietly(source, state, isDirectory: true);
            return true;
        }

        if (!File.Exists(source))
        {
            if (isRoot)
                state.Errors.Add($"源不存在：{source}");
            return false;
        }

        var (destFile, replaced) = state.ResolveConflict(
            Path.Combine(targetParent, displayName), source, sourceIsDirectory: false);
        if (destFile is null)
        {
            state.DoneBytes += SafeSize(source);
            state.ReportProgress(source);
            return false;
        }

        // 决定"替换"而目标恰好是同名文件夹时，按"移入该文件夹"处理
        if (replaced && Directory.Exists(destFile))
            destFile = Path.Combine(destFile, displayName);

        var size = new FileInfo(source).Length;
        if (allowFastMove && !Directory.Exists(destFile))
        {
            File.Move(source, destFile, replaced && File.Exists(destFile));
        }
        else
        {
            // 跨卷或目标类型不兼容：复制后删除源
            await CopyFileContentAsync(source, destFile, state).ConfigureAwait(false);
            DeleteSourceQuietly(source, state, isDirectory: false);
        }

        if (isRoot)
            state.Transferred.Add(new TransferedItem(source, destFile));
        state.DoneBytes += size;
        state.ReportProgress(source);
        return true;
    }

    private static async Task CopyFileContentAsync(string source, string destination, CopyState state)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), state.CancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), state.CancellationToken).ConfigureAwait(false);
            state.DoneBytes += read;
            state.ReportProgress(source);
        }
    }

    private static void DeleteSourceQuietly(string source, CopyState state, bool isDirectory)
    {
        try
        {
            if (isDirectory)
                Directory.Delete(source, recursive: true);
            else
                File.Delete(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException)
        {
            state.Errors.Add($"已移入目标，但删除源失败：{source}（{ex.Message}）");
        }
    }

    /// <summary>复制一个文件或整个目录（递归）。返回 false 表示因冲突被跳过。</summary>
    private async Task<bool> CopyItemAsync(
        string source,
        string targetParent,
        string displayName,
        bool isRoot,
        CopyState state)
    {
        if (File.Exists(source))
            return await CopyFileCoreAsync(source, targetParent, displayName, isRoot, state).ConfigureAwait(false);

        if (!Directory.Exists(source))
        {
            if (isRoot)
                state.Errors.Add($"源不存在：{source}");
            return false;
        }

        var destination = state.ResolveConflict(
            Path.Combine(targetParent, displayName),
            source,
            sourceIsDirectory: true).Destination;
        if (destination is null)
            return false;

        Directory.CreateDirectory(destination);
        if (isRoot)
            state.Transferred.Add(new TransferedItem(source, destination));
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            // 子项的复制/跳过不计入顶层结果统计，只计入进度与错误
            await CopyItemAsync(entry, destination, Path.GetFileName(entry), isRoot: false, state)
                .ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> CopyFileCoreAsync(
        string source,
        string targetParent,
        string displayName,
        bool isRoot,
        CopyState state)
    {
        var destination = state.ResolveConflict(
            Path.Combine(targetParent, displayName),
            source,
            sourceIsDirectory: false).Destination;
        if (destination is null)
        {
            // 跳过也要推进进度条
            state.DoneBytes += SafeSize(source);
            state.ReportProgress(source);
            return false;
        }

        await CopyFileContentAsync(source, destination, state).ConfigureAwait(false);
        if (isRoot)
            state.Transferred.Add(new TransferedItem(source, destination));
        return true;
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

    public Task<DeleteResult> DeletePermanentlyAsync(
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
                    File.Delete(path);
                    deleted++;
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    deleted++;
                }
                else
                {
                    errors.Add($"源不存在：{path}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or System.Security.SecurityException)
            {
                errors.Add($"永久删除失败：{path}（{ex.Message}）");
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

    public Task<string> CreateTextFileAsync(string parentDirectory, string? name = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(parentDirectory))
            throw new DirectoryNotFoundException($"父目录不存在：{parentDirectory}");

        var baseName = string.IsNullOrWhiteSpace(name) ? "新建文本文档.txt" : name!;
        var destination = GetAvailableNumberedPath(parentDirectory, baseName);
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(destination, string.Empty);
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

    private static long SafeSize(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;
            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Sum(file => file.Length);
            }
        }
        catch (Exception)
        {
            // 大小未知按 0 处理，不影响复制本身
        }

        return 0;
    }

    /// <summary>一次复制操作的共享状态：进度、冲突回调与统计。</summary>
    private sealed class CopyState
    {
        private long _lastReportTimestamp = Stopwatch.GetTimestamp();

        public CopyState(CopyOptions? options, CancellationToken cancellationToken)
        {
            Progress = options?.Progress;
            OnConflict = options?.OnConflict;
            CancellationToken = cancellationToken;
        }

        public IProgress<CopyProgress>? Progress { get; }
        public Func<ConflictContext, ConflictDecision>? OnConflict { get; }
        public CancellationToken CancellationToken { get; }

        public long TotalBytes { get; set; }
        public long DoneBytes { get; set; }
        public int CopiedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ConflictIndex { get; set; }
        public List<string> Errors { get; } = new();

        /// <summary>根级成功传输的（源 → 实际落点）明细，供撤销/重做使用。</summary>
        public List<TransferedItem> Transferred { get; } = new();

        /// <summary>是否发生过"替换"决策（覆盖文件或合并目录，此类结果不可撤销）。</summary>
        public bool ReplacedAny { get; set; }

        /// <summary>决定冲突目标的去向；返回 null 表示跳过，Replaced 表示覆盖现有目标。</summary>
        public (string? Destination, bool Replaced) ResolveConflict(
            string destination, string source, bool sourceIsDirectory)
        {
            if (!File.Exists(destination) && !Directory.Exists(destination))
                return (destination, false);

            var decision = OnConflict is null
                ? ConflictDecision.KeepBoth
                : OnConflict(new ConflictContext(
                    MakeConflictItem(source, destination, sourceIsDirectory),
                    ++ConflictIndex,
                    TotalConflicts: -1));

            return decision switch
            {
                ConflictDecision.Replace => ReplaceAndMark(destination),
                ConflictDecision.Skip => (null, false),
                _ => (GetAvailablePath(
                        Path.GetDirectoryName(destination) ?? string.Empty,
                        Path.GetFileName(destination)),
                    false),
            };

            (string? Destination, bool Replaced) ReplaceAndMark(string target)
            {
                ReplacedAny = true;
                return (target, true);
            }
        }

        public void ReportProgress(string currentFile, bool final = false)
        {
            if (Progress is null)
                return;

            var now = Stopwatch.GetTimestamp();
            if (!final && now - _lastReportTimestamp < Stopwatch.Frequency / 10)
                return;

            _lastReportTimestamp = now;
            Progress.Report(new CopyProgress(
                TotalBytes,
                final ? TotalBytes : Math.Min(DoneBytes, TotalBytes),
                currentFile));
        }

        private static ConflictItem MakeConflictItem(string source, string destination, bool sourceIsDirectory)
        {
            long sourceBytes;
            DateTime sourceModified;
            if (sourceIsDirectory)
            {
                sourceBytes = -1;
                sourceModified = Directory.GetLastWriteTime(source);
            }
            else
            {
                sourceBytes = new FileInfo(source).Length;
                sourceModified = File.GetLastWriteTime(source);
            }

            long existingBytes;
            DateTime existingModified;
            if (File.Exists(destination))
            {
                existingBytes = new FileInfo(destination).Length;
                existingModified = File.GetLastWriteTime(destination);
            }
            else
            {
                existingBytes = -1;
                existingModified = Directory.GetLastWriteTime(destination);
            }

            return new ConflictItem(source, destination, sourceBytes, sourceModified, existingBytes, existingModified);
        }
    }
}

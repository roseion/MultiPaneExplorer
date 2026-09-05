using System.IO;
using System.Security.Principal;
using System.Text;

namespace FileOps.Core;

/// <summary>
/// 通过直读各盘 $Recycle.Bin\{SID}\ 下的 $I（元数据）/$R（数据）文件操作回收站，
/// 不经过 Shell COM（规避第三方扩展在非 Explorer 宿主内的崩溃问题）。仅管理当前用户的回收站。
/// </summary>
public sealed class RecycleBinService : IRecycleBinService
{
    private readonly Func<IEnumerable<string>> _binDirectorySource;

    public RecycleBinService() : this(EnumerateUserBinDirectories) { }

    /// <summary>测试可注入回收站目录来源，把操作范围限定在临时目录。</summary>
    internal RecycleBinService(Func<IEnumerable<string>> binDirectorySource) =>
        _binDirectorySource = binDirectorySource;

    public Task<IReadOnlyList<RecycleBinEntry>> EnumerateAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RecycleBinEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<RecycleBinEntry>();
            foreach (var binDirectory in _binDirectorySource())
                entries.AddRange(ReadPairs(binDirectory, cancellationToken));
            return entries;
        }, cancellationToken);

    public Task<IReadOnlyList<string>> RestoreAsync(
        IEnumerable<RecycleBinEntry> entries, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var restored = new List<string>();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var target = RestoreCore(entry);
                    restored.Add(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or System.Security.SecurityException or ArgumentException)
                {
                    // 单条失败不影响其余条目；调用方以还原数量感知结果
                }
            }
            return restored;
        }, cancellationToken);

    public Task<int> DeletePermanentlyAsync(
        IEnumerable<RecycleBinEntry> entries, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var deleted = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DeletePairQuietly(entry))
                    deleted++;
            }
            return deleted;
        }, cancellationToken);

    public Task<int> EmptyAsync(CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<RecycleBinEntry>();
            foreach (var binDirectory in _binDirectorySource())
                entries.AddRange(ReadPairs(binDirectory, cancellationToken));
            return await DeletePermanentlyAsync(entries, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public IReadOnlyList<string> Snapshot()
    {
        var snapshots = new List<string>();
        foreach (var binDirectory in _binDirectorySource())
        {
            foreach (var infoPath in SafeEnumerateFiles(binDirectory, "$I*"))
                snapshots.Add(infoPath);
        }
        return snapshots;
    }

    public Task<IReadOnlyList<RecycleBinEntry>> DiffAsync(
        IReadOnlyList<string> beforeSnapshot, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RecycleBinEntry>>(() =>
        {
            var known = new HashSet<string>(beforeSnapshot, StringComparer.OrdinalIgnoreCase);
            var added = new List<RecycleBinEntry>();
            foreach (var binDirectory in _binDirectorySource())
            {
                foreach (var entry in ReadPairs(binDirectory, cancellationToken))
                {
                    if (!known.Contains(entry.InfoPath))
                        added.Add(entry);
                }
            }
            return added;
        }, cancellationToken);

    /// <summary>解析一个 $I 元数据文件；格式不符或文件损坏时返回 null。目录条目的 SizeBytes 约定为 -1。</summary>
    internal static RecycleBinEntry? ParseInfo(string infoPath, string itemPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(infoPath);
            if (bytes.Length < 24)
                return null;

            var version = BitConverter.ToInt32(bytes, 0);
            var size = BitConverter.ToInt64(bytes, 8);
            var deletedTime = DateTime.FromFileTime(BitConverter.ToInt64(bytes, 16));

            string originalPath;
            if (version >= 2)
            {
                // v2 布局：[0..4) 版本、[4..8) 未知、[8..16) 大小、[16..24) 删除时间、
                // [24..28) 名称字符数（INT32，含结尾 \0）、[28..) UTF-16 名称
                if (bytes.Length < 28)
                    return null;
                var nameChars = BitConverter.ToInt32(bytes, 24);
                if (nameChars <= 0 || bytes.Length < 28 + nameChars * 2)
                    return null;
                originalPath = Encoding.Unicode.GetString(bytes, 28, nameChars * 2).TrimEnd('\0');
            }
            else
            {
                // v1：520 字节定长 UTF-16 名称
                if (bytes.Length < 24 + 520)
                    return null;
                originalPath = Encoding.Unicode.GetString(bytes, 24, 520).TrimEnd('\0');
            }

            if (originalPath.Length == 0)
                return null;

            return new RecycleBinEntry(
                originalPath,
                Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                IsDirectory: size < 0,
                size,
                deletedTime,
                itemPath,
                infoPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<RecycleBinEntry> ReadPairs(string binDirectory, CancellationToken cancellationToken)
    {
        var entries = new List<RecycleBinEntry>();
        foreach (var infoPath in SafeEnumerateFiles(binDirectory, "$I*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemPath = Path.Combine(
                binDirectory,
                "$R" + Path.GetFileName(infoPath)[2..]); // $I<后缀> → $R<后缀>

            var entry = ParseInfo(infoPath, itemPath);
            if (entry is null)
                continue;

            // 磁盘上的 $R 实体是目录与否为准（$I 中目录大小记录为 0，仅靠 -1 约定不可靠）
            var isDirectory = Directory.Exists(itemPath) && !File.Exists(itemPath);
            if (isDirectory != entry.IsDirectory)
                entry = entry with { IsDirectory = isDirectory, SizeBytes = isDirectory ? -1 : entry.SizeBytes };

            entries.Add(entry);
        }
        return entries;
    }

    private string RestoreCore(RecycleBinEntry entry)
    {
        var parent = Path.GetDirectoryName(entry.OriginalPath);
        if (string.IsNullOrEmpty(parent))
            throw new IOException($"原始路径缺少父目录：{entry.OriginalPath}");
        Directory.CreateDirectory(parent);

        var target = entry.OriginalPath;
        if (File.Exists(target) || Directory.Exists(target))
        {
            target = FileOperationService.GetAvailablePath(parent, entry.OriginalName);
        }

        if (Directory.Exists(entry.ItemPath) && !File.Exists(entry.ItemPath))
            Directory.Move(entry.ItemPath, target);
        else
            File.Move(entry.ItemPath, target);

        try
        {
            File.Delete(entry.InfoPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 数据已还原，$I 清理失败只留下孤儿元数据，不影响结果
        }

        return target;
    }

    private static bool DeletePairQuietly(RecycleBinEntry entry)
    {
        try
        {
            if (Directory.Exists(entry.ItemPath) && !File.Exists(entry.ItemPath))
                Directory.Delete(entry.ItemPath, recursive: true);
            else if (File.Exists(entry.ItemPath))
                File.Delete(entry.ItemPath);
            else
                return false;

            if (File.Exists(entry.InfoPath))
                File.Delete(entry.InfoPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>当前用户在各本地盘的回收站目录（不存在则跳过）。</summary>
    private static IEnumerable<string> EnumerateUserBinDirectories()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
            yield break;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                continue;

            var binDirectory = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid!);
            if (Directory.Exists(binDirectory))
                yield return binDirectory;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            // $I/$R 是隐藏+系统文件，默认枚举会跳过它们
            return Directory.EnumerateFiles(directory, pattern, new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.None,
                IgnoreInaccessible = true,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}

namespace FileOps.Core;

/// <summary>回收站里的一条记录（对应一对 $I 元数据 + $R 数据文件）。</summary>
public sealed record RecycleBinEntry(
    string OriginalPath,
    string OriginalName,
    bool IsDirectory,
    long SizeBytes,
    DateTime DeletedTime,
    string ItemPath,
    string InfoPath);

/// <summary>回收站的枚举、还原与清理能力（仅当前用户自己的回收站）。</summary>
public interface IRecycleBinService
{
    /// <summary>枚举所有本地盘上当前用户的回收站条目。</summary>
    Task<IReadOnlyList<RecycleBinEntry>> EnumerateAsync(CancellationToken cancellationToken = default);

    /// <summary>把条目还原到删除前的位置；目标被占用时自动改名（"- 副本"），父目录缺失时自动补建。返回还原后的路径。</summary>
    Task<IReadOnlyList<string>> RestoreAsync(IEnumerable<RecycleBinEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>永久删除指定条目（不进回收站）。返回成功数。</summary>
    Task<int> DeletePermanentlyAsync(IEnumerable<RecycleBinEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>清空当前用户在所有本地盘的回收站。返回删除数。</summary>
    Task<int> EmptyAsync(CancellationToken cancellationToken = default);

    /// <summary>现存 $I 文件路径快照；与 <see cref="DiffAsync"/> 配合找出"这次删除"新增的条目。</summary>
    IReadOnlyList<string> Snapshot();

    /// <summary>对比快照，返回其后新增的回收站条目。</summary>
    Task<IReadOnlyList<RecycleBinEntry>> DiffAsync(IReadOnlyList<string> beforeSnapshot, CancellationToken cancellationToken = default);
}

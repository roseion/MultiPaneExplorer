namespace FileOps.Core;

/// <summary>一个同名冲突：来源与目标的信息对比。</summary>
public sealed record ConflictItem(
    string SourcePath,
    string TargetPath,
    long SourceBytes,
    DateTime SourceModified,
    long ExistingBytes,
    DateTime ExistingModified);

/// <summary>交给冲突处理回调的上下文；TotalConflicts 为 -1 表示总数未知。</summary>
public sealed record ConflictContext(ConflictItem Item, int Index, int TotalConflicts);

/// <summary>复制进度。DoneBytes/TotalBytes 为字节，DoneItems/TotalItems 为根级项目计数。</summary>
public sealed record CopyProgress(
    long TotalBytes,
    long DoneBytes,
    string CurrentFile,
    long DoneItems = 0,
    long TotalItems = 0)
{
    public int Percent => TotalBytes <= 0 ? 100 : (int)Math.Min(100, DoneBytes * 100 / TotalBytes);
}

/// <summary>复制行为选项：进度回报与冲突处理。OnConflict 为 null 时保持兼容旧行为（冲突自动改名保留两者）。</summary>
public sealed class CopyOptions
{
    /// <summary>进度回报；回调线程由 IProgress<T> 的实现决定。</summary>
    public IProgress<CopyProgress>? Progress { get; init; }

    /// <summary>遇到同名冲突时询问；返回 Replace/Skip/KeepBoth。</summary>
    public Func<ConflictContext, ConflictDecision>? OnConflict { get; init; }
}

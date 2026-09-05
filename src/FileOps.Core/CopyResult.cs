namespace FileOps.Core;

/// <summary>一次根级传输：源路径 → 实际落点（含"保留两者"改名后的落点）。撤销/重做依赖。</summary>
public sealed record TransferedItem(string Source, string Destination);

/// <summary>一次复制操作的结果统计。</summary>
public sealed record CopyResult(
    int CopiedCount,
    int SkippedCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<TransferedItem>? Transferred = null,
    bool ReplacedAny = false)
{
    /// <summary>是否存在未能复制的条目。</summary>
    public bool HasErrors => Errors.Count > 0;
}

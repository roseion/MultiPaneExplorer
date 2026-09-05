namespace FileOps.Core;

/// <summary>一次复制操作的结果统计。</summary>
public sealed record CopyResult(int CopiedCount, int SkippedCount, IReadOnlyList<string> Errors)
{
    /// <summary>是否存在未能复制的条目。</summary>
    public bool HasErrors => Errors.Count > 0;
}

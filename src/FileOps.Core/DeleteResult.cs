namespace FileOps.Core;

/// <summary>一次删除操作的结果统计。</summary>
public sealed record DeleteResult(int DeletedCount, IReadOnlyList<string> Errors)
{
    /// <summary>是否存在未能删除的条目。</summary>
    public bool HasErrors => Errors.Count > 0;
}

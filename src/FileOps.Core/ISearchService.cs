namespace FileOps.Core;

/// <summary>递归文件搜索。</summary>
public interface ISearchService
{
    /// <summary>
    /// 在 root 下（含全部子目录）查找名称包含 pattern 的文件与目录（大小写不敏感），
    /// 结果以完整路径增量产出。访问受限的子目录自动跳过。
    /// </summary>
    IAsyncEnumerable<string> SearchAsync(string root, string pattern, CancellationToken cancellationToken = default);
}

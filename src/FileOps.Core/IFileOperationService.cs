namespace FileOps.Core;

/// <summary>与 UI 无关的文件操作接口。</summary>
public interface IFileOperationService
{
    /// <summary>
    /// 把若干源路径（文件或目录）复制到目标目录内。
    /// 目标存在同名项时不覆盖，自动改名为"xx - 副本"、"xx - 副本 (2)"……
    /// 单个源失败不影响其余条目，失败原因记录在 <see cref="CopyResult.Errors"/> 中。
    /// </summary>
    Task<CopyResult> CopyIntoAsync(
        IEnumerable<string> sourcePaths,
        string targetDirectory,
        CancellationToken cancellationToken = default);
}

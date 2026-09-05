namespace FileOps.Core;

/// <summary>与 UI 无关的文件操作接口。</summary>
public interface IFileOperationService
{
    /// <summary>
    /// 把若干源路径（文件或目录）复制到目标目录内。
    /// 冲突处理：<paramref name="options"/> 未提供 OnConflict 回调时，同名冲突自动改名为
    /// "xx - 副本"、"xx - 副本 (2)"……（兼容旧行为）；提供回调时，每个冲突（含目录内的文件）
    /// 都会询问，由回调决定替换/跳过/保留两者。
    /// 单个源失败不影响其余条目，失败原因记录在 <see cref="CopyResult.Errors"/> 中。
    /// </summary>
    Task<CopyResult> CopyIntoAsync(
        IEnumerable<string> sourcePaths,
        string targetDirectory,
        CopyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把若干路径删除到系统回收站。单个失败不影响其余条目。
    /// </summary>
    Task<DeleteResult> DeleteToRecycleBinAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重命名文件或目录（保持在原目录内）。
    /// 名称非法或目标已存在时抛出异常；路径与新名称相同（仅大小写差异视为未变）时原样返回。
    /// </summary>
    Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在 parentDirectory 下新建文件夹；name 为空时使用"新建文件夹"，重名时自动追加序号。
    /// 返回新目录的完整路径。
    /// </summary>
    Task<string> CreateDirectoryAsync(string parentDirectory, string? name = null, CancellationToken cancellationToken = default);
}

using System.IO;
using System.Runtime.CompilerServices;

namespace FileOps.Core;

/// <summary>基于 EnumerationOptions 递归枚举的搜索实现。</summary>
public sealed class FileSystemSearchService : ISearchService
{
    public async IAsyncEnumerable<string> SearchAsync(
        string root,
        string pattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            yield break;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        IEnumerator<FileSystemInfo> enumerator;
        try
        {
            enumerator = new DirectoryInfo(root).EnumerateFileSystemInfos("*", options).GetEnumerator();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException)
        {
            // 根目录不存在或不可读：搜索直接结束
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or IOException)
                {
                    // 根目录消失或不可读：搜索到此为止
                    yield break;
                }

                if (!hasNext)
                    yield break;

                cancellationToken.ThrowIfCancellationRequested();

                if (enumerator.Current.Name.Contains(pattern, StringComparison.CurrentCultureIgnoreCase))
                    yield return enumerator.Current.FullName;

                // 定期让出执行权，避免大目录搜索长时间占用线程
                if (Random.Shared.Next(64) == 0)
                    await Task.Yield();
            }
        }
    }
}

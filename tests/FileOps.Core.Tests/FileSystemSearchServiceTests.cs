using System.IO;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class FileSystemSearchServiceTests : IDisposable
{
    private readonly FileSystemSearchService _service = new();
    private readonly string _root;

    public FileSystemSearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-search-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteFile(string relativePath, string content = "x")
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private async Task<List<string>> SearchAsync(string pattern)
    {
        var results = new List<string>();
        await foreach (var path in _service.SearchAsync(_root, pattern))
            results.Add(path);
        return results;
    }

    [Fact]
    public async Task Search_FindsFilesAndDirectoriesCaseInsensitively()
    {
        WriteFile("Alpha.txt");
        WriteFile("beta.log");
        WriteFile(Path.Combine("子目录", "ALPHA-dir"));
        WriteFile("gamma.txt");

        var results = await SearchAsync("alpha");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.EndsWith("Alpha.txt"));
        Assert.Contains(results, p => p.EndsWith("ALPHA-dir"));
        Assert.All(results, p => Assert.StartsWith(_root, p));
    }

    [Fact]
    public async Task Search_RecursesIntoNestedDirectories()
    {
        WriteFile(Path.Combine("a", "b", "c", "deep-report.txt"));
        WriteFile("report-top.txt");

        var results = await SearchAsync("report");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        WriteFile("alpha.txt");

        var results = await SearchAsync("不存在的关键字");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_EmptyPattern_YieldsNothing()
    {
        WriteFile("alpha.txt");

        var results = await SearchAsync("  ");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_Cancelled_ThrowsOperationCanceled()
    {
        WriteFile("alpha.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _service.SearchAsync(_root, "alpha", cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task Search_MissingRoot_ReturnsEmpty()
    {
        var results = new List<string>();
        await foreach (var path in _service.SearchAsync(Path.Combine(_root, "no-such-dir"), "alpha"))
            results.Add(path);

        Assert.Empty(results);
    }
}

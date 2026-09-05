using System.IO;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class MoveIntoAsyncTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly string _root;

    public MoveIntoAsyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-move-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFile(string relativePath, string content = "hello")
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string MakeDirectory(string relativePath)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private string Target() => MakeDirectory("target");

    [Fact]
    public async Task MoveFile_SameVolume_RemovesSourceAndCreatesTarget()
    {
        var source = WriteFile(Path.Combine("src", "a.txt"), "内容");
        var target = Target();

        var result = await _service.MoveIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.CopiedCount);
        Assert.False(File.Exists(source));
        Assert.Equal("内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task MoveDirectory_SameVolume_RemovesSourceAndMovesContents()
    {
        var source = MakeDirectory(Path.Combine("src", "folder"));
        WriteFile(Path.Combine("src", "folder", "inner.txt"), "嵌套内容");
        var target = Target();

        var result = await _service.MoveIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.False(Directory.Exists(source));
        Assert.Equal("嵌套内容", File.ReadAllText(Path.Combine(target, "folder", "inner.txt")));
    }

    [Fact]
    public async Task MoveFile_Replace_OverwritesExisting()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.MoveIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Replace });

        Assert.Equal(1, result.CopiedCount);
        Assert.False(File.Exists(source));
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task MoveFile_Skip_KeepsSourceAndTarget()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.MoveIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Skip });

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("新内容", File.ReadAllText(source));
        Assert.Equal("旧内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task MoveFile_KeepBoth_RenamesTarget()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.MoveIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.KeepBoth });

        Assert.Equal(1, result.CopiedCount);
        Assert.False(File.Exists(source));
        Assert.Equal("旧内容", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "a - 副本.txt")));
    }

    [Fact]
    public async Task MoveFile_ReplaceOntoFolderName_MovesIntoFolder()
    {
        var target = Target();
        MakeDirectory(Path.Combine(target, "a.txt"));
        var source = WriteFile(Path.Combine("src", "a.txt"), "内容");

        var result = await _service.MoveIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Replace });

        Assert.False(result.HasErrors);
        Assert.False(File.Exists(source));
        Assert.Equal("内容", File.ReadAllText(Path.Combine(target, "a.txt", "a.txt")));
    }

    [Fact]
    public async Task MoveDirectory_Replace_MergesIntoExistingFolder()
    {
        var target = Target();
        Directory.CreateDirectory(Path.Combine(target, "folder"));
        File.WriteAllText(Path.Combine(target, "folder", "inner.txt"), "旧内容");
        var source = MakeDirectory(Path.Combine("src", "folder"));
        WriteFile(Path.Combine("src", "folder", "inner.txt"), "新内容");
        WriteFile(Path.Combine("src", "folder", "extra.txt"), "新增");

        var result = await _service.MoveIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Replace });

        Assert.False(result.HasErrors);
        Assert.False(Directory.Exists(source));
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "folder", "inner.txt")));
        Assert.Equal("新增", File.ReadAllText(Path.Combine(target, "folder", "extra.txt")));
    }

    [Fact]
    public async Task MoveFile_ForceSlowMove_CopiesThenDeletesSource()
    {
        _service.ForceSlowMove = true;
        var source = WriteFile(Path.Combine("src", "a.txt"), "慢路径内容");
        var target = Target();

        var result = await _service.MoveIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.False(File.Exists(source));
        Assert.Equal("慢路径内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task MoveFile_ReportsProgressToTotal()
    {
        var source = WriteFile(Path.Combine("src", "big.bin"), new string('a', 256 * 1024));
        var target = Target();
        var reports = new List<CopyProgress>();

        await _service.MoveIntoAsync([source], target,
            new CopyOptions { Progress = new CollectingProgress(reports) });

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(256 * 1024L, r.TotalBytes));
        Assert.Equal(256 * 1024L, reports[^1].DoneBytes);
    }

    private sealed class CollectingProgress(List<CopyProgress> sink) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => sink.Add(value);
    }
}

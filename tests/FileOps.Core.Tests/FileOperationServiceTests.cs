using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class FileOperationServiceTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly string _root;

    public FileOperationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

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
    public async Task CopyFile_IntoTargetDirectory_CopiesContent()
    {
        var source = WriteFile(Path.Combine("src", "a.txt"), "内容A");

        var result = await _service.CopyIntoAsync([source], Target());

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("内容A", File.ReadAllText(Path.Combine(_root, "target", "a.txt")));
    }

    [Fact]
    public async Task CopyFile_WhenNameConflicts_AppendsCopySuffix()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.CopyIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.Equal("旧内容", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "a - 副本.txt")));
    }

    [Fact]
    public async Task CopyFile_WhenTwoConflictsExist_SecondSuffixNumbered()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "1");
        File.WriteAllText(Path.Combine(target, "a - 副本.txt"), "2");
        var source = WriteFile(Path.Combine("src", "a.txt"), "3");

        var result = await _service.CopyIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.Equal("3", File.ReadAllText(Path.Combine(target, "a - 副本 (2).txt")));
    }

    [Fact]
    public async Task CopyDirectory_CopiesNestedStructureRecursively()
    {
        var source = MakeDirectory(Path.Combine("src", "folder"));
        WriteFile(Path.Combine("src", "folder", "inner.txt"), "嵌套内容");
        WriteFile(Path.Combine("src", "folder", "sub", "deep.txt"), "更深内容");

        var result = await _service.CopyIntoAsync([source], Target());

        Assert.False(result.HasErrors);
        Assert.Equal("嵌套内容", File.ReadAllText(Path.Combine(_root, "target", "folder", "inner.txt")));
        Assert.Equal("更深内容", File.ReadAllText(Path.Combine(_root, "target", "folder", "sub", "deep.txt")));
    }

    [Fact]
    public async Task CopyDirectory_WhenNameConflicts_AppendsCopySuffix()
    {
        var target = Target();
        Directory.CreateDirectory(Path.Combine(target, "folder"));
        var source = MakeDirectory(Path.Combine("src", "folder"));
        WriteFile(Path.Combine("src", "folder", "f.txt"));

        var result = await _service.CopyIntoAsync([source], target);

        Assert.False(result.HasErrors);
        Assert.True(File.Exists(Path.Combine(target, "folder - 副本", "f.txt")));
    }

    [Fact]
    public async Task CopyInto_SourceMissing_RecordsErrorButCopiesRest()
    {
        var target = Target();
        var missing = Path.Combine(_root, "src", "ghost.txt");
        var file = WriteFile(Path.Combine("src", "real.txt"));

        var result = await _service.CopyIntoAsync([missing, file], target);

        Assert.True(result.HasErrors);
        Assert.Equal(1, result.CopiedCount);
        Assert.Single(result.Errors);
        Assert.Contains("ghost.txt", result.Errors[0]);
        Assert.True(File.Exists(Path.Combine(target, "real.txt")));
    }

    [Fact]
    public async Task CopyInto_MissingTargetDirectory_ThrowsDirectoryNotFoundException()
    {
        var source = WriteFile(Path.Combine("src", "a.txt"));
        var missingTarget = Path.Combine(_root, "no-such-dir");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _service.CopyIntoAsync([source], missingTarget));
    }

    [Fact]
    public async Task CopyFile_IntoItsOwnDirectory_CreatesCopyWithoutOverwriting()
    {
        var source = WriteFile(Path.Combine("inplace", "a.txt"), "原文");

        var result = await _service.CopyIntoAsync([source], Path.GetDirectoryName(source)!);

        Assert.False(result.HasErrors);
        Assert.Equal("原文", File.ReadAllText(source));
        Assert.Equal("原文", File.ReadAllText(Path.Combine(_root, "inplace", "a - 副本.txt")));
    }
}

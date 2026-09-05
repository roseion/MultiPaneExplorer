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

    [Fact]
    public async Task DeleteFile_ToRecycleBin_RemovesFromDirectory()
    {
        var file = WriteFile(Path.Combine("todelete", "a.txt"));

        var result = await _service.DeleteToRecycleBinAsync([file]);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.DeletedCount);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task DeleteDirectory_ToRecycleBin_RemovesFromDirectory()
    {
        var dir = MakeDirectory(Path.Combine("todelete", "folder"));
        WriteFile(Path.Combine("todelete", "folder", "f.txt"));

        var result = await _service.DeleteToRecycleBinAsync([dir]);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.DeletedCount);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Delete_MissingSource_RecordsErrorButDeletesRest()
    {
        var file = WriteFile(Path.Combine("todelete", "a.txt"));
        var missing = Path.Combine(_root, "todelete", "ghost.txt");

        var result = await _service.DeleteToRecycleBinAsync([missing, file]);

        Assert.True(result.HasErrors);
        Assert.Equal(1, result.DeletedCount);
        Assert.Single(result.Errors);
        Assert.Contains("ghost.txt", result.Errors[0]);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Rename_ChangesName()
    {
        var file = WriteFile(Path.Combine("ren", "a.txt"), "内容");

        var newPath = await _service.RenameAsync(file, "b.txt");

        Assert.Equal(Path.Combine(_root, "ren", "b.txt"), newPath);
        Assert.False(File.Exists(file));
        Assert.Equal("内容", File.ReadAllText(newPath));
    }

    [Fact]
    public async Task Rename_Directory_ChangesName()
    {
        var dir = MakeDirectory(Path.Combine("ren", "old"));

        var newPath = await _service.RenameAsync(dir, "new");

        Assert.Equal(Path.Combine(_root, "ren", "new"), newPath);
        Assert.True(Directory.Exists(newPath));
    }

    [Fact]
    public async Task Rename_ToExistingName_Throws()
    {
        var file = WriteFile(Path.Combine("ren", "a.txt"));
        WriteFile(Path.Combine("ren", "b.txt"));

        await Assert.ThrowsAsync<IOException>(() => _service.RenameAsync(file, "b.txt"));
    }

    [Fact]
    public async Task Rename_WithInvalidCharacters_Throws()
    {
        var file = WriteFile(Path.Combine("ren", "a.txt"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenameAsync(file, "a<b>.txt"));
    }

    [Fact]
    public async Task Rename_WithEmptyName_Throws()
    {
        var file = WriteFile(Path.Combine("ren", "a.txt"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenameAsync(file, "  "));
    }

    [Fact]
    public async Task Rename_SamePath_ReturnsUnchanged()
    {
        var file = WriteFile(Path.Combine("ren", "a.txt"));

        var result = await _service.RenameAsync(file, "a.txt");

        Assert.Equal(file, result);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task CreateDirectory_DefaultName_UsesNewFolder()
    {
        var parent = MakeDirectory("mkdir");

        var created = await _service.CreateDirectoryAsync(parent);

        Assert.Equal(Path.Combine(parent, "新建文件夹"), created);
        Assert.True(Directory.Exists(created));
    }

    [Fact]
    public async Task CreateDirectory_WhenNameConflicts_AutoIncrements()
    {
        var parent = MakeDirectory("mkdir");
        Directory.CreateDirectory(Path.Combine(parent, "新建文件夹"));

        var second = await _service.CreateDirectoryAsync(parent);
        var third = await _service.CreateDirectoryAsync(parent);

        Assert.Equal(Path.Combine(parent, "新建文件夹 (2)"), second);
        Assert.Equal(Path.Combine(parent, "新建文件夹 (3)"), third);
    }

    [Fact]
    public async Task CreateDirectory_WithCustomName_UsesIt()
    {
        var parent = MakeDirectory("mkdir");

        var created = await _service.CreateDirectoryAsync(parent, "图片备份");

        Assert.Equal(Path.Combine(parent, "图片备份"), created);
    }

    [Fact]
    public async Task CreateDirectory_MissingParent_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _service.CreateDirectoryAsync(Path.Combine(_root, "no-such-dir")));
    }

    [Fact]
    public async Task CopyFile_WithReplaceHandler_OverwritesExisting()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.CopyIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Replace });

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task CopyFile_WithSkipHandler_KeepsTargetAndCountsSkipped()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.CopyIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Skip });

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("旧内容", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.False(File.Exists(Path.Combine(target, "a - 副本.txt")));
    }

    [Fact]
    public async Task Copy_WithKeepBothHandler_RenamesLikeDefault()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");
        var source = WriteFile(Path.Combine("src", "a.txt"), "新内容");

        var result = await _service.CopyIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.KeepBoth });

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("旧内容", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "a - 副本.txt")));
    }

    [Fact]
    public async Task CopyDirectory_WithReplaceHandler_MergesIntoExisting()
    {
        var target = Target();
        Directory.CreateDirectory(Path.Combine(target, "folder"));
        File.WriteAllText(Path.Combine(target, "folder", "inner.txt"), "旧内容");
        var source = MakeDirectory(Path.Combine("src", "folder"));
        WriteFile(Path.Combine("src", "folder", "inner.txt"), "新内容");
        WriteFile(Path.Combine("src", "folder", "extra.txt"), "新增");

        var result = await _service.CopyIntoAsync([source], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Replace });

        Assert.False(result.HasErrors);
        Assert.Equal("新内容", File.ReadAllText(Path.Combine(target, "folder", "inner.txt")));
        Assert.Equal("新增", File.ReadAllText(Path.Combine(target, "folder", "extra.txt")));
        Assert.False(Directory.Exists(Path.Combine(target, "folder - 副本")));
    }

    [Fact]
    public async Task Copy_ConflictHandler_ReceivesConflictDetails()
    {
        var target = Target();
        File.WriteAllText(Path.Combine(target, "a.txt"), "12345");
        var source = WriteFile(Path.Combine("src", "a.txt"), "abc");

        ConflictContext? received = null;
        await _service.CopyIntoAsync([source], target,
            new CopyOptions { OnConflict = context => { received = context; return ConflictDecision.Replace; } });

        Assert.NotNull(received);
        Assert.Equal(source, received!.Item.SourcePath);
        Assert.Equal(Path.Combine(target, "a.txt"), received.Item.TargetPath);
        Assert.Equal(3, received.Item.SourceBytes);
        Assert.Equal(5, received.Item.ExistingBytes);
        Assert.Equal(1, received.Index);
    }

    [Fact]
    public async Task Copy_ReportsProgressAndReachesTotal()
    {
        var target = Target();
        var first = WriteFile(Path.Combine("src", "1.bin"), new string('a', 300 * 1024));
        var second = WriteFile(Path.Combine("src", "2.bin"), new string('b', 200 * 1024));
        var reports = new List<CopyProgress>();
        var progress = new CollectingProgress(reports);

        await _service.CopyIntoAsync([first, second], target, new CopyOptions { Progress = progress });

        var total = 500 * 1024L;
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(total, r.TotalBytes));
        Assert.Equal(total, reports[^1].DoneBytes);
        Assert.True(File.Exists(Path.Combine(target, "1.bin")));
        Assert.True(File.Exists(Path.Combine(target, "2.bin")));
    }

    [Fact]
    public async Task Copy_Cancelled_ThrowsOperationCanceled()
    {
        var target = Target();
        var source = WriteFile(Path.Combine("src", "big.bin"), new string('a', 1024 * 1024));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.CopyIntoAsync([source], target, cancellationToken: cts.Token));
    }

    private sealed class CollectingProgress(List<CopyProgress> sink) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => sink.Add(value);
    }
}

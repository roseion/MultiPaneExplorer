using System.IO;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

/// <summary>CopyResult 的传输明细（撤销/重做依赖）：根级成功条目按源顺序记录、覆盖标记、跳过不计入。</summary>
public sealed class TransferRecordTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly string _root;

    public TransferRecordTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-transfer-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task CopyIntoAsync_RecordsRootDestinationsInSourceOrder()
    {
        var sourceA = WriteFile("src/a.txt", "A");
        var sourceB = WriteFile("src/b.txt", "B");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        var result = await _service.CopyIntoAsync([sourceA, sourceB], target);

        Assert.False(result.ReplacedAny); // 默认无覆盖
        Assert.NotNull(result.Transferred);
        Assert.Equal(2, result.Transferred!.Count);
        Assert.Equal(sourceA, result.Transferred[0].Source);
        Assert.Equal(Path.Combine(target, "a.txt"), result.Transferred[0].Destination);
        Assert.Equal(sourceB, result.Transferred[1].Source);
        Assert.True(File.Exists(result.Transferred[1].Destination));
    }

    [Fact]
    public async Task CopyIntoAsync_DirectorySource_RecordsDestinationDirectory()
    {
        var sourceDir = Path.Combine(_root, "srcdir");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "inner.txt"), "内");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        var result = await _service.CopyIntoAsync([sourceDir], target);

        var transferred = Assert.Single(result.Transferred!);
        Assert.Equal(Path.Combine(target, "srcdir"), transferred.Destination);
        Assert.True(Directory.Exists(transferred.Destination));
        Assert.True(File.Exists(Path.Combine(transferred.Destination, "inner.txt")));
    }

    [Fact]
    public async Task CopyIntoAsync_ReplaceConflict_SetsReplacedAny()
    {
        var source = WriteFile("src/a.txt", "新内容");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "a.txt"), "旧内容");

        var result = await _service.CopyIntoAsync([source], target, new CopyOptions
        {
            OnConflict = _ => ConflictDecision.Replace,
        });

        Assert.True(result.ReplacedAny);
        var transferred = Assert.Single(result.Transferred!);
        Assert.Equal(Path.Combine(target, "a.txt"), transferred.Destination);
        Assert.Equal("新内容", File.ReadAllText(transferred.Destination));
    }

    [Fact]
    public async Task CopyIntoAsync_SkippedSource_NotRecordedInTransferred()
    {
        var sourceA = WriteFile("src/a.txt", "A");
        WriteFile("src/b.txt", "B");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "b.txt"), "旧B"); // 仅 b 构成冲突并被跳过

        var result = await _service.CopyIntoAsync([sourceA, Path.Combine(_root, "src/b.txt")], target,
            new CopyOptions { OnConflict = _ => ConflictDecision.Skip });

        Assert.False(result.ReplacedAny);
        var transferred = Assert.Single(result.Transferred!);
        Assert.Equal(sourceA, transferred.Source);
    }

    [Fact]
    public async Task MoveIntoAsync_RecordsRootDestinations()
    {
        var source = WriteFile("src/moved.txt", "M");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);

        var result = await _service.MoveIntoAsync([source], target);

        var transferred = Assert.Single(result.Transferred!);
        Assert.Equal(Path.Combine(target, "moved.txt"), transferred.Destination);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(transferred.Destination));
    }

    [Fact]
    public async Task MoveIntoAsync_ReplaceOntoDirectory_MarksReplacedAny()
    {
        var sourceDir = Path.Combine(_root, "srcdir");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "inner.txt"), "内");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(target, "srcdir"));
        File.WriteAllText(Path.Combine(target, "srcdir", "existing.txt"), "已在");

        var result = await _service.MoveIntoAsync([sourceDir], target, new CopyOptions
        {
            OnConflict = _ => ConflictDecision.Replace,
        });

        // Replace 到同名目录 = 合并，目标目录内容不可复原，不可撤销
        Assert.True(result.ReplacedAny);
        Assert.True(File.Exists(Path.Combine(target, "srcdir", "existing.txt")));
        Assert.True(File.Exists(Path.Combine(target, "srcdir", "inner.txt")));
    }
}

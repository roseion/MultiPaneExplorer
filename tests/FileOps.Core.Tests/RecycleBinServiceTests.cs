using System.IO;
using System.Text;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class RecycleBinServiceTests : IDisposable
{
    private readonly RecycleBinService _service;
    private readonly string _root;
    private readonly string _binDir;

    public RecycleBinServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-recyclebin-tests-" + Guid.NewGuid().ToString("N"));
        _binDir = Path.Combine(_root, "bin");
        Directory.CreateDirectory(_binDir);
        // 注入盘源，把枚举/清空的范围限定在临时目录，绝不触碰真实回收站
        _service = new RecycleBinService(() => [_binDir]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---- $I 元数据解析（v2 / v1 两种格式） ----

    [Fact]
    public void ParseInfo_V2_ExtractsOriginalPathSizeAndTime()
    {
        var deleted = new DateTime(2026, 9, 1, 12, 30, 0, DateTimeKind.Local);
        var infoPath = WriteInfoFile(BuildInfoV2(2048, deleted, @"C:\Users\me\报表.xlsx"), "info");

        var entry = RecycleBinService.ParseInfo(infoPath, itemPath: Path.Combine(_binDir, "$R1.xlsx"));

        Assert.NotNull(entry);
        Assert.Equal(@"C:\Users\me\报表.xlsx", entry!.OriginalPath);
        Assert.Equal("报表.xlsx", entry.OriginalName);
        Assert.False(entry.IsDirectory);
        Assert.Equal(2048, entry.SizeBytes);
        Assert.Equal(deleted, entry.DeletedTime);
        Assert.Equal(Path.Combine(_binDir, "$R1.xlsx"), entry.ItemPath);
        Assert.Equal(infoPath, entry.InfoPath);
    }

    [Fact]
    public void ParseInfo_V1_FixedWidthName_IsSupported()
    {
        var deleted = new DateTime(2025, 1, 2, 3, 4, 0, DateTimeKind.Local);
        var infoPath = WriteInfoFile(BuildInfoV1(10, deleted, @"D:\old\notes.txt"), "info");

        var entry = RecycleBinService.ParseInfo(infoPath, itemPath: Path.Combine(_binDir, "$R2.txt"));

        Assert.NotNull(entry);
        Assert.Equal(@"D:\old\notes.txt", entry!.OriginalPath);
        Assert.Equal(10, entry.SizeBytes);
        Assert.Equal(deleted, entry.DeletedTime);
    }

    [Fact]
    public void ParseInfo_TruncatedFile_ReturnsNull()
    {
        var infoPath = WriteInfoFile(new byte[] { 2, 0, 0, 0 }, "info");

        Assert.Null(RecycleBinService.ParseInfo(infoPath, itemPath: Path.Combine(_binDir, "$R4")));
    }

    // ---- 枚举：$I/$R 配对与原始路径回填 ----

    [Fact]
    public async Task EnumerateAsync_PairsInfoWithDataFiles()
    {
        PlaceBinPair("a.txt", @"C:\nowhere\a.txt", "内容");

        var entries = await _service.EnumerateAsync();

        var entry = Assert.Single(entries);
        Assert.Equal(@"C:\nowhere\a.txt", entry.OriginalPath);
        Assert.Equal("a.txt", entry.OriginalName);
        Assert.False(entry.IsDirectory);
        Assert.StartsWith("$R", Path.GetFileName(entry.ItemPath));
        Assert.True(File.Exists(entry.ItemPath));
    }

    // ---- 还原：移回原路径、删除 $I、父目录缺失自动补建、目标被占用时自动改名 ----

    [Fact]
    public async Task RestoreAsync_MovesItemBackAndDeletesInfoFile()
    {
        var originalPath = Path.Combine(_root, "deleted-parent", "restore.txt"); // 父目录不存在
        var (entry, _) = PlaceBinPair("restore.txt", originalPath, "内容");

        var restored = await _service.RestoreAsync([entry]);

        var restoredPath = Assert.Single(restored);
        Assert.Equal(originalPath, restoredPath);
        Assert.True(File.Exists(restoredPath));
        Assert.Equal("内容", File.ReadAllText(restoredPath));
        Assert.False(File.Exists(entry.InfoPath));
        Assert.False(File.Exists(entry.ItemPath));
    }

    [Fact]
    public async Task RestoreAsync_WhenTargetOccupied_RenamesWithCopySuffix()
    {
        var originalPath = Path.Combine(_root, "occupied.txt");
        File.WriteAllText(originalPath, "占用者");
        var (entry, _) = PlaceBinPair("occupied.txt", originalPath, "回收站里的");

        var restored = await _service.RestoreAsync([entry]);

        var restoredPath = Assert.Single(restored);
        Assert.NotEqual(originalPath, restoredPath);
        Assert.Equal("回收站里的", File.ReadAllText(restoredPath));
        Assert.Equal("占用者", File.ReadAllText(originalPath));
    }

    // ---- 永久删除与清空 ----

    [Fact]
    public async Task DeletePermanentlyAsync_RemovesItemAndInfo()
    {
        var (entry, itemPath) = PlaceBinPair("gone.txt", @"C:\nowhere\gone.txt", "数据");

        var deleted = await _service.DeletePermanentlyAsync([entry]);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(itemPath));
        Assert.False(File.Exists(entry.InfoPath));
    }

    [Fact]
    public async Task EmptyAsync_RemovesEveryPairInScope()
    {
        var (first, firstItem) = PlaceBinPair("a.txt", @"C:\nowhere\a.txt", "1");
        var (second, secondItem) = PlaceBinPair("b.txt", @"C:\nowhere\b.txt", "2");

        var deleted = await _service.EmptyAsync();

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(firstItem));
        Assert.False(File.Exists(secondItem));
        Assert.False(File.Exists(first.InfoPath));
        Assert.False(File.Exists(second.InfoPath));
    }

    // ---- 快照 diff：删除前后对比找出新增条目（撤销删除依赖） ----

    [Fact]
    public async Task DiffAsync_AfterPlacingNewPair_FindsOnlyNewEntry()
    {
        PlaceBinPair("old.txt", @"C:\nowhere\old.txt", "旧");
        var snapshot = _service.Snapshot();
        PlaceBinPair("new.txt", @"C:\nowhere\new.txt", "新");

        var added = await _service.DiffAsync(snapshot);

        var entry = Assert.Single(added);
        Assert.Equal(@"C:\nowhere\new.txt", entry.OriginalPath);
    }

    // ---- 测试脚手架：在临时目录里摆放一对命名对齐的 $I/$R 文件 ----

    /// <summary>放置一对 $I/$R 文件；返回解析出的条目与 $R 完整路径。</summary>
    private (RecycleBinEntry Entry, string ItemPath) PlaceBinPair(string itemName, string originalPath, string content)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var extension = Path.GetExtension(itemName);
        var itemPath = Path.Combine(_binDir, "$R" + suffix + extension);
        File.WriteAllText(itemPath, content);

        // 真实回收站中 $I 与 $R 共享同一文件名（含扩展名）
        var infoPath = Path.Combine(_binDir, "$I" + suffix + extension);
        File.WriteAllBytes(infoPath, BuildInfoV2(content.Length, DateTime.Now.AddMinutes(-5), originalPath));

        var entry = RecycleBinService.ParseInfo(infoPath, itemPath)
            ?? throw new InvalidOperationException("测试自身摆放的 $I 文件应可解析");
        return (entry, itemPath);
    }

    private string WriteInfoFile(byte[] bytes, string fileName)
    {
        var infoPath = Path.Combine(_binDir, "$I" + fileName + ".tmp");
        File.WriteAllBytes(infoPath, bytes);
        return infoPath;
    }

    /// <summary>$I v2：4 字节版本 + 4 字节未知 + 8 字节大小 + 8 字节 FILETIME + 4 字节名称字符数 + UTF-16 名称。</summary>
    private static byte[] BuildInfoV2(long size, DateTime deletionTime, string originalPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2); // 版本
        writer.Write(0); // 未知/填充字段
        writer.Write(size);
        writer.Write(deletionTime.ToFileTime());
        var nameBytes = Encoding.Unicode.GetBytes(originalPath + "\0"); // UTF-16LE，与真实 $I 一致
        writer.Write(nameBytes.Length / 2); // 含结尾 \0 的字符数（INT32）
        writer.Write(nameBytes);
        return stream.ToArray();
    }

    /// <summary>$I v1：4 字节版本 + 4 字节盘号 + 8 字节大小 + 8 字节 FILETIME + 520 字节定长 UTF-16 名称。</summary>
    private static byte[] BuildInfoV1(long size, DateTime deletionTime, string originalPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(2); // 盘符序号（C:）
        writer.Write(size);
        writer.Write(deletionTime.ToFileTime());
        var nameBytes = new byte[520];
        Encoding.Unicode.GetBytes(originalPath, 0, originalPath.Length, nameBytes, 0);
        writer.Write(nameBytes);
        return stream.ToArray();
    }
}

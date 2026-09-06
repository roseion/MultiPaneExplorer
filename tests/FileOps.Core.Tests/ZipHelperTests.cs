using System.IO.Compression;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

/// <summary>ZipHelper：多选压缩为单个 zip。</summary>
public sealed class ZipHelperTests : IDisposable
{
    private readonly string _root;

    public ZipHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-zip-tests-" + Guid.NewGuid().ToString("N"));
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
    public void CreateZip_MultipleFiles_PackagesAll()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var a = WriteFile(Path.Combine("src", "a.txt"), "AAA");
        var b = WriteFile(Path.Combine("src", "b.txt"), "BBB");

        var zipPath = ZipHelper.CreateZip([a, b], target, "pack");

        Assert.Equal(Path.Combine(target, "pack.zip"), zipPath);
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(["a.txt", "b.txt"], archive.Entries.Select(entry => entry.FullName).Order());
    }

    [Fact]
    public void CreateZip_Directory_KeepsStructure_WithEmptySub()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        WriteFile(Path.Combine("src", "folder", "sub", "file.txt"), "内容");
        Directory.CreateDirectory(Path.Combine(_root, "src", "folder", "empty"));
        var folder = Path.Combine(_root, "src", "folder");

        var zipPath = ZipHelper.CreateZip([folder], target, "backup");

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("folder/sub/file.txt", names);
        Assert.Contains("folder/empty/", names); // 空目录保留条目
    }

    [Fact]
    public void CreateZip_NameConflict_AppendsNumber()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var file = WriteFile("a.txt");

        var first = ZipHelper.CreateZip([file], target, "pack");
        var second = ZipHelper.CreateZip([file], target, "pack");

        Assert.Equal(Path.Combine(target, "pack.zip"), first);
        Assert.Equal(Path.Combine(target, "pack (2).zip"), second);
    }

    [Fact]
    public void CreateZip_EmptyPaths_Throws()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        Assert.Throws<ArgumentException>(() => ZipHelper.CreateZip([], target, "pack"));
    }

    // ---- 解压 ----

    [Fact]
    public void ExtractZip_KeepsStructure_AndEmptyDirs()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        WriteFile(Path.Combine("src", "folder", "sub", "file.txt"), "内容");
        Directory.CreateDirectory(Path.Combine(_root, "src", "folder", "empty"));
        var zip2 = ZipHelper.CreateZip([Path.Combine(_root, "src", "folder")], target, "pack2");

        var dest = Directory.CreateDirectory(Path.Combine(_root, "extract")).FullName;
        ZipHelper.ExtractZip(zip2, dest);

        Assert.Equal("内容", File.ReadAllText(Path.Combine(dest, "folder", "sub", "file.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "folder", "empty")));
    }

    [Fact]
    public void ExtractZip_NameConflict_AppendsNumber()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var file = WriteFile("a.txt", "打包内容");
        var zipPath = ZipHelper.CreateZip([file], target, "pack");

        var dest = Directory.CreateDirectory(Path.Combine(_root, "extract")).FullName;
        File.WriteAllText(Path.Combine(dest, "a.txt"), "已存在内容");

        ZipHelper.ExtractZip(zipPath, dest);

        Assert.Equal("已存在内容", File.ReadAllText(Path.Combine(dest, "a.txt"))); // 原文件不动
        Assert.Equal("打包内容", File.ReadAllText(Path.Combine(dest, "a (2).txt")));
    }

    [Fact]
    public void ExtractZip_ZipSlipEntry_StaysInsideDestination()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        var zipPath = Path.Combine(target, "evil.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            archive.CreateEntry("../evil.txt");

        var dest = Directory.CreateDirectory(Path.Combine(_root, "extract")).FullName;
        ZipHelper.ExtractZip(zipPath, dest);

        Assert.False(File.Exists(Path.Combine(_root, "evil.txt"))); // 逃逸失败
        Assert.True(File.Exists(Path.Combine(dest, "evil.txt")));   // 留在目标内
    }
}

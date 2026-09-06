using System.IO;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class WshShortcutServiceTests : IDisposable
{
    private readonly WshShortcutService _service = new();
    private readonly string _root;

    public WshShortcutServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-lnk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateShortcut(string shortcutPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell COM 不可用");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Save();
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    [Fact]
    public void ResolveTarget_FolderShortcut_ReturnsTargetDirectory()
    {
        var directory = Path.Combine(_root, "目标文件夹");
        Directory.CreateDirectory(directory);
        var shortcut = Path.Combine(_root, "文件夹快捷方式.lnk");
        CreateShortcut(shortcut, directory);

        var result = _service.ResolveTarget(shortcut);

        Assert.NotNull(result);
        Assert.Equal(Normalize(directory), Normalize(result!), ignoreCase: true);
    }

    [Fact]
    public void ResolveTarget_FileShortcut_ReturnsTargetFile()
    {
        var file = Path.Combine(_root, "目标.txt");
        File.WriteAllText(file, "内容");
        var shortcut = Path.Combine(_root, "文件快捷方式.lnk");
        CreateShortcut(shortcut, file);

        var result = _service.ResolveTarget(shortcut);

        Assert.NotNull(result);
        Assert.Equal(Normalize(file), Normalize(result!), ignoreCase: true);
    }

    [Fact]
    public void ResolveTarget_NonShortcutFile_ReturnsNull()
    {
        var file = Path.Combine(_root, "普通文件.txt");
        File.WriteAllText(file, "内容");

        Assert.Null(_service.ResolveTarget(file));
    }

    [Fact]
    public void ResolveTarget_MissingPath_ReturnsNull()
    {
        var missing = Path.Combine(_root, "不存在.lnk");

        Assert.Null(_service.ResolveTarget(missing));
    }

    [Fact]
    public void CreateShortcut_CreatesLink_ThatResolvesToTarget()
    {
        var file = Path.Combine(_root, "目标.txt");
        File.WriteAllText(file, "内容");
        var linkPath = Path.Combine(_root, "发送的快捷方式.lnk");

        _service.CreateShortcut(file, linkPath);

        Assert.True(File.Exists(linkPath));
        var result = _service.ResolveTarget(linkPath);
        Assert.NotNull(result);
        Assert.Equal(Normalize(file), Normalize(result!), ignoreCase: true);
    }

    [Fact]
    public void CreateShortcut_ToMissingDirectory_Throws()
    {
        var file = Path.Combine(_root, "目标.txt");
        File.WriteAllText(file, "内容");
        var linkPath = Path.Combine(_root, "缺失目录", "快捷方式.lnk");

        Assert.Throws<InvalidOperationException>(() => _service.CreateShortcut(file, linkPath));
    }
}

using System.IO;
using System.Runtime.InteropServices;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

public sealed class V140FeatureTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly ShellIconService _iconService = new();
    private readonly string _root;

    public V140FeatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-v140-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---- 图标提取 ----

    [Fact]
    public void GetSmallIcon_ForFileExtension_ReturnsIconHandle()
    {
        var hIcon = RunSta(() => _iconService.GetSmallIcon("C:\\ anywhere\\report.txt", isDirectory: false));

        Assert.NotEqual(IntPtr.Zero, hIcon);
        RunSta(() => _ = DestroyIcon(hIcon));
    }

    [Fact]
    public void GetSmallIcon_ForDirectory_ReturnsIconHandle()
    {
        var hIcon = RunSta(() => _iconService.GetSmallIcon("C:\\ anywhere\\某文件夹", isDirectory: true));

        Assert.NotEqual(IntPtr.Zero, hIcon);
        RunSta(() => _ = DestroyIcon(hIcon));
    }

    /// <summary>按真实路径取图标（文件树盘符节点用）：C:\ 应返回盘符图标句柄。</summary>
    [Fact]
    public void GetPathSmallIcon_ForDriveRoot_ReturnsIconHandle()
    {
        var hIcon = RunSta(() => _iconService.GetPathSmallIcon("C:\\"));

        Assert.NotEqual(IntPtr.Zero, hIcon);
        RunSta(() => _ = DestroyIcon(hIcon));
    }

    // ---- DropEffect 编解码 ----

    [Fact]
    public void DropEffectCodec_RoundTrips()
    {
        Assert.Equal(ClipboardDropEffect.Copy, DropEffectCodec.Decode(DropEffectCodec.Encode(ClipboardDropEffect.Copy)));
        Assert.Equal(ClipboardDropEffect.Move, DropEffectCodec.Decode(DropEffectCodec.Encode(ClipboardDropEffect.Move)));
    }

    [Fact]
    public void DropEffectCodec_InvalidPayload_ReturnsNone()
    {
        Assert.Equal(ClipboardDropEffect.None, DropEffectCodec.Decode(null));
        Assert.Equal(ClipboardDropEffect.None, DropEffectCodec.Decode([1, 2]));
        Assert.Equal(ClipboardDropEffect.None, DropEffectCodec.Decode([9, 0, 0, 0]));
    }

    // ---- 新建文本文档 ----

    [Fact]
    public async Task CreateTextFile_DefaultName_CreatesEmptyFile()
    {
        var created = await _service.CreateTextFileAsync(_root);

        Assert.Equal(Path.Combine(_root, "新建文本文档.txt"), created);
        Assert.True(File.Exists(created));
        Assert.Equal(string.Empty, File.ReadAllText(created));
    }

    [Fact]
    public async Task CreateTextFile_WhenNameConflicts_AutoIncrements()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "旧");
        File.WriteAllText(Path.Combine(_root, "notes (2).txt"), "旧2");

        var created = await _service.CreateTextFileAsync(_root, "notes.txt");

        Assert.Equal(Path.Combine(_root, "notes (3).txt"), created);
    }

    private static T RunSta<T>(Func<T> action)
    {
        T? result = default;
        var thread = new Thread(() => result = action());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result!;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

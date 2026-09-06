using System.Text;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

/// <summary>提权辅助进程执行器：载荷/结果 JSON 与四类操作的执行。</summary>
public sealed class ElevatedOpExecutorTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly string _root;

    public ElevatedOpExecutorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-elevated-tests-" + Guid.NewGuid().ToString("N"));
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
    public void Payload_Result_JsonRoundTrip()
    {
        var payloadPath = Path.Combine(_root, "payload.json");
        var payload = new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.CopyInto,
            Sources = [WriteFile("a.txt")],
            TargetDirectory = _root,
        };

        ElevatedOpExecutor.SavePayload(payload, payloadPath);
        var loaded = ElevatedOpExecutor.LoadPayload(payloadPath);

        Assert.Equal(ElevatedOpKind.CopyInto, loaded.Kind);
        Assert.Single(loaded.Sources);
        Assert.Equal(_root, loaded.TargetDirectory);

        var resultPath = payloadPath + ".result.json";
        ElevatedOpExecutor.SaveResult(new ElevatedOpResult { SuccessCount = 1 }, resultPath);
        var restored = ElevatedOpExecutor.TryLoadResult(resultPath);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.SuccessCount);
        Assert.False(restored.HasErrors);
    }

    [Fact]
    public async Task Execute_CopyInto_CopiesFiles()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var source = WriteFile(Path.Combine("src", "a.txt"), "内容");

        var result = ElevatedOpExecutor.Execute(new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.CopyInto,
            Sources = [source],
            TargetDirectory = target,
        }, _service);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal("内容", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.True(File.Exists(source)); // 复制保留源
    }

    [Fact]
    public async Task Execute_MoveInto_MovesFiles()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var source = WriteFile(Path.Combine("src", "a.txt"), "内容");

        var result = ElevatedOpExecutor.Execute(new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.MoveInto,
            Sources = [source],
            TargetDirectory = target,
        }, _service);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.SuccessCount);
        Assert.False(File.Exists(source));
        Assert.Equal("内容", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public void Execute_DeletePermanently_RemovesFiles()
    {
        var file = WriteFile("doomed.txt");

        var result = ElevatedOpExecutor.Execute(new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.DeletePermanently,
            Sources = [file],
        }, _service);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.SuccessCount);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Execute_MissingSources_RecordsErrors()
    {
        var result = ElevatedOpExecutor.Execute(new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.DeletePermanently,
            Sources = [Path.Combine(_root, "ghost.txt")],
        }, _service);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.SuccessCount);
    }

    /// <summary>删除到回收站走真实回收站后立即清掉测试项，避免污染。</summary>
    [Fact]
    public void Execute_DeleteToRecycleBin_SendsToBin()
    {
        var file = WriteFile("tobin.txt");

        var result = ElevatedOpExecutor.Execute(new ElevatedOpPayload
        {
            Kind = ElevatedOpKind.DeleteToRecycleBin,
            Sources = [file],
        }, _service);

        Assert.False(result.HasErrors);
        Assert.Equal(1, result.SuccessCount);
        Assert.False(File.Exists(file));

        // 清理回收站中的测试项（按 $I 文件名匹配本次删除的路径）
        CleanupRecycleBin(file);
    }

    private static void CleanupRecycleBin(string originalPath)
    {
        try
        {
            var root = Path.Combine(
                new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!)?.RootDirectory.FullName
                    ?? "C:\\", "$Recycle.Bin");
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var binDir = Path.Combine(root, sid);
            if (!Directory.Exists(binDir))
                return;
            foreach (var info in Directory.EnumerateFiles(binDir, "$I*", new EnumerationOptions
                     {
                         AttributesToSkip = FileAttributes.None,
                         IgnoreInaccessible = true,
                     }))
            {
                var bytes = File.ReadAllBytes(info);
                if (bytes.Length < 32)
                    continue;
                var nameLength = BitConverter.ToInt32(bytes, 24);
                if (nameLength <= 0 || 32 + nameLength * 2 > bytes.Length)
                    continue;
                var name = Encoding.Unicode.GetString(bytes, 32, nameLength * 2).TrimEnd('\0');
                if (!string.Equals(name, originalPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Delete(info);
                File.Delete(Path.Combine(binDir, "$R" + Path.GetFileName(info)[2..]));
            }
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }
}

using System.IO;
using System.Text.Json;

namespace FileOps.Core;

/// <summary>提权辅助进程可执行的操作类型。</summary>
public enum ElevatedOpKind
{
    CopyInto,
    MoveInto,
    DeleteToRecycleBin,
    DeletePermanently,
}

/// <summary>提权操作载荷：主进程写入 JSON，辅助进程读取执行。</summary>
public sealed class ElevatedOpPayload
{
    public ElevatedOpKind Kind { get; set; }

    public List<string> Sources { get; set; } = [];

    /// <summary>复制/移动的目标目录；删除类操作为 null。</summary>
    public string? TargetDirectory { get; set; }
}

/// <summary>提权操作执行结果。</summary>
public sealed class ElevatedOpResult
{
    public int SuccessCount { get; set; }

    public List<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// 提权辅助进程的执行端：在（提权的）本进程实例内执行载荷描述的操作。
/// 冲突不弹窗，自动改名"xx - 副本"保留两者；此路径的结果不进撤销栈
/// （撤销在普通权限下会再次失败）。
/// </summary>
public static class ElevatedOpExecutor
{
    public static ElevatedOpResult Execute(ElevatedOpPayload payload, IFileOperationService? ops = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var service = ops ?? new FileOperationService();
        var result = new ElevatedOpResult();
        try
        {
            switch (payload.Kind)
            {
                case ElevatedOpKind.CopyInto:
                    var copied = service.CopyIntoAsync(payload.Sources, payload.TargetDirectory!)
                        .GetAwaiter().GetResult();
                    result.SuccessCount = copied.CopiedCount;
                    result.Errors.AddRange(copied.Errors);
                    break;
                case ElevatedOpKind.MoveInto:
                    var moved = service.MoveIntoAsync(payload.Sources, payload.TargetDirectory!)
                        .GetAwaiter().GetResult();
                    result.SuccessCount = moved.CopiedCount;
                    result.Errors.AddRange(moved.Errors);
                    break;
                case ElevatedOpKind.DeleteToRecycleBin:
                    var binned = service.DeleteToRecycleBinAsync(payload.Sources)
                        .GetAwaiter().GetResult();
                    result.SuccessCount = binned.DeletedCount;
                    result.Errors.AddRange(binned.Errors);
                    break;
                case ElevatedOpKind.DeletePermanently:
                    var deleted = service.DeletePermanentlyAsync(payload.Sources)
                        .GetAwaiter().GetResult();
                    result.SuccessCount = deleted.DeletedCount;
                    result.Errors.AddRange(deleted.Errors);
                    break;
                default:
                    result.Errors.Add($"未知操作类型：{payload.Kind}");
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"提权操作执行失败：{ex.Message}");
        }
        return result;
    }

    public static void SavePayload(ElevatedOpPayload payload, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(payload));

    public static ElevatedOpPayload LoadPayload(string path) =>
        JsonSerializer.Deserialize<ElevatedOpPayload>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"提权载荷无效：{path}");

    public static void SaveResult(ElevatedOpResult result, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(result));

    public static ElevatedOpResult? TryLoadResult(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ElevatedOpResult>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

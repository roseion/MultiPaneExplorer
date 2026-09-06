using System.Diagnostics;
using System.IO;
using FileOps.Core;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// UAC 提权重试：把失败操作打包成载荷，用 runas 启动自身辅助进程
/// （--elevated-op）执行，结果经 JSON 回传。用户取消 UAC 或启动失败返回 null。
/// </summary>
public static class ElevatedRetry
{
    /// <summary>错误列表中是否存在"权限不足/拒绝访问"类失败。</summary>
    public static bool HasAccessDenied(IEnumerable<string> errors) =>
        errors.Any(error => error.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
            || error.Contains("未授权", StringComparison.OrdinalIgnoreCase)
            || error.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase));

    public static async Task<ElevatedOpResult?> RunAsync(ElevatedOpPayload payload)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || payload.Sources.Count == 0)
            return null;

        var payloadPath = Path.Combine(
            Path.GetTempPath(), "mpe-elevated-" + Guid.NewGuid().ToString("N") + ".json");
        ElevatedOpExecutor.SavePayload(payload, payloadPath);
        try
        {
            var startInfo = new ProcessStartInfo(
                exePath, $"{MultiPaneExplorer.App.App.ElevatedOpSwitch} \"{payloadPath}\"")
            {
                Verb = "runas", // 触发 UAC；用户拒绝时抛 Win32Exception
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;
            await process.WaitForExitAsync().ConfigureAwait(true);
            return ElevatedOpExecutor.TryLoadResult(payloadPath + ".result.json");
        }
        catch (Exception)
        {
            return null; // UAC 取消 / 提权被策略拒绝等
        }
        finally
        {
            TryDelete(payloadPath);
            TryDelete(payloadPath + ".result.json");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 临时文件清理失败可忽略
        }
    }
}

using System.IO;

namespace FileOps.Core;

/// <summary>基于 Windows Script Host COM 的 .lnk 目标解析实现。</summary>
public sealed class WshShortcutService : IShortcutService
{
    public string? ResolveTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)
            || !string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                      or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                      or InvalidOperationException)
        {
            return null;
        }
    }
}

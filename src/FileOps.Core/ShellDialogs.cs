using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileOps.Core;

/// <summary>
/// Windows 系统对话框集成（属性 / 打开方式），零 COM 互操作风险的 Shell 功能子集。
/// </summary>
public static class ShellDialogs
{
    private const uint SHOP_FILEPATH = 2;

    /// <summary>显示文件的系统"属性"对话框（与资源管理器右键属性一致）。</summary>
    public static bool ShowFileProperties(IntPtr ownerHwnd, string path) =>
        SHObjectProperties(ownerHwnd, SHOP_FILEPATH, path, propertySheet: null);

    /// <summary>显示系统"打开方式"对话框，让用户选择用哪个程序打开文件。</summary>
    public static void ShowOpenWithDialog(string path)
    {
        // rundll32 调用 shell32 的 OpenAs_RunDLL：系统标准的"你要如何打开这个文件？"对话框
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL {path}",
            UseShellExecute = true,
        });
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string objectName, string? propertySheet);
}

using System.Runtime.InteropServices;

namespace FileOps.Core;

/// <summary>
/// 按扩展名/目录属性提取 Shell 文件图标句柄（HICON，16×16 小图标）。
/// 使用 SHGFI_USEFILEATTRIBUTES：只看扩展名与属性，不实际读取文件本身。
/// 调用方负责用 DestroyIcon 释放返回的句柄（建议上层按扩展名缓存）。
/// </summary>
public sealed class ShellIconService
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    /// <summary>提取小图标句柄；失败返回 IntPtr.Zero。</summary>
    public IntPtr GetSmallIcon(string pathOrExtension, bool isDirectory)
    {
        var name = isDirectory ? "文件夹" : EnsureExtension(pathOrExtension);
        var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(
            name, attributes, ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

        return result != 0 ? info.hIcon : IntPtr.Zero;
    }

    private static string EnsureExtension(string pathOrExtension)
    {
        var name = Path.GetFileName(pathOrExtension);
        if (string.IsNullOrEmpty(name))
            name = pathOrExtension;
        return Path.HasExtension(name) ? name : name + ".unknown";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint fileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

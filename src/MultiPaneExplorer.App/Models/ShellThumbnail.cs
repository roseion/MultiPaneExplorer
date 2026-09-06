using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 基于 IShellItemImageFactory 的 Shell 缩略图提取（视频/PDF 等依赖系统缩略图提供程序的类型）。
/// 全部调用包裹 try/catch：COM 不可用或无提供程序时返回 null，由调用方回退系统大图标。
/// 已知限制：缩略图提供程序在进程内加载，个别第三方扩展可能不稳，但失败仅表现为无缩略图。
/// </summary>
public static class ShellThumbnail
{
    public static ImageSource? TryGetThumbnail(string path, int pixels)
    {
        try
        {
            var factoryId = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryId, out var factory);
            if (factory is null)
                return null;

            var hr = factory.GetImage(new SIZE(pixels, pixels), SIIGBF.Resizetofit, out var hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze(); // 冻结后可跨线程回填
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [Flags]
    private enum SIIGBF
    {
        Resizetofit = 0,
        Biggersizeok = 1,
        Memoryonly = 2,
        Icononly = 4,
        Thumbnailonly = 8,
        Incacheonly = 0x10,
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}

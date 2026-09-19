using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FileOps.Core;

namespace MultiPaneExplorer.App.Models;

/// <summary>
/// 主题管理：浅/深/跟随系统三态。通过整体替换 App 资源里的 token 合并字典实现即时切换；
/// "跟随系统"读取注册表 AppsUseLightTheme 并监听 WM_SETTINGCHANGE 实时响应。
/// </summary>
public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string System = "System";

    private static IntPtr _hwnd;
    private static HwndSourceHook? _hook;

    public static string CurrentEffective { get; private set; } = Light;

    /// <summary>有效主题变化后触发（含首次 Apply）；用于重建渐变背景等无法经 DynamicResource 更新的视觉。</summary>
    public static event Action? ThemeChanged;

    /// <summary>应用主题设置（System 先解析为实际值）。在 UI 线程调用。</summary>
    public static void Apply(string theme)
    {
        var effective = theme switch
        {
            Dark => Dark,
            Light => Light,
            _ => IsSystemDark() ? Dark : Light,
        };
        var changed = effective != CurrentEffective;
        CurrentEffective = effective;

        var uri = new Uri(
            $"pack://application:,,,/Themes/Tokens.{effective}.xaml", UriKind.Absolute);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Tokens.") == true);
        var newDict = new ResourceDictionary { Source = uri };
        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = newDict; // 原位替换，保持合并顺序（token 在样式前）
        }
        else
        {
            dictionaries.Insert(0, newDict);
        }

        HookSystemThemeChanges();
        if (changed)
            ThemeChanged?.Invoke();
    }

    /// <summary>构建当前主题的窗底渐变画刷（左上→右下，蓝-薄荷 Air 感）。</summary>
    public static LinearGradientBrush BuildBackdropBrush()
    {
        var (top, bottom) = CurrentEffective == Dark
            ? (Color.FromRgb(0x24, 0x26, 0x2B), Color.FromRgb(0x19, 0x1B, 0x20))
            : (Color.FromRgb(0xF3, 0xF8, 0xF5), Color.FromRgb(0xE7, 0xEF, 0xFB));
        return new LinearGradientBrush(top, bottom, 45);
    }

    /// <summary>当前会话主题字符串（供保存）。</summary>
    public static string SelectedTheme { get; private set; } = System;

    /// <summary>带记忆的应用：记录用户选择并生效。</summary>
    public static void ApplySelected(string theme)
    {
        SelectedTheme = theme is Dark or Light or System ? theme : System;
        Apply(SelectedTheme);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 挂主窗口消息钩子：系统主题变化（WM_SETTINGCHANGE）且当前为跟随模式时立即重应用。
    /// </summary>
    private static void HookSystemThemeChanges()
    {
        if (_hook is not null)
            return; // 已挂过
        var window = Application.Current.MainWindow;
        if (window is null)
            return;
        var source = PresentationSource.FromVisual(window) as HwndSource;
        if (source is null)
            return;

        _hwnd = source.Handle;
        _hook = WndProc;
        source.AddHook(_hook);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg != WM_SETTINGCHANGE || SelectedTheme != System)
            return IntPtr.Zero;

        // lParam 指向 "ImmersiveColorSet" 时才是主题切换
        try
        {
            var section = Marshal.PtrToStringUni(lParam);
            if (section == "ImmersiveColorSet")
            {
                Apply(ThemeManager.System); // 按系统最新状态重解析
            }
        }
        catch
        {
            // 消息解析失败忽略
        }
        return IntPtr.Zero;
    }
}

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

    /// <summary>强调色可选集（索引即色板序号）；浅/深两套端值。</summary>
    public static readonly (string Name, Color Light, Color Dark)[] Accents =
    [
        ("蓝", Color.FromRgb(0x0A, 0x60, 0xFF), Color.FromRgb(0x40, 0x9C, 0xFF)),
        ("紫", Color.FromRgb(0x7B, 0x5C, 0xF5), Color.FromRgb(0xA6, 0x8B, 0xFF)),
        ("绿", Color.FromRgb(0x0E, 0x9F, 0x6E), Color.FromRgb(0x2C, 0xC5, 0x8C)),
        ("粉", Color.FromRgb(0xE5, 0x46, 0x7F), Color.FromRgb(0xF2, 0x6B, 0x9C)),
        ("橙", Color.FromRgb(0xF0, 0x79, 0x3C), Color.FromRgb(0xF7, 0x9B, 0x68)),
    ];

    /// <summary>当前强调色索引（0-4）。</summary>
    public static int SelectedAccentIndex { get; private set; }

    /// <summary>按当前有效主题应用强调色（主题切换后也需再调一次）。
    /// 实现方式：构建独立强调色字典（4 支新画刷）插入合并字典末尾——后加入的字典优先级最高，
    /// 覆盖 token 字典里的同名键；整体替换而非原地改色（运行时加进 App 资源的画刷会被密封冻结）。</summary>
    public static void ApplyAccent(int index)
    {
        SelectedAccentIndex = Math.Clamp(index, 0, Accents.Length - 1);
        var accent = Accents[SelectedAccentIndex];
        var main = CurrentEffective == Dark ? accent.Dark : accent.Light;
        Color Brighten(float factor, byte alpha) => Color.FromArgb(
            alpha,
            (byte)Math.Clamp((int)Math.Round(main.R * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(main.G * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(main.B * factor), 0, 255));

        var accentDict = new ResourceDictionary
        {
            ["W11.Accent"] = new SolidColorBrush(main),
            ["W11.AccentHover"] = new SolidColorBrush(Brighten(
                CurrentEffective == Dark ? 1.12f : 1.25f, 0xFF)),
            ["W11.AccentPressed"] = new SolidColorBrush(Brighten(
                CurrentEffective == Dark ? 0.82f : 0.72f, 0xFF)),
            ["W11.AccentTint"] = new SolidColorBrush(Brighten(
                CurrentEffective == Dark ? 0.30f : 2.2f, 0xE6)),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_accentDictionary is not null)
        {
            var at = dictionaries.IndexOf(_accentDictionary);
            if (at >= 0)
            {
                dictionaries[at] = accentDict; // 原位替换
                _accentDictionary = accentDict;
                return;
            }
        }
        dictionaries.Add(accentDict); // 后加入的合并字典查找优先级最高
        _accentDictionary = accentDict;
    }

    private static ResourceDictionary? _accentDictionary;

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
        ApplyAccent(SelectedAccentIndex); // 强调色随主题重应用（深浅端值不同）
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

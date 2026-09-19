using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiPaneExplorer.App.Models;

/// <summary>true→Collapsed、false→Visible：回收站视图下隐藏不适用的普通菜单项。</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>空字符串/null→Collapsed：状态栏"可用空间"等可选文本的显隐。</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>整数→主题里的彩卡底色画刷（W11.Tint{0-3}）：此电脑页驱动器彩卡用。</summary>
public sealed class TintBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var index = value is int i ? Math.Clamp(i, 0, 3) : 0;
        return System.Windows.Application.Current?.TryFindResource($"W11.Tint{index}")
               as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

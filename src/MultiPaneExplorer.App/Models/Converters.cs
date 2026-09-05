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

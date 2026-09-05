using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiPaneExplorer.App.Views;

/// <summary>极简重命名输入对话框：预填当前名称，确定后由调用方执行重命名。</summary>
public sealed class RenameDialog : Window
{
    private readonly TextBox _input;

    public string InputText => _input.Text;

    public RenameDialog(string currentName)
    {
        Title = "重命名";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _input = new TextBox { Margin = new Thickness(12, 12, 12, 4), Text = currentName };
        var ok = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };

        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_input.Text))
                DialogResult = true;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Content = new StackPanel { Children = { _input, buttons } };
        Loaded += (_, _) =>
        {
            _input.SelectAll();
            _input.Focus();
        };
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileOps.Core;

namespace MultiPaneExplorer.App.Views;

/// <summary>
/// 同名冲突对话框：展示来源与目标的大小/时间对比，
/// 由用户选择替换、跳过、保留两者，或取消整个粘贴；可勾选"应用于全部"。
/// </summary>
public sealed class ConflictDialog : Window
{
    public ConflictDecision Decision { get; private set; } = ConflictDecision.KeepBoth;
    public bool ApplyToAll { get; private set; }

    public ConflictDialog(ConflictItem item, int index)
    {
        Title = "目标已存在同名项目";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var header = new TextBlock
        {
            Margin = new Thickness(14, 14, 14, 8),
            TextWrapping = TextWrapping.Wrap,
            Text = $"目标位置已存在“{Path.GetFileName(item.TargetPath)}”"
                   + (index > 0 ? $"（第 {index} 个冲突）" : string.Empty),
        };

        var grid = new Grid { Margin = new Thickness(14, 0, 14, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddCell(grid, 0, 0, string.Empty);
        AddCell(grid, 0, 1, "来源", bold: true);
        AddCell(grid, 0, 2, "现有目标", bold: true);
        AddCell(grid, 1, 0, "大小");
        AddCell(grid, 1, 1, DescribeSize(item.SourceBytes));
        AddCell(grid, 1, 2, DescribeSize(item.ExistingBytes));
        AddCell(grid, 2, 0, "修改时间");
        AddCell(grid, 2, 1, item.SourceModified.ToString("yyyy-MM-dd HH:mm"));
        AddCell(grid, 2, 2, item.ExistingModified.ToString("yyyy-MM-dd HH:mm"));

        var content = new StackPanel { Children = { header, grid } };

        // 现有目标为文件夹（ExistingBytes<0）时提示"替换=合并"语义
        if (item.ExistingBytes < 0)
        {
            content.Children.Add(new TextBlock
            {
                Margin = new Thickness(14, 4, 14, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkGoldenrod,
                Text = "⚠ 目标是文件夹：选择“替换”将把来源合并进该文件夹（复制与移动语义一致）。",
            });
        }

        var applyToAll = new CheckBox
        {
            Content = "对此后的冲突使用相同选择",
            Margin = new Thickness(14, 8, 14, 0),
        };
        content.Children.Add(applyToAll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14),
        };
        buttons.Children.Add(MakeButton("替换", ConflictDecision.Replace, applyToAll, 8, isDefault: true));
        buttons.Children.Add(MakeButton("跳过", ConflictDecision.Skip, applyToAll, 8));
        buttons.Children.Add(MakeButton("保留两者", ConflictDecision.KeepBoth, applyToAll, 8));
        buttons.Children.Add(MakeCancelButton());
        content.Children.Add(buttons);

        Content = content;
        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(this);
        };
    }

    private Button MakeButton(string label, ConflictDecision decision, CheckBox applyToAll, double rightMargin,
        bool isDefault = false)
    {
        var button = new Button
        {
            Content = label,
            Width = 84,
            Margin = new Thickness(0, 0, rightMargin, 0),
            IsDefault = isDefault,
        };
        button.Click += (_, _) =>
        {
            Decision = decision;
            ApplyToAll = applyToAll.IsChecked == true;
            DialogResult = true;
        };
        return button;
    }

    /// <summary>显式取消按钮（Esc 也可触发）：终止整个传输，已复制内容保留。</summary>
    private Button MakeCancelButton()
    {
        var button = new Button
        {
            Content = "取消",
            Width = 84,
            IsCancel = true,
        };
        button.Click += (_, _) => DialogResult = false;
        return button;
    }

    private static void AddCell(Grid grid, int row, int column, string text, bool bold = false)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 2, 8, 2),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, column);
        grid.Children.Add(textBlock);
    }

    private static string DescribeSize(long bytes) =>
        bytes < 0 ? "（文件夹）" : Models.FsEntry.FormatSize(bytes);
}

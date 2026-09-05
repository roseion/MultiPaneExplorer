using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiPaneExplorer.App.Controls;

namespace MultiPaneExplorer.App;

/// <summary>
/// 主窗口：2/3/4 窗格动态布局（可拖拽分隔条）。
/// 四个窗格实例常驻内存，切换布局只改动可见数量，各窗格的导航状态因此得以保留。
/// </summary>
public partial class MainWindow : Window
{
    private enum PaneLayout
    {
        Two,
        Three,
        Four,
    }

    private static readonly SolidColorBrush SplitterBrush = new(Color.FromRgb(0xDD, 0xDD, 0xDD));

    private readonly List<ExplorerPane> _panes = [];
    private PaneLayout _layout = PaneLayout.Two;
    private int _visiblePaneCount = 2;

    public MainWindow()
    {
        InitializeComponent();
        CreatePanes();
        ApplyLayout(PaneLayout.Two);
        Loaded += (_, _) => _panes[0].FocusList();
    }

    /// <summary>初始路径：桌面 | 文档 | 下载 | C 盘根目录；不存在的目录自动退回"此电脑"。</summary>
    private void CreatePanes()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string?[] initialPaths = [desktop, documents, downloads, @"C:\"];

        for (var i = 0; i < initialPaths.Length; i++)
        {
            _panes.Add(new ExplorerPane
            {
                InitialPath = Directory.Exists(initialPaths[i]) ? initialPaths[i] : null,
                FocusOnLoad = false,
            });
        }
    }

    private void ApplyLayout(PaneLayout layout)
    {
        _layout = layout;
        _visiblePaneCount = layout switch
        {
            PaneLayout.Two => 2,
            PaneLayout.Three => 3,
            _ => 4,
        };

        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();

        if (_visiblePaneCount <= 3)
        {
            for (var i = 0; i < _visiblePaneCount; i++)
                PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var i = 0; i < _visiblePaneCount; i++)
            {
                var column = i * 2;
                PaneGrid.Children.Add(_panes[i]);
                Grid.SetColumn(_panes[i], column);

                if (i < _visiblePaneCount - 1)
                {
                    var splitter = NewColumnSplitter();
                    PaneGrid.Children.Add(splitter);
                    Grid.SetColumn(splitter, column + 1);
                }
            }
        }
        else
        {
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Place(_panes[0], 0, 0);
            Place(_panes[1], 0, 2);
            Place(_panes[2], 2, 0);
            Place(_panes[3], 2, 2);

            // 纵向分隔条贯穿两行：拖动同时调整上下两行的列宽，保持列对齐
            var vertical = NewColumnSplitter();
            vertical.SetValue(Grid.RowProperty, 0);
            vertical.SetValue(Grid.ColumnProperty, 1);
            vertical.SetValue(Grid.RowSpanProperty, 3);
            PaneGrid.Children.Add(vertical);

            var horizontal = NewRowSplitter();
            horizontal.SetValue(Grid.RowProperty, 1);
            horizontal.SetValue(Grid.ColumnProperty, 0);
            horizontal.SetValue(Grid.ColumnSpanProperty, 3);
            PaneGrid.Children.Add(horizontal);
        }
    }

    private static GridSplitter NewColumnSplitter() => new()
    {
        Width = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = SplitterBrush,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ResizeDirection = GridResizeDirection.Columns,
    };

    private static GridSplitter NewRowSplitter() => new()
    {
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = SplitterBrush,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ResizeDirection = GridResizeDirection.Rows,
    };

    private void Place(ExplorerPane pane, int row, int column)
    {
        PaneGrid.Children.Add(pane);
        Grid.SetRow(pane, row);
        Grid.SetColumn(pane, column);
    }

    private IReadOnlyList<ExplorerPane> VisiblePanes() => _panes.Take(_visiblePaneCount).ToList();

    /// <summary>F6：键盘焦点在可见窗格之间循环切换。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.F6)
            return;

        var visible = VisiblePanes();
        if (visible.Count == 0)
            return;

        var currentIndex = -1;
        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].IsKeyboardFocusWithin)
            {
                currentIndex = i;
                break;
            }
        }

        var next = visible[(currentIndex + 1) % visible.Count];
        next.FocusList();
        e.Handled = true;
    }

    private void Layout_Checked(object sender, RoutedEventArgs e)
    {
        // XAML 解析期间 Checked 会先触发一次，此时窗格尚未就绪，跳过
        if (PaneGrid is null || _panes.Count == 0)
            return;

        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out PaneLayout layout))
            ApplyLayout(layout);
    }

    private void HiddenFiles_Changed(object sender, RoutedEventArgs e)
    {
        var show = HiddenFilesToggle?.IsChecked == true;
        foreach (var pane in _panes)
            pane.Vm.ShowHiddenFiles = show;
    }
}

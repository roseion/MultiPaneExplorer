using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileOps.Core;
using MultiPaneExplorer.App.Controls;

namespace MultiPaneExplorer.App;

/// <summary>
/// 主窗口：2/3/4 窗格动态布局（可拖拽分隔条）、多标签页、收藏夹与会话恢复。
/// 四个窗格实例常驻内存，切换布局只改动可见数量，各窗格的导航状态因此得以保留。
/// </summary>
public partial class MainWindow : Window
{
    private enum PaneLayout
    {
        Two,
        Three,
        FourGrid,
        FourColumns,
    }

    private static readonly SolidColorBrush SplitterBrush = new(Color.FromRgb(0xDD, 0xDD, 0xDD));

    private readonly List<ExplorerPane> _panes = [];
    private PaneLayout _layout = PaneLayout.Two;
    private int _visiblePaneCount = 2;
    private ExplorerPane? _lastFocusedPane;

    public MainWindow()
    {
        InitializeComponent();
        CreatePanes();
        ApplySessionOrDefault();
        ApplyLayout(_layout);
        Loaded += (_, _) => _lastFocusedPane?.FocusList();
        PaneGrid.GotKeyboardFocus += (_, _) =>
        {
            var focused = VisiblePanes().FirstOrDefault(pane => pane.IsKeyboardFocusWithin);
            if (focused is not null)
                _lastFocusedPane = focused;
        };
        Closing += (_, _) => SaveSession();
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

    /// <summary>启动时恢复上次会话；无会话文件时保持默认（双栏 + 初始路径）。</summary>
    private void ApplySessionOrDefault()
    {
        var session = SessionStore.TryLoad();
        if (session is null)
        {
            _lastFocusedPane = _panes[0];
            return;
        }

        if (Enum.TryParse<PaneLayout>(session.Layout, out var layout))
        {
            _layout = layout;
            var radio = layout switch
            {
                PaneLayout.Three => LayoutThreeButton,
                PaneLayout.FourGrid => LayoutFourGridButton,
                PaneLayout.FourColumns => LayoutFourColumnsButton,
                _ => LayoutTwoButton,
            };
            radio.IsChecked = true; // 触发 Layout_Checked → ApplyLayout
            _layout = layout;       // Layout_Checked 内部已按 Tag 解析，这里保持一致
        }

        for (var i = 0; i < _panes.Count && i < session.Panes.Count; i++)
            _panes[i].RestoreState(session.Panes[i]);

        HiddenFilesToggle.IsChecked = session.ShowHiddenFiles; // 在标签恢复后下发全局设置

        _lastFocusedPane = _panes[0];
    }

    private void SaveSession()
    {
        try
        {
            SessionStore.Save(new SessionState
            {
                Layout = _layout.ToString(),
                ShowHiddenFiles = HiddenFilesToggle.IsChecked == true,
                Panes = _panes.Select(pane => pane.CaptureState()).ToList(),
            });
        }
        catch
        {
            // 会话保存失败不影响退出
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

        if (layout != PaneLayout.FourGrid)
        {
            // 2/3/4 窗格并排：n 个窗格需要 2n-1 列——窗格占偶数列（等分剩余空间），分隔条占奇数列（Auto，固定 6px）
            for (var i = 0; i < _visiblePaneCount * 2 - 1; i++)
            {
                PaneGrid.ColumnDefinitions.Add(i % 2 == 0
                    ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    : new ColumnDefinition { Width = GridLength.Auto });
            }

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
            pane.ForEachTab(vm => vm.ShowHiddenFiles = show);
    }

    /// <summary>收藏夹菜单：条目跳转到最近聚焦的窗格；末项把该窗格当前目录加入收藏。</summary>
    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        var target = _lastFocusedPane ?? _panes[0];
        var menu = new ContextMenu();

        foreach (var folder in FavoritesStore.Load())
        {
            var item = new MenuItem { Header = folder };
            item.Click += (_, _) => target.Vm.NavigateTo(folder);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        var addCurrent = new MenuItem { Header = "把当前文件夹加入收藏" };
        addCurrent.Click += (_, _) =>
        {
            var path = target.Vm.CurrentPath;
            if (path is null)
                return;
            var favorites = FavoritesStore.Load();
            if (!favorites.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                favorites.Add(path);
                FavoritesStore.Save(favorites);
            }
        };
        menu.Items.Add(addCurrent);

        menu.PlacementTarget = FavoritesButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // 在 Click 处理器里同步打开会被随后的鼠标事件立即关闭，异步打开规避此问题
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => menu.IsOpen = true));
    }
}

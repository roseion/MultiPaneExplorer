using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using FileOps.Core;
using MultiPaneExplorer.App.Controls;
using MultiPaneExplorer.App.ViewModels;

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

    private static readonly SolidColorBrush SplitterBrush = new(Color.FromRgb(0xE5, 0xE5, 0xE5));

    private readonly List<ExplorerPane> _panes = [];
    private PaneLayout _layout = PaneLayout.Two;
    private int _visiblePaneCount = 2;
    private ExplorerPane? _lastFocusedPane;
    private double _uiScale = 1.0;

    /// <summary>合并标题栏的基准高度（XAML 中 CaptionBar 的 Height，未缩放值）。</summary>
    private const double CaptionBarBaseHeight = 36;

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
            {
                _lastFocusedPane = focused;
                UpdatePreview();
            }
        };
        UndoHub.Service.Changed += RefreshUndoButtons;
        foreach (var pane in _panes)
            pane.SelectionChanged += UpdatePreview; // 预览跟随任意窗格的选中变化
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

        // 列布局在窗格 Loaded 前种入，全局一份
        if (session.ColumnWidths is not null || session.HiddenColumns is not null)
            Models.ColumnLayoutStore.Seed(session.ColumnWidths ?? [], session.HiddenColumns ?? []);

        HiddenFilesToggle.IsChecked = session.ShowHiddenFiles; // 在标签恢复后下发全局设置

        if (session.UiScale > 0)
            _uiScale = Math.Clamp(session.UiScale, 0.8, 2.0);
        ApplyZoom();

        _lastFocusedPane = _panes[0];

        // 预览栏：先恢复宽度，再按记忆的开关状态显示（_lastFocusedPane 已就位，可立即填充内容）
        if (session.PreviewWidth >= PreviewBorder.MinWidth)
            PreviewBorder.Width = session.PreviewWidth;
        PreviewToggle.IsChecked = session.ShowPreview;
    }

    private void SaveSession()
    {
        try
        {
            SessionStore.Save(new SessionState
            {
                Layout = _layout.ToString(),
                ShowHiddenFiles = HiddenFilesToggle.IsChecked == true,
                UiScale = _uiScale,
                ColumnWidths = new Dictionary<string, double>(Models.ColumnLayoutStore.Widths),
                HiddenColumns = [.. Models.ColumnLayoutStore.Hidden],
                ShowPreview = PreviewToggle.IsChecked == true,
                PreviewWidth = double.IsNaN(PreviewBorder.Width) || PreviewBorder.Width <= 0
                    ? 260
                    : PreviewBorder.Width,
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

    /// <summary>F6：键盘焦点在可见窗格之间循环切换；Ctrl+=/-/0：界面整体缩放；Ctrl+Z/Y：全局撤销/重做（文本框内保留原生编辑）。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+= / Ctrl+- / Ctrl+0：整体缩放（允许 Ctrl+Shift+= 产生的 + 号）
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (Keyboard.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) == 0)
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    SetZoom(_uiScale + 0.1);
                    e.Handled = true;
                    return;
                case Key.OemMinus or Key.Subtract:
                    SetZoom(_uiScale - 0.1);
                    e.Handled = true;
                    return;
                case Key.D0 or Key.NumPad0:
                    SetZoom(1.0);
                    e.Handled = true;
                    return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.OriginalSource is not TextBoxBase)
        {
            switch (e.Key)
            {
                case Key.Z:
                    _ = RunUndoRedoAsync(undo: true);
                    e.Handled = true;
                    return;
                case Key.Y:
                    _ = RunUndoRedoAsync(undo: false);
                    e.Handled = true;
                    return;
            }
        }

        // Alt+P：预览窗格开关
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.P)
        {
            PreviewToggle.IsChecked = PreviewToggle.IsChecked != true;
            e.Handled = true;
            return;
        }

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

    // ---- 预览窗格：跟随最近聚焦窗格的选中项；取消选中保留上次内容 ----

    private void PreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PreviewBorder is null || Preview is null)
            return;
        var show = PreviewToggle.IsChecked == true;
        PreviewBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewToggle.IsChecked != true)
            return;
        var pane = _lastFocusedPane ?? _panes[0];
        if (pane.Vm.SelectedEntries.LastOrDefault() is not { } entry)
            return; // 无选中：保留上次预览内容（与资源管理器一致）
        Preview.Show(entry);
    }

    // ---- 界面整体缩放：对根面板做 LayoutTransform，文字/图标/边距等比放大，随会话记忆 ----

    private void SetZoom(double scale)
    {
        _uiScale = Math.Clamp(Math.Round(scale, 2), 0.8, 2.0);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        RootPanel.LayoutTransform = new ScaleTransform(_uiScale, _uiScale);
        // 缩放会改变工具栏的视觉高度，标题栏拖拽命中区必须同步，否则错位后下半截变成拖拽区
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
            chrome.CaptionHeight = CaptionBarBaseHeight * _uiScale;
    }

    // ---- 合并标题栏：自绘窗口按钮、最大化越界补边、最大化/还原图标切换 ----

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // 无边框窗口最大化时会向四周越出一个边框宽度，补边距防止内容被裁/盖住任务栏
        var chrome = WindowChrome.GetWindowChrome(this);
        var overhang = chrome?.ResizeBorderThickness ?? new Thickness(6);
        RootPanel.Margin = WindowState == WindowState.Maximized ? overhang : default(Thickness);

        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "向下还原" : "最大化";
    }

    // ---- 撤销/重做：执行后刷新全部可见窗格，状态显示在最近聚焦的窗格 ----

    private async void UndoButton_Click(object sender, RoutedEventArgs e) => await RunUndoRedoAsync(undo: true);

    private async void RedoButton_Click(object sender, RoutedEventArgs e) => await RunUndoRedoAsync(undo: false);

    private async Task RunUndoRedoAsync(bool undo)
    {
        var service = UndoHub.Service;
        var target = _lastFocusedPane ?? _panes[0];
        try
        {
            var description = undo ? await service.UndoAsync() : await service.RedoAsync();
            foreach (var pane in VisiblePanes())
                pane.ForEachTab(vm => vm.RefreshCommand.Execute(null));
            target.Vm.ShowTransientStatus((undo ? "已撤销：" : "已重做：") + description);
        }
        catch (Exception ex)
        {
            target.Vm.ShowTransientStatus((undo ? "撤销" : "重做") + $"失败：{ex.Message}");
        }
        RefreshUndoButtons();
    }

    private void RefreshUndoButtons()
    {
        // Changed 事件理论上在 UI 线程触发（服务端已保证），此处封送兜底防回归
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RefreshUndoButtons);
            return;
        }

        UndoButton.IsEnabled = UndoHub.Service.CanUndo;
        UndoButton.ToolTip = UndoHub.Service.UndoDescription is { } undoDescription
            ? $"撤销：{undoDescription} (Ctrl+Z)"
            : "无可撤销操作 (Ctrl+Z)";
        RedoButton.IsEnabled = UndoHub.Service.CanRedo;
        RedoButton.ToolTip = UndoHub.Service.RedoDescription is { } redoDescription
            ? $"重做：{redoDescription} (Ctrl+Y)"
            : "无可重做操作 (Ctrl+Y)";
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

    /// <summary>收藏夹菜单：条目跳转到最近聚焦的窗格（带 ✕ 移除）、"常用"分组（自动计数 Top5）、加入收藏。</summary>
    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        var target = _lastFocusedPane ?? _panes[0];
        var menu = new ContextMenu();

        var favorites = FavoritesStore.Load();
        foreach (var folder in favorites)
            menu.Items.Add(BuildFavoriteMenuItem(target, folder, removable: true));

        var frequent = FileOps.Core.FrequentStore.Top(5)
            .Where(folder => Directory.Exists(folder)
                && !favorites.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (frequent.Count > 0)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "常用", IsEnabled = false });
            foreach (var folder in frequent)
                menu.Items.Add(BuildFavoriteMenuItem(target, folder, removable: false));
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        var addCurrent = new MenuItem { Header = "把当前文件夹加入收藏" };
        addCurrent.Click += (_, _) =>
        {
            var path = target.Vm.CurrentPath;
            if (path is null)
                return;
            var current = FavoritesStore.Load();
            if (!current.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                current.Add(path);
                FavoritesStore.Save(current);
            }
        };
        menu.Items.Add(addCurrent);

        menu.PlacementTarget = FavoritesButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // 在 Click 处理器里同步打开会被随后的鼠标事件立即关闭，异步打开规避此问题
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => menu.IsOpen = true));
    }

    /// <summary>收藏菜单条目：名称跳转，可移除条目右缘带 ✕ 按钮。</summary>
    private static MenuItem BuildFavoriteMenuItem(ExplorerPane target, string folder, bool removable)
    {
        var item = new MenuItem();
        var panel = new Grid { Width = 230 };
        panel.Children.Add(new TextBlock
        {
            Text = folder,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (removable)
        {
            var remove = new Button
            {
                Content = "✕",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 10,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = "从收藏中移除",
            };
            remove.Click += (_, removeEvent) =>
            {
                removeEvent.Handled = true; // 不触发条目跳转
                var current = FavoritesStore.Load();
                current.RemoveAll(favorite => string.Equals(favorite, folder, StringComparison.OrdinalIgnoreCase));
                FavoritesStore.Save(current);
                item.IsEnabled = false;
                item.Header = folder + "（已移除）";
            };
            panel.Children.Add(remove);
        }
        item.Header = panel;
        item.Click += (_, _) => target.Vm.NavigateTo(folder);
        return item;
    }
}

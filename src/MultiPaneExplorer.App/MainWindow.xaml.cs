using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using FileOps.Core;
using MultiPaneExplorer.App.Controls;
using MultiPaneExplorer.App.Models;
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

    /// <summary>主题分隔条画刷（资源未就绪时退回同值浅色，保证可用）。</summary>
    private static Brush SplitterBrush =>
        System.Windows.Application.Current?.TryFindResource("W11.Splitter") as Brush
        ?? new SolidColorBrush(Color.FromRgb(0xE7, 0xEB, 0xF1));

    private readonly List<ExplorerPane> _panes = [];
    private PaneLayout _layout = PaneLayout.Two;
    private int _visiblePaneCount = 2;
    private ExplorerPane? _lastFocusedPane;
    private double _uiScale = 1.0;

    /// <summary>合并标题栏的基准高度（XAML 中 CaptionBar 的 Height，未缩放值）。</summary>
    private const double CaptionBarBaseHeight = 40;

    private bool _showHidden;
    private bool _showPreview;
    private bool _galleryEnabled = true;
    private double _previewWidth = 260;

    public MainWindow()
    {
        InitializeComponent();
        CreatePanes();
        ApplySessionOrDefault();
        // 窗底渐变（Air 感）：DynamicResource 无法可靠驱动 GradientStop 换肤，改由代码按主题重建
        ThemeManager.ThemeChanged += ApplyBackdrop;
        ApplyBackdrop();
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
            _layout = layout; // ctor 末尾按此值 ApplyLayout
        }

        for (var i = 0; i < _panes.Count && i < session.Panes.Count; i++)
            _panes[i].RestoreState(session.Panes[i]);

        // 列布局在窗格 Loaded 前种入，全局一份
        if (session.ColumnWidths is not null || session.HiddenColumns is not null)
            Models.ColumnLayoutStore.Seed(session.ColumnWidths ?? [], session.HiddenColumns ?? []);

        if (session.UiScale > 0)
            _uiScale = Math.Clamp(session.UiScale, 0.8, 2.0);
        ApplyZoom();

        _lastFocusedPane = _panes[0];

        // 主题：应用会话设置（Light/Dark/System）与强调色，跟随系统时挂 WM_SETTINGCHANGE 钩子
        ThemeManager.ApplySelected(string.IsNullOrEmpty(session.Theme)
            ? Models.ThemeManager.System
            : session.Theme);
        ThemeManager.ApplyAccent(session.AccentIndex);

        _galleryEnabled = session.GalleryEnabled;

        // 全局开关状态字段化（原标题栏控件的值迁入"查看"菜单）
        _showHidden = session.ShowHiddenFiles;
        if (_showHidden)
            foreach (var pane in _panes)
                pane.ForEachTab(vm => vm.ShowHiddenFiles = true);

        if (session.PreviewWidth >= 180)
            _previewWidth = session.PreviewWidth;
        PreviewBorder.Width = _previewWidth;
        _showPreview = session.ShowPreview;
        ApplyPreviewVisibility();
    }

    private void SaveSession()
    {
        try
        {
            SessionStore.Save(new SessionState
            {
                Layout = _layout.ToString(),
                ShowHiddenFiles = _showHidden,
                UiScale = _uiScale,
                ColumnWidths = new Dictionary<string, double>(Models.ColumnLayoutStore.Widths),
                HiddenColumns = [.. Models.ColumnLayoutStore.Hidden],
                ShowPreview = _showPreview,
                PreviewWidth = double.IsNaN(PreviewBorder.Width) || PreviewBorder.Width <= 0
                    ? 260
                    : PreviewBorder.Width,
                Theme = ThemeManager.SelectedTheme,
                AccentIndex = ThemeManager.SelectedAccentIndex,
                GalleryEnabled = _galleryEnabled,
                Panes = _panes.Select(pane => pane.CaptureState()).ToList(),
            });
        }
        catch
        {
            // 会话保存失败不影响退出
        }
    }

    /// <summary>全局搜索：回车在最近聚焦窗格递归搜索，Esc 清除。</summary>
    private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            GlobalSearchBox.Clear();
            var pane0 = _lastFocusedPane ?? _panes[0];
            pane0.Vm.SearchSubdirectories = false;
            pane0.Vm.FilterText = "";
            e.Handled = true;
            return;
        }
        if (e.Key is not Key.Enter)
            return;

        var text = GlobalSearchBox.Text.Trim();
        var pane = _lastFocusedPane ?? _panes[0];
        if (text.Length == 0)
        {
            pane.Vm.SearchSubdirectories = false;
            pane.Vm.FilterText = "";
        }
        else
        {
            pane.Vm.SearchSubdirectories = true; // 先开递归（FilterText 为空时不会触发搜索）
            pane.Vm.FilterText = text;           // 再赋关键字，触发递归搜索
        }
        e.Handled = true;
    }

    private void ApplyBackdrop() =>
        RootPanel.Background = ThemeManager.BuildBackdropBrush();

    /// <summary>画廊条显隐联动：仅双栏布局 + 全局开关开启时允许。</summary>
    private void RefreshGalleryAllowed()
    {
        var allow = _galleryEnabled && _layout == PaneLayout.Two;
        foreach (var pane in _panes)
            pane.SetGalleryAllowed(allow);
    }

    // ---- 供设置窗口调用的公开入口 ----

    public void SetShowHiddenFilesAll(bool show)
    {
        _showHidden = show;
        ApplyHiddenFiles();
    }

    public void SetPreviewVisible(bool show)
    {
        _showPreview = show;
        ApplyPreviewVisibility();
    }

    public void SetGalleryEnabled(bool enabled)
    {
        _galleryEnabled = enabled;
        RefreshGalleryAllowed();
    }

    public void ApplyLayoutByTag(string tag)
    {
        if (Enum.TryParse<PaneLayout>(tag, out var layout) && _layout != layout)
            ApplyLayout(layout);
    }

    public string CurrentLayoutTag => _layout.ToString();
    public bool ShowHidden => _showHidden;
    public bool ShowPreview => _showPreview;
    public bool GalleryEnabled => _galleryEnabled;


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
        RefreshGalleryAllowed();
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
                case Key.N:
                    new MainWindow().Show(); // 进程内新窗口（与资源管理器 Ctrl+N 一致）
                    e.Handled = true;
                    return;
            }
        }

        // Alt+P：预览窗格开关
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.P)
        {
            _showPreview = !_showPreview;
            ApplyPreviewVisibility();
            e.Handled = true;
            return;
        }

        // Ctrl+Alt+T：深浅主题一键切换（写会话记忆）
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key is Key.T)
        {
            var target = ThemeManager.CurrentEffective == ThemeManager.Dark
                ? ThemeManager.Light
                : ThemeManager.Dark;
            ThemeManager.ApplySelected(target);
            SaveSessionTheme();
            if (_lastFocusedPane is { } pane)
                pane.Vm.ShowTransientStatus($"已切换到{(target == ThemeManager.Dark ? "深色" : "浅色")}主题");
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

    private void ApplyPreviewVisibility()
    {
        if (PreviewBorder is null || Preview is null)
            return;
        PreviewBorder.Visibility = _showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = _showPreview ? Visibility.Visible : Visibility.Collapsed;
        if (_showPreview)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_showPreview)
            return;
        var pane = _lastFocusedPane ?? _panes[0];
        if (pane.Vm.SelectedEntries.LastOrDefault() is not { } entry)
            return; // 无选中：保留上次预览内容（与资源管理器一致）
        Preview.Show(entry);
    }

    // ---- "查看"菜单：布局切换、显示隐藏文件、预览窗格（替代原标题栏控件区） ----

    private void ViewMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        foreach (var (layout, title) in new[]
                 {
                     (PaneLayout.Two, "双栏"),
                     (PaneLayout.Three, "三栏"),
                     (PaneLayout.FourGrid, "四栏（田字）"),
                     (PaneLayout.FourColumns, "四栏（并排）"),
                 })
        {
            var item = new MenuItem
            {
                Header = title,
                IsChecked = _layout == layout,
                IsCheckable = true,
            };
            var target = layout;
            item.Click += (_, _) =>
            {
                if (_layout != target)
                    ApplyLayout(target);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var hiddenItem = new MenuItem
        {
            Header = "显示隐藏文件",
            IsCheckable = true,
            IsChecked = _showHidden,
        };
        hiddenItem.Click += (_, _) =>
        {
            _showHidden = hiddenItem.IsChecked;
            ApplyHiddenFiles();
        };
        menu.Items.Add(hiddenItem);

        var previewItem = new MenuItem
        {
            Header = "预览窗格 (Alt+P)",
            IsCheckable = true,
            IsChecked = _showPreview,
        };
        previewItem.Click += (_, _) =>
        {
            _showPreview = previewItem.IsChecked;
            ApplyPreviewVisibility();
        };
        menu.Items.Add(previewItem);

        var galleryItem = new MenuItem
        {
            Header = "缩略图画廊（双栏时显示）",
            IsCheckable = true,
            IsChecked = _galleryEnabled,
        };
        galleryItem.Click += (_, _) =>
        {
            _galleryEnabled = galleryItem.IsChecked;
            RefreshGalleryAllowed();
        };
        menu.Items.Add(galleryItem);

        var settingsItem = new MenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => new Views.SettingsWindow().Show();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // 外观：主题三态（即时切换，随会话记忆）
        var appearanceItem = new MenuItem { Header = "外观" };
        foreach (var (value, title) in new[]
                 {
                     (Models.ThemeManager.Light, "浅色"),
                     (Models.ThemeManager.Dark, "深色"),
                     (Models.ThemeManager.System, "跟随系统"),
                 })
        {
            var themeItem = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = ThemeManager.SelectedTheme == value,
            };
            var target = value;
            themeItem.Click += (_, _) =>
            {
                ThemeManager.ApplySelected(target);
                SaveSessionTheme();
            };
            appearanceItem.Items.Add(themeItem);
        }
        menu.Items.Add(appearanceItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = $"多栏资源管理器 v{GetVersion()}",
            IsEnabled = false,
        });

        menu.PlacementTarget = ViewMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // 在 Click 处理器里同步打开会被随后的鼠标事件立即关闭，异步打开规避此问题
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => menu.IsOpen = true));
    }

    private void ApplyHiddenFiles()
    {
        foreach (var pane in _panes)
            pane.ForEachTab(vm => vm.ShowHiddenFiles = _showHidden);
    }

    /// <summary>主题选择即时写入会话文件（不等退出）。</summary>
    /// <summary>供设置窗口即时保存会话。</summary>
    public void SaveSessionSnapshot() => SaveSession();

    private void SaveSessionTheme()
    {
        try
        {
            var session = SessionStore.TryLoad() ?? new SessionState();
            session.Theme = ThemeManager.SelectedTheme;
            session.Layout = _layout.ToString();
            session.ShowHiddenFiles = _showHidden;
            session.UiScale = _uiScale;
            session.ColumnWidths = new Dictionary<string, double>(Models.ColumnLayoutStore.Widths);
            session.HiddenColumns = [.. Models.ColumnLayoutStore.Hidden];
            session.ShowPreview = _showPreview;
            session.PreviewWidth = double.IsNaN(PreviewBorder.Width) || PreviewBorder.Width <= 0
                ? 260
                : PreviewBorder.Width;
            session.Panes = _panes.Select(pane => pane.CaptureState()).ToList();
            SessionStore.Save(session);
        }
        catch
        {
            // 主题记忆失败不影响切换
        }
    }

    private static string GetVersion()
    {
        try
        {
            var assembly = typeof(MainWindow).Assembly;
            return assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString(3)
                   ?? "1.9.0";
        }
        catch
        {
            return "1.9.0";
        }
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

    // ---- 标题栏原生交互：右键系统菜单 + 最大化钮悬停贴靠布局弹窗 ----

    /// <summary>标题栏空白处右键：弹出系统菜单（移动/大小/最小化/最大化/关闭），坐标转到屏幕系。</summary>
    private void CaptionBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Control)
            return; // 命令按钮上不弹系统菜单
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));
    }

    private System.Windows.Threading.DispatcherTimer? _snapShowTimer;

    private void MaximizeButton_MouseEnter(object sender, MouseEventArgs e)
    {
        _snapShowTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _snapShowTimer.Tick += (_, _) =>
        {
            _snapShowTimer.Stop();
            SnapPopup.IsOpen = true;
        };
        _snapShowTimer.Stop();
        _snapShowTimer.Start();
    }

    private void MaximizeButton_MouseLeave(object sender, MouseEventArgs e) => _snapShowTimer?.Stop();

    private void SnapPopup_MouseLeave(object sender, MouseEventArgs e) => SnapPopup.IsOpen = false;

    private void SnapZone_Click(object sender, MouseButtonEventArgs e)
    {
        SnapPopup.IsOpen = false;
        if (sender is Border { Tag: string zone })
            SnapTo(zone);
    }

    /// <summary>把窗口贴靠到当前显示器工作区的指定分区。</summary>
    private void SnapTo(string zone)
    {
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        if (dpi <= 0)
            dpi = 1.0;
        var work = GetWorkAreaPixels();

        double x = work.Left / dpi, y = work.Top / dpi;
        double w = (work.Right - work.Left) / dpi, h = (work.Bottom - work.Top) / dpi;
        var (rx, ry, rw, rh) = zone switch
        {
            "Right" => (x + w / 2, y, w / 2, h),
            "LeftTop" => (x, y, w / 2, h / 2),
            "RightTop" => (x + w / 2, y, w / 2, h / 2),
            "LeftBottom" => (x, y + h / 2, w / 2, h / 2),
            "RightBottom" => (x + w / 2, y + h / 2, w / 2, h / 2),
            _ => (x, y, w / 2, h), // Left
        };

        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
        Left = rx;
        Top = ry;
        Width = rw;
        Height = rh;
    }

    private RECT GetWorkAreaPixels()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return info.rcWork;
        }

        // 兜底：主屏工作区按当前 DPI 折算为像素
        var wa = SystemParameters.WorkArea;
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return new RECT { Left = 0, Top = 0, Right = (int)(wa.Right * dpi), Bottom = (int)(wa.Bottom * dpi) };
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

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
        // 保留空实现以兼容旧 XAML 引用（当前 XAML 已不再触发）
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

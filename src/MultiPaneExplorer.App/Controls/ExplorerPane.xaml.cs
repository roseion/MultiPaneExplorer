using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MultiPaneExplorer.App.Models;
using MultiPaneExplorer.App.ViewModels;

namespace MultiPaneExplorer.App.Controls;

/// <summary>
/// 一个独立的资源管理器窗格：文件树侧栏（可开关）、导航栏、地址栏、文件列表、状态栏。
/// </summary>
public partial class ExplorerPane : UserControl
{
    public static readonly DependencyProperty InitialPathProperty = DependencyProperty.Register(
        nameof(InitialPath), typeof(string), typeof(ExplorerPane), new PropertyMetadata(default(string)));

    public static readonly DependencyProperty FocusOnLoadProperty = DependencyProperty.Register(
        nameof(FocusOnLoad), typeof(bool), typeof(ExplorerPane), new PropertyMetadata(true));

    private bool _initialized;
    private bool _revealing;
    private bool _addressEditing;
    private bool _tabDragArmed;
    private int? _tabDragSourceIndex;
    private Point _tabDragStartPosition;
    private Point _dragStartPosition;
    private bool _dragArmed;
    private DragDropEffects _pendingDropEffect;
    private Point? _rubberBandStart;
    private bool _rubberBandActive;
    private bool _rubberBandCtrl;
    private HashSet<FsEntry> _rubberBandBase = [];
    private RubberBandAdorner? _rubberAdorner;
    private double _treeColumnWidth = 200;
    private readonly List<PaneViewModel> _tabs = [];
    private int _activeTabIndex;

    private static Brush DropHintBrush =>
        System.Windows.Application.Current?.TryFindResource("W11.Accent") as Brush
        ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
    private static Brush ActiveTabBrush =>
        System.Windows.Application.Current?.TryFindResource("W11.CardBackground") as Brush
        ?? Brushes.White;

    private static readonly Dictionary<string, string> SortHeaderTitles = new()
    {
        ["Name"] = "名称",
        ["Modified"] = "修改时间",
        ["Created"] = "创建时间",
        ["Type"] = "类型",
        ["Size"] = "大小",
    };

    public ExplorerPane()
    {
        InitializeComponent();
        Vm = CreateTab();
        _tabs.Add(Vm);
        DataContext = Vm;
        ColumnLayoutStore.Changed += ApplyColumnLayout; // 列布局全局共享，任一窗格变更全体同步
        _typeAheadTimer.Tick += (_, _) => ResetTypeAhead();
        Loaded += (_, _) =>
        {
            if (_initialized)
                return;
            _initialized = true;
            Vm.Initialize(InitialPath);
            UpdateSortHeaders();
            ApplyColumnLayout();
            ApplyViewMode();
            RefreshBreadcrumb();
            RefreshTabStrip();
            if (FocusOnLoad)
                EntryList.Focus();
        };
    }

    /// <summary>当前激活标签页的视图模型（导航栏/列表/文件树都作用于它），随标签切换更新。</summary>
    public PaneViewModel Vm { get; private set; }

    /// <summary>窗格首次加载时定位到的目录；为空或不存在时停留在"此电脑"。</summary>
    public string? InitialPath
    {
        get => (string?)GetValue(InitialPathProperty);
        set => SetValue(InitialPathProperty, value);
    }

    /// <summary>任意标签页的选中集合变化（预览窗格跟随用）。</summary>
    public event Action? SelectionChanged;

    /// <summary>加载完成后是否自动获得键盘焦点（多窗格时只给一个窗格）。</summary>
    public bool FocusOnLoad
    {
        get => (bool)GetValue(FocusOnLoadProperty);
        set => SetValue(FocusOnLoadProperty, value);
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab();

    /// <summary>双击标签条空白处新建标签页（标签钮自身会处理按下事件，只有空白处到达这里）。</summary>
    private void TabStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            AddTab();
    }

    private PaneViewModel CreateTab()
    {        var tab = new PaneViewModel();
        tab.CurrentPathChanged += path =>
        {
            if (ReferenceEquals(tab, Vm))
                OnCurrentPathChanged(path);
            RefreshTabStrip();
        };
        tab.PropertyChanged += (_, e) =>
        {
            if (!ReferenceEquals(tab, Vm))
                return;
            if (e.PropertyName == nameof(PaneViewModel.ViewMode))
            {
                ApplyViewMode();
                QueueThumbnailsForCurrentEntries();
            }
            else if (e.PropertyName == nameof(PaneViewModel.ShowTree))
                UpdateTreeColumnVisibility();
            else if (e.PropertyName is nameof(PaneViewModel.FilterText) or nameof(PaneViewModel.SearchSubdirectories))
                UpdateLocationColumnVisibility();
        };
        tab.EntryFocusRequested += () =>
        {
            if (ReferenceEquals(tab, Vm))
                EntryList.Focus();
        };
        tab.SelectionChanged += () => SelectionChanged?.Invoke();
        tab.Entries.CollectionChanged += (_, e) =>
        {
            if (ReferenceEquals(tab, Vm) && tab.ViewMode != "Details" && e.NewItems is not null)
                ThumbnailLoader.EnqueueRange(e.NewItems.Cast<FsEntry>());
        };
        return tab;
    }

    private void AddTab(string? initialPath = null)
    {
        var tab = CreateTab();
        tab.Initialize(initialPath ?? Vm.CurrentPath);
        _tabs.Insert(_activeTabIndex + 1, tab);
        SwitchTab(_activeTabIndex + 1);
    }

    private void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || _tabs.Count == 1)
            return;
        var closing = _tabs[index];
        _tabs.RemoveAt(index);
        closing.Shutdown();
        SwitchTab(Math.Min(index, _tabs.Count - 1));
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return;
        _activeTabIndex = index;
        Vm = _tabs[index];
        DataContext = Vm;
        UpdateSortHeaders();
        _revealing = true;
        try
        {
            Vm.RevealInTree();
        }
        finally
        {
            _revealing = false;
        }
        RefreshTabStrip();
        QueueThumbnailsForCurrentEntries();
        RefreshBreadcrumb();
        ApplyViewMode();
        UpdateTreeColumnVisibility();
        EntryList.Focus();
    }

    /// <summary>文件树显示/隐藏时联动列宽与分隔条：隐藏前记忆宽度，恢复时还原。</summary>
    private void UpdateTreeColumnVisibility()
    {
        var visible = Vm.ShowTree;
        if (visible)
        {
            TreeColumn.Width = new GridLength(_treeColumnWidth);
        }
        else
        {
            if (TreeColumn.Width.IsAbsolute && TreeColumn.Width.Value >= TreeColumn.MinWidth)
                _treeColumnWidth = TreeColumn.Width.Value;
            TreeColumn.Width = new GridLength(0);
        }
        TreeSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>非详细信息视图下，为当前列表条目补齐大图标/缩略图。</summary>
    private void QueueThumbnailsForCurrentEntries()
    {
        if (Vm.ViewMode != "Details")
            ThumbnailLoader.EnqueueRange(Vm.Entries);
    }

    private static string TitleOf(PaneViewModel tab)
    {
        if (tab.CurrentPath is null)
            return "此电脑";
        if (tab.CurrentPath == SpecialLocations.RecycleBin)
            return "回收站";
        var name = Path.GetFileName(tab.CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? tab.CurrentPath : name;
    }

    private void RefreshTabStrip()
    {
        TabStrip.Children.Clear();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var index = i;
            var closeButton = new Button
            {
                Content = "✕",
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                FontSize = 9,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Margin = new Thickness(6, 0, 0, 0),
            };
            closeButton.Click += (_, e) =>
            {
                e.Handled = true;
                CloseTab(index);
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = TitleOf(_tabs[index]),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(closeButton);

            var tabButton = new Button
            {
                Content = content,
                Margin = new Thickness(0, 2, 3, 0),
                Background = i == _activeTabIndex ? ActiveTabBrush : Brushes.Transparent,
                // 卡片形标签（顶部圆角）：激活 = 卡片白底，非激活透明
                Style = System.Windows.Application.Current?.TryFindResource("W11.TabButton") as Style,
            };
            tabButton.Click += (_, _) => SwitchTab(index);
            tabButton.MouseUp += (_, mouse) =>
            {
                if (mouse.ChangedButton == MouseButton.Middle)
                {
                    CloseTab(index); // 中键关闭（与浏览器/资源管理器一致）
                    mouse.Handled = true;
                }
            };

            // 拖拽重排：先武装，MouseMove 超过阈值才启动 DragDrop（避免吞掉普通点击）
            tabButton.AllowDrop = true;
            tabButton.PreviewMouseLeftButtonDown += (_, down) =>
            {
                _tabDragArmed = true;
                _tabDragStartPosition = down.GetPosition(tabButton);
            };
            tabButton.PreviewMouseMove += (_, move) =>
            {
                if (!_tabDragArmed || move.LeftButton != MouseButtonState.Pressed)
                {
                    _tabDragArmed = false;
                    return;
                }
                var position = move.GetPosition(tabButton);
                if (Math.Abs(position.X - _tabDragStartPosition.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(position.Y - _tabDragStartPosition.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }
                _tabDragArmed = false;
                _tabDragSourceIndex = index;
                var data = new DataObject("pane-tab-index", index);
                _ = DragDrop.DoDragDrop(tabButton, data, DragDropEffects.Move);
            };
            tabButton.DragOver += (_, over) =>
            {
                over.Effects = _tabDragSourceIndex is { } source && source != index
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
                over.Handled = true;
            };
            tabButton.Drop += (_, drop) =>
            {
                if (_tabDragSourceIndex is { } source)
                    MoveTab(source, index);
                _tabDragSourceIndex = null;
                drop.Handled = true;
            };
            TabStrip.Children.Add(tabButton);
        }
    }

    private void MoveTab(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _tabs.Count || to >= _tabs.Count)
            return;
        var tab = _tabs[from];
        _tabs.RemoveAt(from);
        _tabs.Insert(to, tab);
        if (_activeTabIndex == from)
            _activeTabIndex = to;
        else if (from < _activeTabIndex && to >= _activeTabIndex)
            _activeTabIndex--;
        else if (from > _activeTabIndex && to <= _activeTabIndex)
            _activeTabIndex++;
        RefreshTabStrip();
    }

    /// <summary>对所有标签页执行同一操作（如全局设置下发）。</summary>
    public void ForEachTab(Action<PaneViewModel> action)
    {
        foreach (var tab in _tabs)
            action(tab);
    }

    /// <summary>捕获当前窗格状态（用于会话保存）。</summary>
    public FileOps.Core.PaneState CaptureState() => new()
    {
        ShowTree = Vm.ShowTree,
        TreeWidth = _treeColumnWidth,
        ActiveTabIndex = _activeTabIndex,
        Tabs = _tabs.Select(tab => new FileOps.Core.PaneTabState { Path = tab.CurrentPath, ViewMode = tab.ViewMode }).ToList(),
    };

    /// <summary>按会话状态恢复标签页（布局与全局设置由主窗口负责）。</summary>
    public void RestoreState(FileOps.Core.PaneState state)
    {
        foreach (var tab in _tabs)
            tab.Shutdown();
        _tabs.Clear();

        foreach (var tabState in state.Tabs)
        {
            var tab = CreateTab();
            tab.ShowTree = state.ShowTree;
            tab.ViewMode = string.IsNullOrEmpty(tabState.ViewMode) ? "Details" : tabState.ViewMode!;
            tab.Initialize(tabState.Path);
            _tabs.Add(tab);
        }

        if (_tabs.Count == 0)
        {
            var tab = CreateTab();
            tab.ShowTree = state.ShowTree;
            tab.Initialize(null);
            _tabs.Add(tab);
        }

        if (state.TreeWidth > 0)
            _treeColumnWidth = Math.Clamp(state.TreeWidth, 120, 520);

        SwitchTab(Math.Clamp(state.ActiveTabIndex, 0, _tabs.Count - 1));
    }

    public void FocusList() => EntryList.Focus();

    /// <summary>窗格目录变化时（含列表/地址栏/前进后退），让文件树跟随定位并刷新列头文案。</summary>
    private void OnCurrentPathChanged(string? path)
    {
        _addressEditing = false;
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbBar.Visibility = Visibility.Visible;
        RefreshBreadcrumb();
        UpdateSortHeaders(); // 回收站视图下"修改时间"列头切换为"删除时间"
        ApplyViewMode();     // "此电脑"页切换驱动器宽卡

        if (path is null)
            return;
        _revealing = true;
        try
        {
            Vm.RevealInTree();
        }
        finally
        {
            _revealing = false;
        }
    }

    /// <summary>
    /// 统一裁决列表呈现：此电脑页 = 驱动器宽卡（Explorer"设备和驱动器"式）；
    /// 其余按 ViewMode（详细信息/大图标/列表）。代码后置切换，避免本地值与样式触发器的优先级纠缠。
    /// </summary>
    private void ApplyViewMode()
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(EntryList, ScrollBarVisibility.Auto);

        if (Vm.CurrentPath is null)
        {
            EntryList.View = null;
            EntryList.ItemTemplate = (DataTemplate)FindResource("DriveCardTemplate");
            EntryList.ItemsPanel = (ItemsPanelTemplate)FindResource("DriveCardsPanel");
            ScrollViewer.SetHorizontalScrollBarVisibility(EntryList, ScrollBarVisibility.Disabled);
            return;
        }

        EntryList.ItemTemplate = null;
        EntryList.ItemsPanel = (ItemsPanelTemplate)FindResource("DetailsItemsPanel");
        switch (Vm.ViewMode)
        {
            case "LargeIcons":
                EntryList.View = null;
                EntryList.ItemTemplate = (DataTemplate)FindResource("LargeIconTemplate");
                EntryList.ItemsPanel = (ItemsPanelTemplate)FindResource("LargeIconsPanel");
                // 禁横向滚动条：让 WrapPanel 按可视宽度换行，否则内容宽无限增长永远不换行
                ScrollViewer.SetHorizontalScrollBarVisibility(EntryList, ScrollBarVisibility.Disabled);
                break;
            case "List":
                EntryList.View = null;
                EntryList.ItemTemplate = (DataTemplate)FindResource("ListTemplate");
                EntryList.ItemsPanel = (ItemsPanelTemplate)FindResource("ListPanel");
                ScrollViewer.SetHorizontalScrollBarVisibility(EntryList, ScrollBarVisibility.Disabled);
                break;
            default:
                EntryList.View = EntryView; // x:Name 的 GridView（详细信息）
                break;
        }
    }

    private void DirTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_revealing)
            return;
        if (e.NewValue is FsTreeNode { IsDummy: false } node)
            Vm.NavigateTo(node.FullPath);
    }

    private void DirTree_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: FsTreeNode node })
            node.LoadChildren();
    }

    /// <summary>文件树节点右键：固定到收藏/取消收藏、在新标签页打开。</summary>
    private void DirTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(DirTree, source) is not TreeViewItem
            {
                DataContext: FsTreeNode { IsDummy: false } node,
            }
            || node.FullPath == SpecialLocations.RecycleBin
            || !Directory.Exists(node.FullPath))
        {
            return;
        }

        e.Handled = true;
        var favorites = FileOps.Core.FavoritesStore.Load();
        var pinned = favorites.Contains(node.FullPath, StringComparer.OrdinalIgnoreCase);

        var menu = new ContextMenu();
        var pinItem = new MenuItem { Header = pinned ? "从收藏中移除" : "固定到收藏" };
        pinItem.Click += (_, _) =>
        {
            var current = FileOps.Core.FavoritesStore.Load();
            if (pinned)
                current.RemoveAll(item => string.Equals(item, node.FullPath, StringComparison.OrdinalIgnoreCase));
            else if (!current.Contains(node.FullPath, StringComparer.OrdinalIgnoreCase))
                current.Add(node.FullPath);
            FileOps.Core.FavoritesStore.Save(current);
        };
        menu.Items.Add(pinItem);

        var newTabItem = new MenuItem { Header = "在新标签页打开" };
        newTabItem.Click += (_, _) => AddTab(node.FullPath);
        menu.Items.Add(newTabItem);

        menu.PlacementTarget = DirTree;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        // 右键事件处理过程中同步打开会被随后的鼠标事件立即关闭（同面包屑下拉坑），异步打开规避
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => menu.IsOpen = true));
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter)
            return;
        AddressSuggestPopup.IsOpen = false;
        if (Vm.NavigateToAddress(Vm.PathText))
        {
            // 导航成功才退出编辑态；路径不存在时留在输入框便于修改
            ExitAddressEditMode(revert: false, refocusList: true);
        }
        e.Handled = true;
    }

    // ---- 面包屑地址栏：常态显示层级段，点击空白进入编辑态 ----

    private void CrumbBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterAddressEditMode();

    private void EnterAddressEditMode()
    {
        if (_addressEditing)
            return;
        _addressEditing = true;
        CrumbBar.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Text = Vm.CurrentPath is null
            ? string.Empty
            : Vm.CurrentPath == SpecialLocations.RecycleBin ? "回收站" : Vm.CurrentPath;
        AddressBox.CaretIndex = AddressBox.Text.Length;
        AddressBox.Focus();
    }

    private void ExitAddressEditMode(bool revert, bool refocusList)
    {
        if (!_addressEditing)
            return;
        _addressEditing = false;
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbBar.Visibility = Visibility.Visible;
        if (revert)
            RefreshBreadcrumb();
        if (refocusList)
            EntryList.Focus();
    }

    private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_addressEditing)
            return;
        // 焦点移入补全列表时不退出（键盘 ↓ 后回车/返回仍可用）
        if (e.NewFocus == SuggestList || SuggestList.IsKeyboardFocusWithin)
            return;
        ExitAddressEditMode(revert: true, refocusList: false);
    }

    /// <summary>按当前目录重建面包屑段；每段可点击跳转、可下拉列出其子目录。</summary>
    private void RefreshBreadcrumb()
    {
        if (_addressEditing)
            return;

        CrumbStrip.Children.Clear();
        var path = Vm.CurrentPath;

        if (path is null)
        {
            CrumbStrip.Children.Add(BuildCrumbSegment("此电脑", null, showLeadingChevron: false));
            return;
        }
        if (path == SpecialLocations.RecycleBin)
        {
            CrumbStrip.Children.Add(BuildCrumbSegment("回收站", SpecialLocations.RecycleBin, showLeadingChevron: false));
            return;
        }

        CrumbStrip.Children.Add(BuildCrumbSegment("此电脑", null, showLeadingChevron: false));

        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (root.Length > 0)
            CrumbStrip.Children.Add(BuildCrumbSegment(root, root, showLeadingChevron: true));

        var current = root;
        var rest = path[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in rest.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            CrumbStrip.Children.Add(BuildCrumbSegment(part, current, showLeadingChevron: true));
        }

        // 深层路径超出宽度时优先保证"当前目录段 + 其下拉箭头"可见
        CrumbScroll.ScrollToEnd();
    }

    /// <summary>构造一段：文本按钮（跳转到该层）+ 下拉箭头（列出该层子目录）+ 段间分隔符。</summary>
    private UIElement BuildCrumbSegment(string label, string? navigatePath, bool showLeadingChevron)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        if (showLeadingChevron)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "›",
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Application.Current?.TryFindResource("W11.TextTertiary") as System.Windows.Media.Brush ?? Brushes.Gray,
            });
        }

        var button = new Button
        {
            Content = label,
            Padding = new Thickness(3, 1, 3, 1),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) =>
        {
            if (navigatePath is null)
                Vm.NavigateTo(null);
            else
                Vm.NavigateTo(navigatePath);
        };
        panel.Children.Add(button);

        var dropdownPath = navigatePath is null ? null : navigatePath;
        if (dropdownPath is not null)
        {
            var arrow = new ToggleButton
            {
                Content = "▼",
                FontSize = 8,
                Width = 14,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            arrow.Click += (_, _) => OpenCrumbDropdown(arrow, dropdownPath);
            panel.Children.Add(arrow);
        }

        return panel;
    }

    /// <summary>展开某段的子目录下拉（"此电脑"段列出驱动器）；单击即跳转。</summary>
    private void OpenCrumbDropdown(ToggleButton toggle, string? path)
    {
        var list = new ListBox
        {
            BorderThickness = new Thickness(0),
            MinWidth = 200,
            MaxHeight = 260,
        };

        var items = new List<string>();
        if (path is null)
        {
            items.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(drive => drive.Name));
        }
        else
        {
            try
            {
                items.AddRange(Directory.EnumerateDirectories(path)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase));
            }
            catch (Exception)
            {
                // 不可读目录：下拉显示空态
            }
        }

        if (items.Count == 0)
        {
            list.Items.Add(new ListBoxItem { Content = "（无子文件夹）", IsEnabled = false });
        }
        else
        {
            list.ItemsSource = items;
            list.SelectedItem = Vm.CurrentPath; // 定位当前所在层级（若有）
        }

        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(list, source) is not ListBoxItem item ||
                item.DataContext is not string fullPath)
                return;
            toggle.IsChecked = false;
            if (crumbDropdownPopup is not null)
                crumbDropdownPopup.IsOpen = false;
            Vm.NavigateTo(fullPath);
            e.Handled = true;
        };

        if (crumbDropdownPopup is not null)
            crumbDropdownPopup.IsOpen = false;
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = toggle,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            Child = new Border
            {
                Background = System.Windows.Application.Current?.TryFindResource("W11.MenuBackground") as System.Windows.Media.Brush ?? Brushes.White,
                BorderBrush = System.Windows.Application.Current?.TryFindResource("W11.ControlBorder") as System.Windows.Media.Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = list,
            },
        };
        popup.Closed += (_, _) => toggle.IsChecked = false;
        crumbDropdownPopup = popup;
        // Click 处于鼠标抬起处理过程中，同步开 Popup 会被随后的捕获释放立即关闭（同 ContextMenu 坑），异步打开规避
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => popup.IsOpen = true));
    }

    private System.Windows.Controls.Primitives.Popup? crumbDropdownPopup;

    // ---- 地址栏补全：输入时在当前目录条目中匹配，Popup 下拉选择 ----

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!AddressBox.IsKeyboardFocusWithin || Vm.CurrentPath is null)
        {
            AddressSuggestPopup.IsOpen = false;
            return;
        }

        var input = AddressBox.Text.Trim();
        if (input.Length == 0)
        {
            AddressSuggestPopup.IsOpen = false;
            return;
        }

        SuggestList.ItemsSource = Vm.Entries
            .Where(entry => entry.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.Equals(entry.Name, input, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        AddressSuggestPopup.IsOpen = SuggestList.Items.Count > 0;
    }

    private void AddressBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when AddressSuggestPopup.IsOpen:
                SuggestList.Focus();
                SuggestList.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.Escape when AddressSuggestPopup.IsOpen:
                AddressSuggestPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Escape:
                // Esc：放弃编辑，还原面包屑显示
                ExitAddressEditMode(revert: true, refocusList: true);
                e.Handled = true;
                break;
        }
    }

    private void SuggestList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ActivateSuggestion();
                e.Handled = true;
                break;
            case Key.Escape:
                AddressSuggestPopup.IsOpen = false;
                AddressBox.Focus();
                AddressBox.CaretIndex = AddressBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Up when SuggestList.SelectedIndex == 0:
                AddressBox.Focus();
                AddressBox.CaretIndex = AddressBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private void SuggestList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 单击即完成补全（与资源管理器地址栏下拉一致）
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(SuggestList, source) is ListBoxItem item)
        {
            SuggestList.SelectedItem = item.DataContext;
            ActivateSuggestion();
            e.Handled = true;
        }
    }

    private void SuggestList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ActivateSuggestion();

    private void ActivateSuggestion()
    {
        if (SuggestList.SelectedItem is not FsEntry entry)
            return;
        AddressSuggestPopup.IsOpen = false;
        if (entry.IsDirectory)
        {
            Vm.NavigateTo(entry.FullPath);
            ExitAddressEditMode(revert: false, refocusList: true);
        }
        else
        {
            AddressBox.Text = entry.FullPath;
            AddressBox.CaretIndex = AddressBox.Text.Length;
            AddressBox.Focus();
        }
    }

    // ---- 内联重命名（F2）：名称单元格内的编辑框 ----

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FsEntry entry)
            return;
        box.Focus();
        var extension = entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name);
        box.Select(0, Math.Max(0, entry.Name.Length - extension.Length));
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FsEntry entry)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _ = Vm.CommitRenameAsync(entry, box.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                Vm.CancelRename();
                e.Handled = true;
                break;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FsEntry entry)
            return;
        if (!entry.IsRenaming)
            return; // 已经提交或取消（回车路径会先处理）
        _ = Vm.CommitRenameAsync(entry, box.Text);
    }

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape)
            return;
        Vm.FilterText = "";
        EntryList.Focus();
        e.Handled = true;
    }

    private void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedPaths.Count == 0)
            return;
        FileOps.Core.ShellDialogs.ShowOpenWithDialog(Vm.SelectedPaths[0]);
    }

    private void Properties_Click(object sender, RoutedEventArgs e) => ShowProperties();

    private void ShowProperties()
    {
        if (Vm.SelectedPaths.Count == 0)
            return;
        var window = Window.GetWindow(this);
        var ownerHwnd = window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        FileOps.Core.ShellDialogs.ShowFileProperties(ownerHwnd, Vm.SelectedPaths[0]);
    }

    /// <summary>搜索结果右键"打开所在文件夹"：跳转到文件目录并定位该文件。</summary>
    private void OpenContainingFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedEntries.LastOrDefault() is not { } entry || entry.IsDirectory)
            return;
        var parent = Path.GetDirectoryName(entry.FullPath);
        if (parent is null || !Directory.Exists(parent))
            return;

        Vm.NavigateTo(parent);
        // LoadEntries 为同步枚举，此时列表已就绪，直接定位
        if (Vm.Entries.FirstOrDefault(item =>
                string.Equals(item.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            EntryList.SelectedItem = match;
            EntryList.ScrollIntoView(match);
        }
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelection(EntryList.SelectedItems.Cast<FsEntry>().ToList());

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e) =>
        Vm.OpenEntryCommand.Execute((sender as ListViewItem)?.Content);

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        // 列宽拖拽结束时 Header 会再收到一次 Click，吞掉避免误排序
        if (_suppressHeaderClick)
        {
            _suppressHeaderClick = false;
            return;
        }

        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header)
            return;

        var tag = header.Column == NameColumn ? "Name"
            : header.Column == ModifiedColumn ? "Modified"
            : header.Column == CreatedColumn ? "Created"
            : header.Column == TypeColumn ? "Type"
            : header.Column == SizeColumn ? "Size"
            : null;

        if (tag is null)
            return;

        Vm.SetSort(tag);
        UpdateSortHeaders();
    }

    // ---- 列管理：列宽拖拽、右键显隐、随会话记忆（全局共享） ----

    private bool _suppressHeaderClick;

    private string? ColumnKey(GridViewColumn column) => column == NameColumn ? "Name"
        : column == ModifiedColumn ? "Modified"
        : column == CreatedColumn ? "Created"
        : column == TypeColumn ? "Type"
        : column == SizeColumn ? "Size"
        : null;

    private static double DefaultColumnWidth(string key) => key switch
    {
        "Name" => 280,
        "Modified" or "Created" => 140,
        _ => 90,
    };

    private void ColumnResizer_DragStarted(object sender, DragStartedEventArgs e) => _suppressHeaderClick = true;

    private void ColumnResizer_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { TemplatedParent: GridViewColumnHeader { Column: not null } header }
            || ColumnKey(header.Column) is not { } key
            || header.Column.Width <= 0)
            return;

        header.Column.Width = Math.Round(Math.Clamp(header.Column.Width + e.HorizontalChange, 40, 800));
        ColumnLayoutStore.SetWidth(key, header.Column.Width);
    }

    private void ColumnResizer_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Click 在 DragCompleted 之后同步触发，用低优先级队列复位，保证吞掉的只是本次拖拽附带的 Click
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => _suppressHeaderClick = false));
    }

    /// <summary>列头右键：显隐开关与重置列宽（替代文件右键菜单）。</summary>
    private void EntryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header
            || ColumnKey(header.Column) is null)
            return;

        e.Handled = true; // 阻止 ListView 的文件右键菜单
        var menu = BuildColumnMenu();
        menu.PlacementTarget = EntryList;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        // 右键事件处理过程中同步打开会被随后的鼠标事件立即关闭（同面包屑下拉坑），异步打开规避
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => menu.IsOpen = true));
    }

    private ContextMenu BuildColumnMenu()
    {
        var menu = new ContextMenu();
        foreach (var (column, title) in new[]
                 {
                     (ModifiedColumn, "修改时间"),
                     (CreatedColumn, "创建时间"),
                     (TypeColumn, "类型"),
                     (SizeColumn, "大小"),
                 })
        {
            var key = ColumnKey(column)!;
            var item = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = !ColumnLayoutStore.Hidden.Contains(key),
            };
            item.Click += (_, _) => ColumnLayoutStore.SetHidden(key, !item.IsChecked);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "重置列宽" };
        reset.Click += (_, _) => ColumnLayoutStore.Reset();
        menu.Items.Add(reset);
        return menu;
    }

    /// <summary>按全局列布局应用各列宽度与显隐（名称列恒显示）。</summary>
    private void ApplyColumnLayout()
    {
        ApplyColumn(NameColumn, "Name");
        ApplyColumn(ModifiedColumn, "Modified");
        ApplyColumn(CreatedColumn, "Created");
        ApplyColumn(TypeColumn, "Type");
        ApplyColumn(SizeColumn, "Size");

        void ApplyColumn(GridViewColumn column, string key)
        {
            if (key != "Name" && ColumnLayoutStore.Hidden.Contains(key))
            {
                column.Width = 0;
                return;
            }
            column.Width = ColumnLayoutStore.Widths.TryGetValue(key, out var width) && width >= 40
                ? width
                : DefaultColumnWidth(key);
        }
    }

    /// <summary>按当前排序列与方向刷新列头箭头（▲/▼）。</summary>
    public void UpdateSortHeaders()
    {
        SetHeader(NameColumn, "Name");
        SetHeader(ModifiedColumn, "Modified");
        SetHeader(CreatedColumn, "Created");
        SetHeader(TypeColumn, "Type");
        SetHeader(SizeColumn, "Size");

        void SetHeader(GridViewColumn column, string tag)
        {
            var title = tag == "Modified" && Vm.IsRecycleBinView ? "删除时间" : SortHeaderTitles[tag];
            if (string.Equals(Vm.SortColumn, tag, StringComparison.Ordinal))
                title += Vm.SortDescending ? " ▼" : " ▲";
            column.Header = title;
        }
    }

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EntryList.SelectAll();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Vm.OpenEntryCommand.Execute(EntryList.SelectedItem);
                e.Handled = true;
                break;
            case Key.F5:
                Vm.RefreshCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Back:
                Vm.UpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                if (Keyboard.Modifiers == ModifierKeys.Shift && !Vm.IsRecycleBinView)
                    Vm.DeletePermanentlyCommand.Execute(null);
                else
                    Vm.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2:
                Vm.RenameCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void Pane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 焦点在文本框内时放行原生编辑快捷键（Ctrl+C/X/V/Z/A 等），避免误触文件操作
        if (e.OriginalSource is TextBoxBase)
            return;

        // Ctrl+Shift+C：复制文件路径
        if (e.Key is Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Vm.CopyPathCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+N：新建文件夹
        if (e.Key is Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Vm.NewFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Alt 系：Alt+Enter 属性、Alt+←/→ 后退/前进
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ShowProperties();
                    e.Handled = true;
                    return;
                case Key.Left:
                    if (Vm.BackCommand.CanExecute(null))
                        Vm.BackCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.Right:
                    if (Vm.ForwardCommand.CanExecute(null))
                        Vm.ForwardCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        switch (e.Key)
        {
            case Key.C:
                Vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.X:
                Vm.CutCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V:
                Vm.PasteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D:
                Vm.DeleteCommand.Execute(null); // Ctrl+D：删除到回收站
                e.Handled = true;
                break;
            case Key.T:
                AddTab();
                e.Handled = true;
                break;
            case Key.W:
                CloseTab(_activeTabIndex);
                e.Handled = true;
                break;
            case Key.F:
                FilterBox.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>"位置"列：递归搜索时自动显示相对路径，清空搜索后隐藏（不进全局列布局，不参与排序）。</summary>
    private void UpdateLocationColumnVisibility()
    {
        LocationColumn.Width = Vm.IsSearchResultsView ? 180 : 0;
    }

    // ---- 键入跳转（type-ahead）：列表聚焦时直接输入名称匹配条目，1s 无输入重置 ----

    private readonly System.Windows.Threading.DispatcherTimer _typeAheadTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };
    private string _typeAheadBuffer = string.Empty;
    private string? _typeAheadLastChar;

    private void EntryList_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 重命名/地址栏等文本框内的输入放行
        if (e.OriginalSource is TextBoxBase || Keyboard.Modifiers != ModifierKeys.None)
            return;

        _typeAheadTimer.Stop();
        // 同一字符连按（缓冲仍为单字符）= 在匹配项之间循环；否则累积缓冲
        var isRepeat = _typeAheadLastChar == e.Text && _typeAheadBuffer.Length == 1;
        _typeAheadBuffer = isRepeat ? e.Text : _typeAheadBuffer + e.Text;
        _typeAheadLastChar = e.Text;

        var candidates = Vm.Entries
            .Where(entry => entry.Name.StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (candidates.Count == 0 && _typeAheadBuffer.Length > 1)
        {
            // 缓冲积累过头：退回最后一个字符再试
            _typeAheadBuffer = _typeAheadBuffer[^1..];
            candidates = Vm.Entries
                .Where(entry => entry.Name.StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        if (candidates.Count > 0)
        {
            var currentIndex = candidates.IndexOf(EntryList.SelectedItem as FsEntry);
            var match = isRepeat && candidates.Count > 1
                ? candidates[(currentIndex + 1) % candidates.Count]
                : candidates[0];
            EntryList.SelectedItem = match;
            EntryList.ScrollIntoView(match);
        }

        _typeAheadTimer.Start();
        e.Handled = true;
    }

    private void ResetTypeAhead()
    {
        _typeAheadTimer.Stop();
        _typeAheadBuffer = string.Empty;
        _typeAheadLastChar = null;
    }

    // ---- 跨窗格拖拽：默认同盘移动、Ctrl=复制、Shift=移动、跨盘=复制 ----


    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPosition = e.GetPosition(EntryList);

        // 空白处按下：启动橡皮筋框选并接管本次按下（阻止默认的"点击空白清除选中"）
        if (ItemsControl.ContainerFromElement(EntryList, e.OriginalSource as DependencyObject) is null)
        {
            _rubberBandStart = _dragStartPosition;
            _rubberBandActive = false;
            _rubberBandCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _rubberBandBase = [.. EntryList.SelectedItems.Cast<FsEntry>()];
            EntryList.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 一律先武装；真正能否拖拽到 MouseMove 时判定——那时 WPF 已完成本次按下的选中更新，
        // 按住 Ctrl/Shift 直接开拖（此前无选中）也能正常触发
        _dragArmed = true;
    }

    // ---- 橡皮筋框选：空白处拖动选择条目（资源管理器行为），Ctrl=反转、Shift/默认=并入 ----

    private void UpdateRubberBand(Point start, Point position)
    {
        if (!_rubberBandActive)
        {
            if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            _rubberBandActive = true;
            _rubberAdorner = new RubberBandAdorner(EntryList);
            AdornerLayer.GetAdornerLayer(EntryList)?.Add(_rubberAdorner);
        }

        var rect = new Rect(start, position);
        _rubberAdorner?.Update(rect);
        ApplyRubberSelection(rect);
    }

    private void ApplyRubberSelection(Rect rect)
    {
        // 只命中已实例化的容器（大图标视图全部实例化；列表视图虚拟化时仅可见项可选）
        var inRect = new HashSet<FsEntry>();
        foreach (var item in Vm.Entries)
        {
            if (EntryList.ItemContainerGenerator.ContainerFromItem(item) is not ListViewItem container)
                continue;
            var bounds = container.TransformToAncestor(EntryList).TransformBounds(
                new Rect(new Point(), container.RenderSize));
            if (bounds.IntersectsWith(rect))
                inRect.Add(item);
        }

        // 批量更新期间挂起 SelectionChanged，结束后手动同步一次
        EntryList.SelectionChanged -= EntryList_SelectionChanged;
        try
        {
            EntryList.SelectedItems.Clear();
            foreach (var item in Vm.Entries)
            {
                var selected = _rubberBandCtrl
                    ? inRect.Contains(item) != _rubberBandBase.Contains(item)
                    : _rubberBandBase.Contains(item) || inRect.Contains(item);
                if (selected)
                    EntryList.SelectedItems.Add(item);
            }
        }
        finally
        {
            EntryList.SelectionChanged += EntryList_SelectionChanged;
        }
        Vm.SetSelection(EntryList.SelectedItems.Cast<FsEntry>().ToList());
    }

    private void EntryList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndRubberBand();

    private void EntryList_LostMouseCapture(object sender, MouseEventArgs e) => EndRubberBand();

    private void EndRubberBand()
    {
        if (_rubberBandStart is null)
            return;
        var wasActive = _rubberBandActive;
        _rubberBandStart = null;
        _rubberBandActive = false;

        if (_rubberAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(EntryList)?.Remove(_rubberAdorner);
            _rubberAdorner = null;
        }
        if (EntryList.IsMouseCaptured)
            EntryList.ReleaseMouseCapture();

        if (!wasActive)
            EntryList.SelectedItems.Clear(); // 原地单击空白 = 取消选中（与资源管理器一致）
    }

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_rubberBandStart is { } rubberStart)
        {
            UpdateRubberBand(rubberStart, e.GetPosition(EntryList));
            return;
        }

        if (Vm.RenamingEntry is not null)
        {
            _dragArmed = false; // 内联重命名中禁止拖拽，避免编辑框被拖走
            return;
        }

        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(EntryList);
        if (Math.Abs(position.X - _dragStartPosition.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStartPosition.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragArmed = false;
        var paths = Vm.SelectedPaths.ToArray();
        if (paths.Length == 0)
            return;

        var data = new DataObject();
        var dropList = new System.Collections.Specialized.StringCollection();
        dropList.AddRange(paths);
        data.SetFileDropList(dropList);

        // 允许复制和移动：拖出到资源管理器/桌面时由目标决定；应用内由 DragOver 计算
        var result = DragDrop.DoDragDrop(EntryList, data, DragDropEffects.Copy | DragDropEffects.Move);
        if (result != DragDropEffects.None)
            Vm.RefreshCommand.Execute(null); // 拖出（可能被移动）或原地拖放都可能改变源目录内容
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Vm.CurrentPath is null || Vm.IsRecycleBinView)
        {
            _pendingDropEffect = DragDropEffects.None;
            e.Effects = DragDropEffects.None;
            DropZone.BorderBrush = System.Windows.Application.Current?.TryFindResource("W11.DangerBrush") as System.Windows.Media.Brush ?? Brushes.IndianRed;
            e.Handled = true;
            return;
        }

        _pendingDropEffect = DecideDropEffect(e);
        e.Effects = _pendingDropEffect;
        DropZone.BorderBrush = DropHintBrush;
        e.Handled = true;
    }

    private DragDropEffects DecideDropEffect(DragEventArgs e)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            return DragDropEffects.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            return DragDropEffects.Move;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var sameVolume = paths.Length > 0 && paths.All(
            path => FileOps.Core.TransferHelper.IsSameVolume(path, Vm.CurrentPath!));
        return sameVolume ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void EntryList_DragLeave(object sender, DragEventArgs e) =>
        DropZone.BorderBrush = Brushes.Transparent;

    private void EntryList_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = Brushes.Transparent;
        // Drop 事件的 e.Effects 不保证携带最后一次 DragOver 的结果，用自己记录的值
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            _ = Vm.PastePathsAsync(paths, move: _pendingDropEffect.HasFlag(DragDropEffects.Move));
        e.Handled = true;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private Point _dragStartPosition;
    private bool _dragArmed;
    private DragDropEffects _pendingDropEffect;
    private readonly List<PaneViewModel> _tabs = [];
    private int _activeTabIndex;

    private static readonly Brush DropHintBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
    private static readonly Brush ActiveTabBrush = Brushes.White;
    private static readonly Brush TabBorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE1, 0xE1));

    private static readonly Dictionary<string, string> SortHeaderTitles = new()
    {
        ["Name"] = "名称",
        ["Modified"] = "修改时间",
        ["Type"] = "类型",
        ["Size"] = "大小",
    };

    public ExplorerPane()
    {
        InitializeComponent();
        Vm = CreateTab();
        _tabs.Add(Vm);
        DataContext = Vm;
        Loaded += (_, _) =>
        {
            if (_initialized)
                return;
            _initialized = true;
            Vm.Initialize(InitialPath);
            UpdateSortHeaders();
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

    /// <summary>加载完成后是否自动获得键盘焦点（多窗格时只给一个窗格）。</summary>
    public bool FocusOnLoad
    {
        get => (bool)GetValue(FocusOnLoadProperty);
        set => SetValue(FocusOnLoadProperty, value);
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab();

    private PaneViewModel CreateTab()
    {
        var tab = new PaneViewModel();
        tab.CurrentPathChanged += path =>
        {
            if (ReferenceEquals(tab, Vm))
                OnCurrentPathChanged(path);
            RefreshTabStrip();
        };
        tab.PropertyChanged += (_, e) =>
        {
            if (ReferenceEquals(tab, Vm) && e.PropertyName == nameof(PaneViewModel.ViewMode))
                QueueThumbnailsForCurrentEntries();
        };
        tab.EntryFocusRequested += () =>
        {
            if (ReferenceEquals(tab, Vm))
                EntryList.Focus();
        };
        tab.Entries.CollectionChanged += (_, e) =>
        {
            if (ReferenceEquals(tab, Vm) && tab.ViewMode != "Details" && e.NewItems is not null)
                ThumbnailLoader.EnqueueRange(e.NewItems.Cast<FsEntry>());
        };
        return tab;
    }

    private void AddTab()
    {
        var tab = CreateTab();
        tab.Initialize(Vm.CurrentPath);
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
        EntryList.Focus();
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
                Padding = new Thickness(8, 2, 4, 2),
                Margin = new Thickness(0, 0, 3, 0),
                Background = i == _activeTabIndex ? ActiveTabBrush : Brushes.Transparent,
                BorderBrush = TabBorderBrush,
            };
            tabButton.Click += (_, _) => SwitchTab(index);
            TabStrip.Children.Add(tabButton);
        }
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

        SwitchTab(Math.Clamp(state.ActiveTabIndex, 0, _tabs.Count - 1));
    }

    public void FocusList() => EntryList.Focus();

    /// <summary>窗格目录变化时（含列表/地址栏/前进后退），让文件树跟随定位。</summary>
    private void OnCurrentPathChanged(string? path)
    {
        _addressEditing = false;
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbBar.Visibility = Visibility.Visible;
        RefreshBreadcrumb();

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
                Foreground = Brushes.Gray,
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
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = list,
            },
        };
        popup.Closed += (_, _) => toggle.IsChecked = false;
        crumbDropdownPopup = popup;
        popup.IsOpen = true;
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

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedPaths.Count == 0)
            return;
        var window = Window.GetWindow(this);
        var ownerHwnd = window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        FileOps.Core.ShellDialogs.ShowFileProperties(ownerHwnd, Vm.SelectedPaths[0]);
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelection(EntryList.SelectedItems.Cast<FsEntry>().ToList());

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e) =>
        Vm.OpenEntryCommand.Execute((sender as ListViewItem)?.Content);

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header)
            return;

        var tag = header.Column == NameColumn ? "Name"
            : header.Column == ModifiedColumn ? "Modified"
            : header.Column == TypeColumn ? "Type"
            : header.Column == SizeColumn ? "Size"
            : null;

        if (tag is null)
            return;

        Vm.SetSort(tag);
        UpdateSortHeaders();
    }

    /// <summary>按当前排序列与方向刷新列头箭头（▲/▼）。</summary>
    public void UpdateSortHeaders()
    {
        SetHeader(NameColumn, "Name");
        SetHeader(ModifiedColumn, "Modified");
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

    // ---- 跨窗格拖拽：默认同盘移动、Ctrl=复制、Shift=移动、跨盘=复制 ----


    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPosition = e.GetPosition(EntryList);
        // 一律先武装；真正能否拖拽到 MouseMove 时判定——那时 WPF 已完成本次按下的选中更新，
        // 按住 Ctrl/Shift 直接开拖（此前无选中）也能正常触发
        _dragArmed = true;
    }

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
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
            DropZone.BorderBrush = Brushes.IndianRed;
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

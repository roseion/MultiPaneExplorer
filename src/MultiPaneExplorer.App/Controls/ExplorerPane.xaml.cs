using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private Point _dragStartPosition;
    private bool _dragArmed;
    private DragDropEffects _pendingDropEffect;
    private readonly List<PaneViewModel> _tabs = [];
    private int _activeTabIndex;

    private static readonly Brush DropHintBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
    private static readonly Brush ActiveTabBrush = Brushes.White;
    private static readonly Brush TabBorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

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
        EntryList.Focus();
    }

    private static string TitleOf(PaneViewModel tab)
    {
        if (tab.CurrentPath is null)
            return "此电脑";
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

    public void FocusList() => EntryList.Focus();

    /// <summary>窗格目录变化时（含列表/地址栏/前进后退），让文件树跟随定位。</summary>
    private void OnCurrentPathChanged(string? path)
    {
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
        Vm.NavigateToAddress(Vm.PathText);
        EntryList.Focus();
        e.Handled = true;
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
        Vm.SetSelection(EntryList.SelectedItems.Cast<FsEntry>().Select(item => item.FullPath));

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
            var title = SortHeaderTitles[tag];
            if (string.Equals(Vm.SortColumn, tag, StringComparison.Ordinal))
                title += Vm.SortDescending ? " ▼" : " ▲";
            column.Header = title;
        }
    }

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
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
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        switch (e.Key)
        {
            case Key.C:
                Vm.CopyCommand.Execute(null);
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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Vm.CurrentPath is null)
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Vm = new PaneViewModel();
        DataContext = Vm;
        Loaded += (_, _) =>
        {
            if (_initialized)
                return;
            _initialized = true;
            Vm.CurrentPathChanged += OnCurrentPathChanged;
            Vm.Initialize(InitialPath);
            UpdateSortHeaders();
            if (FocusOnLoad)
                EntryList.Focus();
        };
    }

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

    public PaneViewModel Vm { get; }

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
        }
    }

    // ---- 跨窗格拖拽复制（基础版：拖动即复制） ----

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPosition = e.GetPosition(EntryList);
        _dragArmed = EntryList.SelectedItem is not null;
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
        _ = DragDrop.DoDragDrop(EntryList, data, DragDropEffects.Copy);
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void EntryList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            _ = Vm.PastePathsAsync(paths);
        e.Handled = true;
    }
}

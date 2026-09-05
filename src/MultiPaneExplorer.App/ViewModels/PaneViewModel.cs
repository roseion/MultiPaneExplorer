using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileOps.Core;
using MultiPaneExplorer.App.Models;
using MultiPaneExplorer.App.Views;

namespace MultiPaneExplorer.App.ViewModels;

/// <summary>单个窗格的状态与逻辑：导航历史、目录列表、文件树、复制/粘贴/删除/重命名/新建。</summary>
public partial class PaneViewModel : ObservableObject
{
    private readonly IFileOperationService _fileOps;
    private readonly IShortcutService _shortcuts;
    private readonly ISearchService _search;
    private readonly Stack<string?> _back = new();
    private readonly Stack<string?> _forward = new();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _searchCts;
    private int _searchGeneration;
    private bool _initialized;
    private bool _isBusy;

    public PaneViewModel(
        IFileOperationService? fileOps = null,
        IShortcutService? shortcuts = null,
        ISearchService? search = null)
    {
        _fileOps = fileOps ?? new FileOperationService();
        _shortcuts = shortcuts ?? new WshShortcutService();
        _search = search ?? new FileSystemSearchService();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (_initialized && !_isBusy)
                LoadEntries();
        };
    }

    /// <summary>当前目录；null 表示"此电脑"（驱动器列表）。</summary>
    [ObservableProperty]
    private string? _currentPath;

    [ObservableProperty]
    private string _pathText = "此电脑";

    [ObservableProperty]
    private ObservableCollection<FsEntry> _entries = new();

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>文件树侧栏是否可见（每窗格独立）。</summary>
    [ObservableProperty]
    private bool _showTree = true;

    /// <summary>是否显示隐藏文件（全局设置，由主窗口同步到所有窗格）。</summary>
    [ObservableProperty]
    private bool _showHiddenFiles;

    /// <summary>当前排序列：Name / Modified / Type / Size。</summary>
    [ObservableProperty]
    private string _sortColumn = "Name";

    [ObservableProperty]
    private bool _sortDescending;

    /// <summary>列表过滤/搜索关键字；导航时自动清空。</summary>
    [ObservableProperty]
    private string _filterText = "";

    /// <summary>true=在当前目录及全部子目录中搜索；false=仅过滤当前列表。</summary>
    [ObservableProperty]
    private bool _searchSubdirectories;

    /// <summary>文件树根节点（驱动器）。</summary>
    public ObservableCollection<FsTreeNode> TreeRoots { get; } = new();

    /// <summary>当前选中条目的完整路径，由视图在选中变化时回写。</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    /// <summary>当前目录变化后通知视图同步文件树定位。</summary>
    public event Action<string?>? CurrentPathChanged;

    partial void OnCurrentPathChanged(string? value)
    {
        PathText = value ?? "此电脑";
        UpCommand.NotifyCanExecuteChanged();
        RestartWatcher(value);
        if (FilterText.Length > 0)
            FilterText = ""; // 导航后重置过滤/搜索（触发 OnFilterTextChanged 刷新列表）
        CurrentPathChanged?.Invoke(value);
    }

    partial void OnFilterTextChanged(string value)
    {
        if (SearchSubdirectories)
            _ = RunSearchAsync();
        else
            LoadEntries();
    }

    partial void OnSearchSubdirectoriesChanged(bool value)
    {
        if (SearchSubdirectories && FilterText.Trim().Length > 0)
            _ = RunSearchAsync();
        else if (!SearchSubdirectories)
            LoadEntries();
    }

    /// <summary>子目录搜索：后台递归枚举，命中结果分批回 UI 线程增量追加。</summary>
    private async Task RunSearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var generation = ++_searchGeneration;

        var root = CurrentPath;
        var pattern = FilterText.Trim();
        if (root is null || pattern.Length == 0)
            return;

        Entries.Clear();
        StatusText = $"正在搜索“{pattern}”…";
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var batch = new List<FsEntry>();

        try
        {
            await foreach (var path in _search.SearchAsync(root, pattern, cts.Token))
            {
                var isDirectory = Directory.Exists(path);
                batch.Add(new FsEntry
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    IsDirectory = isDirectory,
                    SizeBytes = isDirectory ? null : new FileInfo(path).Length,
                    ModifiedTime = isDirectory
                        ? Directory.GetLastWriteTime(path)
                        : File.GetLastWriteTime(path),
                });

                if (batch.Count < 100)
                    continue;
                var chunk = batch;
                batch = new List<FsEntry>();
                _ = dispatcher.BeginInvoke(() => AppendSearchResults(generation, chunk));
            }

            if (batch.Count > 0)
            {
                var chunk = batch;
                _ = dispatcher.BeginInvoke(() => AppendSearchResults(generation, chunk));
            }
        }
        catch (OperationCanceledException)
        {
            // 被新搜索/导航取代，静默结束
        }
        catch (Exception ex)
        {
            if (generation == _searchGeneration)
                StatusText = $"搜索失败：{ex.Message}";
        }
    }

    private void AppendSearchResults(int generation, List<FsEntry> chunk)
    {
        if (generation != _searchGeneration)
            return;
        foreach (var entry in chunk)
            Entries.Add(entry);
        StatusText = $"正在搜索… 已找到 {Entries.Count} 个项目";
    }

    /// <summary>首次加载：定位到 initialPath，不写入导航历史。只生效一次。</summary>
    public void Initialize(string? initialPath = null)
    {
        if (_initialized)
            return;
        _initialized = true;
        LoadTreeRoots();
        CurrentPath = Directory.Exists(initialPath) ? initialPath : null;
        LoadEntries();
    }

    public void SetSelection(IEnumerable<string> paths) => SelectedPaths = paths.ToList();

    public void NavigateTo(string? path)
    {
        if (string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            LoadEntries();
            return;
        }

        _back.Push(CurrentPath);
        _forward.Clear();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        CurrentPath = path;
        LoadEntries();
    }

    /// <summary>地址栏回车：支持目录、文件（定位到所在目录）和"此电脑"。</summary>
    public void NavigateToAddress(string text)
    {
        var trimmed = text.Trim();
        var root = Path.GetPathRoot(trimmed);
        if (root is not null && trimmed.Length > root.Length)
            trimmed = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (trimmed.Length == 0 || trimmed == "此电脑")
        {
            NavigateTo(null);
            return;
        }

        if (Directory.Exists(trimmed))
        {
            NavigateTo(Path.GetFullPath(trimmed));
            return;
        }

        if (File.Exists(trimmed))
        {
            NavigateTo(Path.GetDirectoryName(Path.GetFullPath(trimmed)));
            return;
        }

        StatusText = $"路径不存在：{text}";
    }

    /// <summary>让文件树定位并选中当前目录（找不到时保持树状态不变）。</summary>
    public void RevealInTree()
    {
        if (CurrentPath is null)
            return;
        foreach (var root in TreeRoots)
        {
            if (root.TryReveal(CurrentPath))
                return;
        }
    }

    // ---- 目录变化自动刷新 ----

    /// <summary>导航后把 FileSystemWatcher 切换到新目录（"此电脑"下不监视）。</summary>
    private void RestartWatcher(string? path)
    {
        if (_watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
        }

        if (path is null || !Directory.Exists(path))
            return;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, _) => ScheduleAutoRefresh();
            watcher.Deleted += (_, _) => ScheduleAutoRefresh();
            watcher.Renamed += (_, _) => ScheduleAutoRefresh();
            watcher.Changed += (_, _) => ScheduleAutoRefresh();
            watcher.Error += (_, _) => ScheduleAutoRefresh();
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or ArgumentException)
        {
            // 目录不可监视时退化为手动刷新（F5）
        }
    }

    /// <summary>文件系统事件防抖：重置 300ms 定时器，静止后统一刷新一次。</summary>
    private void ScheduleAutoRefresh() =>
        _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_initialized || _isBusy)
                return;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });

    partial void OnShowHiddenFilesChanged(bool value) => LoadEntries();

    /// <summary>点击列头排序：同列再次点击反向，换列则默认升序。</summary>
    public void SetSort(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.Ordinal))
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        LoadEntries();
    }

    private void LoadTreeRoots()
    {
        if (TreeRoots.Count > 0)
            return;
        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.IsReady)
                     .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            TreeRoots.Add(new FsTreeNode(drive.Name, name: drive.Name));
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (_back.Count == 0)
            return;
        _forward.Push(CurrentPath);
        CurrentPath = _back.Pop();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        LoadEntries();
    }

    private bool CanGoBack() => _back.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        if (_forward.Count == 0)
            return;
        _back.Push(CurrentPath);
        CurrentPath = _forward.Pop();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        LoadEntries();
    }

    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up()
    {
        if (CurrentPath is null)
            return;

        var root = Path.GetPathRoot(CurrentPath);
        var trimmed = Path.TrimEndingDirectorySeparator(CurrentPath);
        var atDriveRoot = root is not null
            && string.Equals(trimmed, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
        NavigateTo(atDriveRoot ? null : Path.GetDirectoryName(trimmed));
    }

    private bool CanGoUp() => CurrentPath is not null;

    [RelayCommand]
    private void Refresh() => LoadEntries();

    [RelayCommand]
    private void OpenEntry(FsEntry? entry)
    {
        if (entry is null)
            return;

        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }

        // 指向文件夹的快捷方式：在软件内打开目标目录，而不是调用系统资源管理器
        if (string.Equals(Path.GetExtension(entry.FullPath), ".lnk", StringComparison.OrdinalIgnoreCase)
            && _shortcuts.ResolveTarget(entry.FullPath) is { } target
            && Directory.Exists(target))
        {
            NavigateTo(target);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"无法打开：{entry.Name}（{ex.Message}）";
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (SelectedPaths.Count == 0)
            return;

        try
        {
            var dropList = new System.Collections.Specialized.StringCollection();
            dropList.AddRange(SelectedPaths.ToArray());
            System.Windows.Clipboard.SetFileDropList(dropList);
            StatusText = $"已复制 {SelectedPaths.Count} 个项目";
        }
        catch (Exception ex)
        {
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var sources = new List<string>();
        try
        {
            if (System.Windows.Clipboard.ContainsFileDropList())
                sources.AddRange(System.Windows.Clipboard.GetFileDropList().Cast<string>());
        }
        catch (Exception ex)
        {
            StatusText = $"读取剪贴板失败：{ex.Message}";
            return;
        }

        if (sources.Count == 0)
        {
            StatusText = "剪贴板中没有可粘贴的文件";
            return;
        }

        await PastePathsAsync(sources);
    }

    /// <summary>把指定路径列表转移到当前目录（move=false 复制 / true 移动）；剪贴板粘贴与拖拽放置共用。
    /// 传输在后台线程执行，进度实时更新状态栏，同名冲突弹出对话框询问。</summary>
    public async Task PastePathsAsync(IReadOnlyList<string> sources, bool move = false)
    {
        if (CurrentPath is null)
        {
            StatusText = "“此电脑”不能作为粘贴目标，请先进入某个目录";
            return;
        }

        var target = CurrentPath;
        var verb = move ? "移动" : "粘贴";
        var cts = new CancellationTokenSource();
        ConflictDecision? remembered = null;
        var options = new CopyOptions
        {
            Progress = new Progress<CopyProgress>(p => StatusText =
                p.TotalBytes > 0 && p.DoneBytes >= p.TotalBytes
                    ? $"{verb}完成，正在刷新列表…"
                    : $"正在{verb}… {FsEntry.FormatSize(p.DoneBytes)} / {FsEntry.FormatSize(p.TotalBytes)}（{p.Percent}%）"),
            OnConflict = context =>
            {
                if (remembered.HasValue)
                    return remembered.Value;

                var (decision, applyToAll, cancelled) = AskConflict(context);
                if (cancelled)
                {
                    cts.Cancel();
                    return ConflictDecision.Skip;
                }

                if (applyToAll)
                    remembered = decision;
                return decision;
            },
        };

        _isBusy = true;
        StatusText = $"正在{verb} {sources.Count} 个项目…";
        try
        {
            var result = await Task.Run(() => move
                ? _fileOps.MoveIntoAsync(sources, target, options, cts.Token)
                : _fileOps.CopyIntoAsync(sources, target, options, cts.Token));
            StatusText = result.HasErrors
                ? $"{verb}完成：{result.CopiedCount} 个成功，{result.SkippedCount} 个跳过，{result.Errors.Count} 个失败"
                : result.SkippedCount > 0
                    ? $"已{verb} {result.CopiedCount} 个项目，跳过 {result.SkippedCount} 个"
                    : $"已{verb} {result.CopiedCount} 个项目";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{verb}已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"{verb}失败：{ex.Message}";
        }
        finally
        {
            _isBusy = false;
        }
        LoadEntries();
    }

    /// <summary>冲突回调发生在后台复制线程，把询问转发到 UI 线程弹窗（复制流程暂停等待答复）。</summary>
    private (ConflictDecision Decision, bool ApplyToAll, bool Cancelled) AskConflict(ConflictContext context) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Views.ConflictDialog(context.Item, context.Index)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            dialog.ShowDialog();
            return (dialog.Decision, dialog.ApplyToAll, dialog.DialogResult != true);
        });

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedPaths.Count == 0)
            return;

        var paths = SelectedPaths.ToList();
        StatusText = $"正在删除 {paths.Count} 个项目…";
        try
        {
            var result = await Task.Run(() => _fileOps.DeleteToRecycleBinAsync(paths));
            StatusText = result.HasErrors
                ? $"删除完成：{result.DeletedCount} 个成功，{result.Errors.Count} 个失败"
                : $"已删除 {result.DeletedCount} 个项目到回收站";
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
        }
        LoadEntries();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedPaths.Count != 1)
        {
            StatusText = "请先选中一个要重命名的项目";
            return;
        }

        var path = SelectedPaths[0];
        var dialog = new RenameDialog(Path.GetFileName(path))
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var newPath = await _fileOps.RenameAsync(path, dialog.InputText.Trim());
            StatusText = $"已重命名为：{Path.GetFileName(newPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"重命名失败：{ex.Message}";
        }
        LoadEntries();
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (CurrentPath is null)
        {
            StatusText = "“此电脑”下不能新建文件夹，请先进入某个目录";
            return;
        }

        try
        {
            var created = await Task.Run(() => _fileOps.CreateDirectoryAsync(CurrentPath));
            StatusText = $"已新建文件夹：{Path.GetFileName(created)}";
        }
        catch (Exception ex)
        {
            StatusText = $"新建文件夹失败：{ex.Message}";
        }
        LoadEntries();
    }

    /// <summary>关闭标签页时释放资源：停止刷新定时器与目录监视，取消进行中的搜索。</summary>
    public void Shutdown()
    {
        _refreshTimer.Stop();
        _searchCts?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void LoadEntries()
    {
        _searchGeneration++;
        _searchCts?.Cancel();
        Entries.Clear();
        var path = CurrentPath;
        try
        {
            if (path is null)
            {
                foreach (var drive in DriveInfo.GetDrives()
                             .Where(d => d.IsReady)
                             .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Entries.Add(new FsEntry
                    {
                        Name = drive.Name,
                        FullPath = drive.Name,
                        IsDirectory = true,
                        ModifiedTime = default,
                    });
                }
            }
            else
            {
                var directory = new DirectoryInfo(path);
                var entries = directory.EnumerateFileSystemInfos()
                    .Where(item => ShowHiddenFiles || (item.Attributes & FileAttributes.Hidden) == 0)
                    .Select(item =>
                    {
                        var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
                        return new FsEntry
                        {
                            Name = item.Name,
                            FullPath = item.FullName,
                            IsDirectory = isDirectory,
                            SizeBytes = isDirectory ? null : ((FileInfo)item).Length,
                            ModifiedTime = item.LastWriteTime,
                        };
                    })
                    .Where(entry => FilterText.Length == 0
                        || entry.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase));

                foreach (var entry in OrderEntries(entries))
                    Entries.Add(entry);
            }

            StatusText = $"{Entries.Count} 个项目" + (FilterText.Length > 0 ? "（已过滤）" : string.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            StatusText = $"无法读取目录：{ex.Message}";
        }
    }

    /// <summary>排序规则：文件夹恒在文件之前，其余按当前列与方向排列。</summary>
    private IOrderedEnumerable<FsEntry> OrderEntries(IEnumerable<FsEntry> entries)
    {
        var directoriesFirst = entries.OrderByDescending(entry => entry.IsDirectory);
        return (SortColumn, SortDescending) switch
        {
            ("Modified", false) => directoriesFirst.ThenBy(entry => entry.ModifiedTime),
            ("Modified", true) => directoriesFirst.ThenByDescending(entry => entry.ModifiedTime),
            ("Type", false) => directoriesFirst.ThenBy(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase),
            ("Type", true) => directoriesFirst.ThenByDescending(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase),
            ("Size", false) => directoriesFirst.ThenBy(entry => entry.SizeBytes ?? -1),
            ("Size", true) => directoriesFirst.ThenByDescending(entry => entry.SizeBytes ?? -1),
            (_, false) => directoriesFirst.ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            (_, true) => directoriesFirst.ThenByDescending(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileOps.Core;
using MultiPaneExplorer.App.Models;

namespace MultiPaneExplorer.App.ViewModels;

/// <summary>单个窗格的状态与逻辑：导航历史、目录列表、复制/粘贴。</summary>
public partial class PaneViewModel : ObservableObject
{
    private readonly IFileOperationService _fileOps;
    private readonly Stack<string?> _back = new();
    private readonly Stack<string?> _forward = new();
    private bool _initialized;

    public PaneViewModel(IFileOperationService? fileOps = null)
    {
        _fileOps = fileOps ?? new FileOperationService();
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

    /// <summary>当前选中条目的完整路径，由视图在选中变化时回写。</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    partial void OnCurrentPathChanged(string? value)
    {
        PathText = value ?? "此电脑";
        UpCommand.NotifyCanExecuteChanged();
    }

    /// <summary>首次加载：定位到 initialPath，不写入导航历史。只生效一次。</summary>
    public void Initialize(string? initialPath = null)
    {
        if (_initialized)
            return;
        _initialized = true;
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
        if (CurrentPath is null)
        {
            StatusText = "“此电脑”不能作为粘贴目标，请先进入某个目录";
            return;
        }

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

        var target = CurrentPath;
        StatusText = $"正在粘贴 {sources.Count} 个项目…";
        try
        {
            var result = await Task.Run(() => _fileOps.CopyIntoAsync(sources, target));
            StatusText = result.HasErrors
                ? $"粘贴完成：{result.CopiedCount} 个成功，{result.Errors.Count} 个失败"
                : $"已粘贴 {result.CopiedCount} 个项目";
        }
        catch (Exception ex)
        {
            StatusText = $"粘贴失败：{ex.Message}";
        }
        LoadEntries();
    }

    private void LoadEntries()
    {
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
                var items = directory.EnumerateFileSystemInfos()
                    .OrderBy(e => (e.Attributes & FileAttributes.Directory) != 0 ? 0 : 1)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
                    Entries.Add(new FsEntry
                    {
                        Name = item.Name,
                        FullPath = item.FullName,
                        IsDirectory = isDirectory,
                        SizeBytes = isDirectory ? null : ((FileInfo)item).Length,
                        ModifiedTime = item.LastWriteTime,
                    });
                }
            }

            StatusText = $"{Entries.Count} 个项目";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            StatusText = $"无法读取目录：{ex.Message}";
        }
    }
}

using System.IO;

namespace FileOps.Core;

/// <summary>一条可撤销/重做的历史操作。</summary>
public interface IUndoableOperation
{
    /// <summary>操作描述（用于撤销按钮提示，如"粘贴 3 个项目"）。</summary>
    string Description { get; }

    bool CanUndo { get; }
    bool CanRedo { get; }

    Task UndoAsync(CancellationToken cancellationToken = default);
    Task RedoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 全局撤销/重做栈。新操作入栈即清空重做栈；撤销/重做失败时操作留在原栈上可重试。
/// </summary>
public sealed class UndoRedoService
{
    private readonly Stack<IUndoableOperation> _undo = new();
    private readonly Stack<IUndoableOperation> _redo = new();

    public bool CanUndo => _undo.Count > 0 && _undo.Peek().CanUndo;
    public bool CanRedo => _redo.Count > 0 && _redo.Peek().CanRedo;
    public string? UndoDescription => CanUndo ? _undo.Peek().Description : null;
    public string? RedoDescription => CanRedo ? _redo.Peek().Description : null;

    /// <summary>栈变化（可用于刷新撤销/重做按钮状态）。</summary>
    public event Action? Changed;

    /// <summary>记录一次已成功执行的操作；不可撤销的操作（如发生过覆盖）不入栈。</summary>
    public void Push(IUndoableOperation operation)
    {
        if (!operation.CanUndo)
            return;

        _redo.Clear();
        _undo.Push(operation);
        Changed?.Invoke();
    }

    /// <summary>撤销最近一次操作，返回其描述；栈空抛 <see cref="InvalidOperationException"/>。</summary>
    public async Task<string> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_undo.Count == 0)
            throw new InvalidOperationException("没有可撤销的操作");

        var operation = _undo.Peek();
        try
        {
            // 不用 ConfigureAwait(false)：Changed 事件供 UI（撤销/重做按钮状态）使用，
            // 必须回到调用线程，否则 WPF 触发跨线程访问异常
            await operation.UndoAsync(cancellationToken);
        }
        finally
        {
            Changed?.Invoke();
        }

        _undo.Pop();
        if (operation.CanRedo)
            _redo.Push(operation);
        Changed?.Invoke();
        return operation.Description;
    }

    /// <summary>重做最近一次被撤销的操作，返回其描述；栈空抛 <see cref="InvalidOperationException"/>。</summary>
    public async Task<string> RedoAsync(CancellationToken cancellationToken = default)
    {
        if (_redo.Count == 0)
            throw new InvalidOperationException("没有可重做的操作");

        var operation = _redo.Peek();
        try
        {
            // 同 UndoAsync：Changed 事件保持在调用线程
            await operation.RedoAsync(cancellationToken);
        }
        finally
        {
            Changed?.Invoke();
        }

        _redo.Pop();
        if (operation.CanUndo)
            _undo.Push(operation);
        Changed?.Invoke();
        return operation.Description;
    }
}

/// <summary>
/// 复制/移动操作（粘贴与拖拽落点共用）。
/// 复制：撤销 = 把落点删进回收站，重做 = 重新复制；移动：撤销 = 移回原父目录，重做 = 再次移动。
/// 发生过"替换/合并"（ReplacedAny）时目标旧数据不可复原，整条操作不可撤销。
/// </summary>
public sealed class TransferOperation : IUndoableOperation
{
    private readonly IFileOperationService _fileOps;
    private readonly IReadOnlyList<TransferedItem> _items;
    private readonly string _targetDirectory;
    private readonly bool _move;

    public TransferOperation(
        IFileOperationService fileOps,
        IReadOnlyList<TransferedItem> items,
        string targetDirectory,
        bool move,
        string verb,
        bool replacedAny = false)
    {
        _fileOps = fileOps;
        _items = items;
        _targetDirectory = targetDirectory;
        _move = move;
        Verb = verb;
        ReplacedAny = replacedAny;
    }

    private string Verb { get; }
    private bool ReplacedAny { get; }

    public string Description => $"{Verb} {_items.Count} 个项目";
    public bool CanUndo => !ReplacedAny && _items.Count > 0;
    public bool CanRedo => _items.Count > 0;

    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_move)
        {
            // 按各源项的原父目录分组移回
            foreach (var group in _items.GroupBy(item => Path.GetDirectoryName(item.Source ?? string.Empty) ?? string.Empty))
            {
                var result = await _fileOps.MoveIntoAsync(
                    group.Select(item => item.Destination), group.Key,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                ThrowIfTotalFailure(result, "撤销移动");
            }
            return;
        }

        var existing = _items.Select(item => item.Destination)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();
        if (existing.Count == 0)
            return;
        var delete = await _fileOps.DeleteToRecycleBinAsync(existing, cancellationToken).ConfigureAwait(false);
        if (delete.DeletedCount == 0 && delete.HasErrors)
            throw new IOException($"撤销{Verb}失败：{string.Join("；", delete.Errors)}");
    }

    public async Task RedoAsync(CancellationToken cancellationToken = default)
    {
        var sources = _items.Select(item => item.Source).ToList();
        var result = _move
            ? await _fileOps.MoveIntoAsync(sources, _targetDirectory, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await _fileOps.CopyIntoAsync(sources, _targetDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        ThrowIfTotalFailure(result, "重做");
    }

    private static void ThrowIfTotalFailure(CopyResult result, string verb)
    {
        if (result.CopiedCount == 0 && result.HasErrors)
            throw new IOException($"{verb}失败：{string.Join("；", result.Errors)}");
    }
}

/// <summary>删除到回收站的操作。撤销 = 还原；重做 = 再次删除并刷新回收站条目（供再次撤销使用）。</summary>
public sealed class DeleteOperation : IUndoableOperation
{
    private readonly IFileOperationService _fileOps;
    private readonly IRecycleBinService _recycleBin;
    private readonly IReadOnlyList<string> _originalPaths;
    private IReadOnlyList<RecycleBinEntry> _entries;

    public DeleteOperation(
        IFileOperationService fileOps,
        IRecycleBinService recycleBin,
        IReadOnlyList<string> originalPaths,
        IReadOnlyList<RecycleBinEntry> entries)
    {
        _fileOps = fileOps;
        _recycleBin = recycleBin;
        _originalPaths = originalPaths;
        _entries = entries;
    }

    public string Description => $"删除 {_originalPaths.Count} 个项目";
    public bool CanUndo => _entries.Count > 0;
    public bool CanRedo => true;

    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        var restored = await _recycleBin.RestoreAsync(_entries, cancellationToken).ConfigureAwait(false);
        if (restored.Count == 0 && _entries.Count > 0)
            throw new IOException("撤销删除失败：无法从回收站还原");
    }

    public async Task RedoAsync(CancellationToken cancellationToken = default)
    {
        var existing = _originalPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();
        if (existing.Count == 0)
            return;

        var snapshot = _recycleBin.Snapshot();
        await _fileOps.DeleteToRecycleBinAsync(existing, cancellationToken).ConfigureAwait(false);
        _entries = await _recycleBin.DiffAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>重命名操作。</summary>
public sealed class RenameOperation : IUndoableOperation
{
    private readonly IFileOperationService _fileOps;
    private readonly string _oldPath;
    private readonly string _newPath;

    public RenameOperation(IFileOperationService fileOps, string oldPath, string newPath)
    {
        _fileOps = fileOps;
        _oldPath = oldPath;
        _newPath = newPath;
    }

    public string Description => $"重命名为 {Path.GetFileName(_newPath)}";
    public bool CanUndo => true;
    public bool CanRedo => true;

    public Task UndoAsync(CancellationToken cancellationToken = default) =>
        _fileOps.RenameAsync(_newPath, Path.GetFileName(_oldPath), cancellationToken);

    public Task RedoAsync(CancellationToken cancellationToken = default) =>
        _fileOps.RenameAsync(_oldPath, Path.GetFileName(_newPath), cancellationToken);
}

/// <summary>新建文件夹/文本文档的操作。撤销 = 永久删除（目录须为空）；重做 = 重新创建。</summary>
public sealed class CreateOperation : IUndoableOperation
{
    private readonly string _path;
    private readonly bool _isDirectory;

    public CreateOperation(string path, bool isDirectory)
    {
        _path = path;
        _isDirectory = isDirectory;
    }

    public string Description => $"新建{( _isDirectory ? "文件夹" : "文本文档")} {Path.GetFileName(_path)}";
    public bool CanUndo => true;
    public bool CanRedo => true;

    public Task UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDirectory)
        {
            if (!Directory.Exists(_path))
                return Task.CompletedTask;
            // 非空目录拒绝删除：里面的内容可能是撤销之前各步尚未回收的用户数据
            Directory.Delete(_path, recursive: false);
        }
        else if (File.Exists(_path))
        {
            File.Delete(_path);
        }
        return Task.CompletedTask;
    }

    public Task RedoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDirectory)
        {
            if (!Directory.Exists(_path))
                Directory.CreateDirectory(_path);
        }
        else if (!File.Exists(_path))
        {
            File.WriteAllText(_path, string.Empty);
        }
        return Task.CompletedTask;
    }
}

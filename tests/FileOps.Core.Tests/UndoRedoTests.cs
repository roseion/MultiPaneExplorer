using System.Collections.Concurrent;
using System.IO;
using FileOps.Core;
using Xunit;

namespace FileOps.Core.Tests;

/// <summary>撤销/重做：栈语义 + 四类操作记录的往返行为。</summary>
public sealed class UndoRedoTests : IDisposable
{
    private readonly FileOperationService _service = new();
    private readonly RecycleBinService _bin = new(); // 仅用于真实回收站的"删入→清理"闭环
    private readonly UndoRedoService _undoRedo = new();
    private readonly string _root;

    public UndoRedoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fileops-undo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFile(string relativePath, string content = "hello")
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // ---- UndoRedoService 栈语义 ----

    [Fact]
    public async Task PushThenUndoThenRedo_MovesOperationBetweenStacks()
    {
        var op = new FakeOperation("测试操作");
        _undoRedo.Push(op);

        Assert.True(_undoRedo.CanUndo);
        Assert.False(_undoRedo.CanRedo);
        Assert.Equal("测试操作", _undoRedo.UndoDescription);

        Assert.Equal("测试操作", await _undoRedo.UndoAsync());
        Assert.Equal(1, op.UndoCount);
        Assert.False(_undoRedo.CanUndo);
        Assert.True(_undoRedo.CanRedo);
        Assert.Equal("测试操作", _undoRedo.RedoDescription);

        Assert.Equal("测试操作", await _undoRedo.RedoAsync());
        Assert.Equal(1, op.RedoCount);
        Assert.True(_undoRedo.CanUndo);
        Assert.False(_undoRedo.CanRedo);
    }

    [Fact]
    public async Task Push_ClearsRedoStack()
    {
        _undoRedo.Push(new FakeOperation("一"));
        await _undoRedo.UndoAsync();
        Assert.True(_undoRedo.CanRedo);

        _undoRedo.Push(new FakeOperation("二"));

        Assert.False(_undoRedo.CanRedo);
        Assert.Equal("二", _undoRedo.UndoDescription);
    }

    [Fact]
    public async Task UndoAsync_WhenEmpty_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _undoRedo.UndoAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _undoRedo.RedoAsync());
    }

    [Fact]
    public async Task UndoFailure_KeepsOperationOnUndoStack()
    {
        var op = new FakeOperation("会失败", undoThrows: true);
        _undoRedo.Push(op);

        await Assert.ThrowsAsync<IOException>(() => _undoRedo.UndoAsync());

        Assert.True(_undoRedo.CanUndo); // 失败的操作留在撤销栈，可重试
        Assert.False(_undoRedo.CanRedo);
    }

    [Fact]
    public async Task Push_NonUndoableOperation_IsIgnored()
    {
        _undoRedo.Push(new FakeOperation("不可撤销", canUndo: false));

        Assert.False(_undoRedo.CanUndo);
    }

    [Fact]
    public void Push_RaisesChangedEvent()
    {
        var changed = 0;
        _undoRedo.Changed += () => changed++;

        _undoRedo.Push(new FakeOperation("一"));

        Assert.Equal(1, changed);
    }

    // ---- TransferOperation：复制（撤销=目标进回收站；重做=重新复制） ----

    [Fact]
    public async Task CopyOperation_UndoSendsToBin_RedoRestoresCopy()
    {
        var source = WriteFile("src/a.txt", "内容A");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var result = await _service.CopyIntoAsync([source], target);
        var operation = new TransferOperation(_service, result.Transferred!, target, move: false, "粘贴");
        _undoRedo.Push(operation);

        var binSnapshot = _bin.Snapshot();
        await _undoRedo.UndoAsync();
        Assert.False(File.Exists(Path.Combine(target, "a.txt")));

        // 清理真实回收站中撤销产生的条目，保证测试零残留
        var added = await _bin.DiffAsync(binSnapshot);
        var binEntry = Assert.Single(added, item => item.OriginalPath == Path.Combine(target, "a.txt"));
        await _bin.DeletePermanentlyAsync([binEntry]);

        await _undoRedo.RedoAsync();
        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
        Assert.Equal("内容A", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    // ---- TransferOperation：移动（撤销=移回原处；重做=再次移动） ----

    [Fact]
    public async Task MoveOperation_UndoMovesBack_RedoMovesAgain()
    {
        var source = WriteFile("src/moved.txt", "M");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var result = await _service.MoveIntoAsync([source], target);
        var operation = new TransferOperation(_service, result.Transferred!, target, move: true, "移动");
        _undoRedo.Push(operation);
        var destination = Path.Combine(target, "moved.txt");

        await _undoRedo.UndoAsync();
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));

        await _undoRedo.RedoAsync();
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void TransferOperation_WithReplacedConflict_CannotUndo()
    {
        var operation = new TransferOperation(_service,
            [new TransferedItem(@"C:\s\a.txt", @"C:\t\a.txt")], @"C:\t",
            move: false, "粘贴", replacedAny: true);

        Assert.False(operation.CanUndo);
    }

    // ---- DeleteOperation：撤销=从回收站还原；重做=再次删入回收站 ----

    [Fact]
    public async Task DeleteOperation_UndoRestores_RedofDeletesAgain_SecondUndoRestores()
    {
        var file = WriteFile("victim.txt", "珍贵数据");
        var snapshot = _bin.Snapshot();
        await _service.DeleteToRecycleBinAsync([file]);
        var entries = await _bin.DiffAsync(snapshot);
        Assert.Single(entries, item => item.OriginalPath == file);

        var operation = new DeleteOperation(_service, _bin, [file], entries);
        _undoRedo.Push(operation);

        await _undoRedo.UndoAsync();
        Assert.True(File.Exists(file));
        Assert.Equal("珍贵数据", File.ReadAllText(file));

        await _undoRedo.RedoAsync();
        Assert.False(File.Exists(file)); // 重新进入回收站，条目信息已刷新

        await _undoRedo.UndoAsync();
        Assert.True(File.Exists(file));  // 用重做时捕获的新条目再还原，测试零残留
    }

    // ---- RenameOperation / CreateOperation ----

    [Fact]
    public async Task RenameOperation_UndoRedoRoundTrip()
    {
        var file = WriteFile("old.txt", "R");

        var newPath = await _service.RenameAsync(file, "new.txt");
        _undoRedo.Push(new RenameOperation(_service, file, newPath));

        await _undoRedo.UndoAsync();
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(newPath));

        await _undoRedo.RedoAsync();
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public async Task CreateOperation_File_UndoDeletesRedoRecreates()
    {
        var created = await _service.CreateTextFileAsync(_root, "note.txt");
        _undoRedo.Push(new CreateOperation(created, isDirectory: false));

        await _undoRedo.UndoAsync();
        Assert.False(File.Exists(created));

        await _undoRedo.RedoAsync();
        Assert.True(File.Exists(created));
        Assert.Equal(string.Empty, File.ReadAllText(created));
    }

    [Fact]
    public async Task CreateOperation_EmptyDirectory_UndoDeletesRedoRecreates()
    {
        var created = await _service.CreateDirectoryAsync(_root, "新建文件夹");
        _undoRedo.Push(new CreateOperation(created, isDirectory: true));

        await _undoRedo.UndoAsync();
        Assert.False(Directory.Exists(created));

        await _undoRedo.RedoAsync();
        Assert.True(Directory.Exists(created));
    }

    [Fact]
    public async Task CreateOperation_DirectoryNoLongerEmpty_UndoFailsAndStays()
    {
        var created = await _service.CreateDirectoryAsync(_root, "非空文件夹");
        File.WriteAllText(Path.Combine(created, "user-file.txt"), "用户后来放进去的");
        _undoRedo.Push(new CreateOperation(created, isDirectory: true));

        await Assert.ThrowsAsync<IOException>(() => _undoRedo.UndoAsync());

        Assert.True(_undoRedo.CanUndo); // 目录非空拒绝撤销，操作留在栈上
        Assert.True(Directory.Exists(created));
    }

    // ---- 测试替身 ----

    /// <summary>Changed 事件供 UI 刷新按钮使用，必须回到调用 UndoAsync/RedoAsync 的线程，不能被 ConfigureAway。</summary>
    [Fact]
    public void UndoAsync_RaisesChangedOnCallerThread()
    {
        var service = new UndoRedoService();
        service.Push(new YieldingOperation());
        int? changedThreadId = null;
        service.Changed += () => changedThreadId = Environment.CurrentManagedThreadId;
        var callerThreadId = Environment.CurrentManagedThreadId;

        var context = new PumpingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = service.UndoAsync();
            context.RunUntil(task);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(callerThreadId, changedThreadId);
    }

    [Fact]
    public void RedoAsync_RaisesChangedOnCallerThread()
    {
        var service = new UndoRedoService();
        service.Push(new YieldingOperation());
        int? changedThreadId = null;
        service.Changed += () => changedThreadId = Environment.CurrentManagedThreadId;
        var callerThreadId = Environment.CurrentManagedThreadId;

        var context = new PumpingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var undoTask = service.UndoAsync();
            context.RunUntil(undoTask);

            var redoTask = service.RedoAsync();
            context.RunUntil(redoTask);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(callerThreadId, changedThreadId);
    }

    /// <summary>内部异步让出线程的操作（模拟真实 IO 回到线程池）。</summary>
    private sealed class YieldingOperation : IUndoableOperation
    {
        public string Description => "异步操作";
        public bool CanUndo => true;
        public bool CanRedo => true;

        public async Task UndoAsync(CancellationToken cancellationToken = default) => await Task.Yield();

        public async Task RedoAsync(CancellationToken cancellationToken = default) => await Task.Yield();
    }

    /// <summary>队列式同步上下文：Post 进队，由测试线程泵执行，模拟 UI 线程的消息循环。</summary>
    private sealed class PumpingSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        /// <summary>泵队列直到任务完成（内部 await 若 ConfigureAway，任务会在线程池完成，绕过本上下文）。</summary>
        public void RunUntil(Task task)
        {
            while (!task.IsCompleted)
            {
                if (!_queue.TryTake(out var item, TimeSpan.FromMilliseconds(1000)))
                    continue;
                item.Callback(item.State);
            }
        }
    }

    private sealed class FakeOperation(
        string description,
        bool canUndo = true,
        bool undoThrows = false) : IUndoableOperation
    {
        public string Description => description;
        public bool CanUndo { get; } = canUndo;
        public bool CanRedo => true;
        public int UndoCount { get; private set; }
        public int RedoCount { get; private set; }

        public Task UndoAsync(CancellationToken cancellationToken = default)
        {
            UndoCount++;
            return undoThrows ? Task.FromException(new IOException("模拟撤销失败")) : Task.CompletedTask;
        }

        public Task RedoAsync(CancellationToken cancellationToken = default)
        {
            RedoCount++;
            return Task.CompletedTask;
        }
    }
}

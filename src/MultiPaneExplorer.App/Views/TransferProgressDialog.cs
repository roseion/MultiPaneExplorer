using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileOps.Core;
using MultiPaneExplorer.App.Models;

namespace MultiPaneExplorer.App.Views;

/// <summary>
/// 非模态传输进度窗口：进度条、项目计数、已传字节、当前文件与取消按钮。
/// 传输 400ms 内结束则全程不显示（防闪烁）；关闭窗口等同取消传输。
/// </summary>
public sealed class TransferProgressDialog : Window
{
    private readonly CancellationTokenSource _cts;
    private readonly DispatcherTimer _showTimer;
    private readonly ProgressBar _bar = new() { Height = 6, Minimum = 0, Maximum = 100 };
    private readonly TextBlock _itemsText = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _bytesText = new() { Foreground = System.Windows.Media.Brushes.Gray };
    private readonly TextBlock _fileText = new()
    {
        Foreground = System.Windows.Media.Brushes.Gray,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 2, 0, 0),
    };
    private bool _shown;
    private bool _finished;

    public TransferProgressDialog(string verb, int totalItems, CancellationTokenSource cts)
    {
        _cts = cts;
        Title = $"正在{verb}…";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false; // 非模态弹出不抢主窗口焦点

        _itemsText.Text = $"共 {totalItems} 个项目";
        var cancelButton = new Button
        {
            Content = "取消",
            Width = 84,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        cancelButton.Click += (_, _) => _cts.Cancel();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children = { _itemsText, _bar, _bytesText, _fileText, cancelButton },
        };

        // 用户直接关窗口 = 取消传输；结束后由 Complete() 关闭，不触发取消
        Closing += (_, _) =>
        {
            if (!_finished)
                _cts.Cancel();
        };

        _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer.Stop();
            if (_finished)
                return;
            _shown = true;
            Show();
        };
    }

    /// <summary>启动延迟显示计时；操作提前结束则永远不显示。</summary>
    public void ShowDelayed() => _showTimer.Start();

    /// <summary>进度回报（Progress&lt;T&gt; 已封送回 UI 线程）。未显示时更新也无副作用，显示后即为最新值。</summary>
    public void Report(CopyProgress progress)
    {
        _bar.IsIndeterminate = progress.TotalBytes <= 0;
        _bar.Value = progress.Percent;
        _itemsText.Text = progress.TotalItems > 0
            ? $"已完成 {progress.DoneItems} / {progress.TotalItems} 个项目"
            : _itemsText.Text;
        _bytesText.Text = $"{FsEntry.FormatSize(progress.DoneBytes)} / {FsEntry.FormatSize(progress.TotalBytes)}";
        _fileText.Text = progress.CurrentFile;
    }

    /// <summary>传输结束（完成/取消/失败）：停表，已显示则关闭。</summary>
    public void Complete()
    {
        _finished = true;
        _showTimer.Stop();
        if (_shown)
            Close();
    }
}

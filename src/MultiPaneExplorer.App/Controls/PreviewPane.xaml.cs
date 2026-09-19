using System.IO;
using System.Windows.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MultiPaneExplorer.App.Models;

namespace MultiPaneExplorer.App.Controls;

/// <summary>
/// 预览窗格：图片（≤1024px 解码）/ 文本（白名单扩展名，前 256KB，含 0 字节判二进制）/
/// 其他文件（大图标+元数据）三种形态，目录显示信息卡。
/// 解码在后台线程执行，代次计数防止慢加载覆盖新选中项。
/// </summary>
public partial class PreviewPane : UserControl
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".ini", ".cfg", ".config", ".csv", ".tsv",
        ".bat", ".cmd", ".ps1", ".sh", ".cs", ".csproj", ".sln", ".slnx", ".slnf",
        ".htm", ".html", ".css", ".js", ".ts", ".py", ".yml", ".yaml", ".sql",
    };

    private const int MaxDecodePixels = 1024;
    private const int MaxTextBytes = 256 * 1024;

    private int _generation;

    public PreviewPane()
    {
        InitializeComponent();
    }

    /// <summary>显示条目；null 表示无选中（调用方通常保留上次内容，不调此方法）。</summary>
    public void Show(FsEntry? entry)
    {
        var generation = ++_generation;

        if (entry is null)
        {
            EmptyState.Visibility = Visibility.Visible;
            ContentRoot.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        ContentRoot.Visibility = Visibility.Collapsed; // 异步内容就绪后再整体显示，避免残留上一项内容闪烁
        HeaderIcon.Source = entry.Icon;
        TitleText.Text = entry.Name;
        MetaText.Text = DescribeMeta(entry);
        ImageHost.Visibility = Visibility.Collapsed;
        TextHost.Visibility = Visibility.Collapsed;
        InfoPanel.Visibility = Visibility.Collapsed;
        DrivePanel.Visibility = Visibility.Collapsed;

        // 驱动器根目录：环形容量卡（参考图 3 的 Storage 卡）
        if (entry.IsDirectory && IsDriveRoot(entry) && entry.DriveUsedFraction is { } fraction)
        {
            ShowDriveCard(entry, fraction);
            return;
        }

        if (entry.IsDirectory)
        {
            ShowInfo(entry, $"文件夹\n修改时间：{DescribeTime(entry.ModifiedTime)}");
            return;
        }

        var extension = Path.GetExtension(entry.Name);
        if (ImageExtensions.Contains(extension))
        {
            _ = LoadImageAsync(entry, generation);
        }
        else if (TextExtensions.Contains(extension))
        {
            _ = LoadTextAsync(entry, generation);
        }
        else
        {
            ShowInfo(entry, $"{entry.Type}\n大小：{FsEntry.FormatSize(entry.SizeBytes ?? 0)}\n修改时间：{DescribeTime(entry.ModifiedTime)}");
        }
    }

    /// <summary>清空预览（导航离开或窗格关闭时）。</summary>
    public void Clear()
    {
        _generation++;
        EmptyState.Visibility = Visibility.Visible;
        ContentRoot.Visibility = Visibility.Collapsed;
    }

    private void ShowInfo(FsEntry entry, string message)
    {
        InfoIcon.Source = entry.Icon;
        InfoText.Text = message;
        InfoPanel.Visibility = Visibility.Visible;
        ContentRoot.Visibility = Visibility.Visible;
    }

    /// <summary>判断条目是否为盘符根目录（驱动器环形卡触发条件）。</summary>
    private static bool IsDriveRoot(FsEntry entry)
    {
        var root = Path.GetPathRoot(entry.FullPath);
        return root is not null && string.Equals(
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            entry.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驱动器环形容量卡：圆环进度 + 百分比 + 盘名 + 容量文字。</summary>
    private void ShowDriveCard(FsEntry entry, double fraction)
    {
        DriveNameText.Text = entry.Name;
        DriveSpaceText.Text = entry.DriveInfoText;
        DonutPercent.Text = $"{Math.Round(fraction * 100)}%";
        DonutValue.Data = BuildArcGeometry(61, fraction);
        DrivePanel.Visibility = Visibility.Visible;
        ContentRoot.Visibility = Visibility.Visible;
    }

    /// <summary>构建圆环值弧：从正上方顺时针扫 fraction 圈。</summary>
    private static Geometry BuildArcGeometry(double radius, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.01, 1);
        var center = new Point(radius + 14, radius + 14); // 150 容器内留出线宽
        var start = new Point(center.X, center.Y - radius);
        var angle = fraction * 2 * Math.PI;
        var end = new Point(
            center.X + radius * Math.Sin(angle),
            center.Y - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static string DescribeMeta(FsEntry entry) => entry.IsDirectory
        ? $"文件夹 · 修改时间 {DescribeTime(entry.ModifiedTime)}"
        : $"{entry.Type} · {FsEntry.FormatSize(entry.SizeBytes ?? 0)} · 修改时间 {DescribeTime(entry.ModifiedTime)}";

    private static string DescribeTime(DateTime time) =>
        time == default ? "—" : time.ToString("yyyy-MM-dd HH:mm");

    private async Task LoadImageAsync(FsEntry entry, int generation)
    {
        var image = await Task.Run(() => DecodePreview(entry.FullPath)).ConfigureAwait(true);
        if (generation != _generation)
            return;
        if (image is null)
        {
            ShowInfo(entry, "无法预览此图片\n\n" + DescribeMeta(entry));
            return;
        }
        ImageHost.Source = image;
        ImageHost.Visibility = Visibility.Visible;
        ContentRoot.Visibility = Visibility.Visible;
    }

    private async Task LoadTextAsync(FsEntry entry, int generation)
    {
        var text = await Task.Run(() => ReadTextHead(entry.FullPath, MaxTextBytes)).ConfigureAwait(true);
        if (generation != _generation)
            return;
        if (text is null)
        {
            ShowInfo(entry, "无法以文本预览\n\n" + DescribeMeta(entry));
            return;
        }
        TextHost.Text = text;
        TextHost.Visibility = Visibility.Visible;
        ContentRoot.Visibility = Visibility.Visible;
    }

    private static BitmapImage? DecodePreview(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // 立即读完并释放文件句柄
            image.DecodePixelWidth = MaxDecodePixels;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // 损坏/不支持的图片回退信息卡
        }
    }

    /// <summary>读取文件头部文本；含 0 字节视为二进制返回 null。</summary>
    private static string? ReadTextHead(string path, int maxBytes)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(maxBytes, stream.Length)];
            if (buffer.Length == 0)
                return string.Empty;
            stream.ReadExactly(buffer);
            if (Array.IndexOf(buffer, (byte)0) >= 0)
                return null;
            var text = Encoding.UTF8.GetString(buffer);
            return stream.Length > maxBytes ? text + Environment.NewLine + Environment.NewLine + "…（已截断）" : text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

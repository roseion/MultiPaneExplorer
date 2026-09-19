using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiPaneExplorer.App.Models;

namespace MultiPaneExplorer.App.Views;

/// <summary>
/// 设置窗口（参考图 1 的卡片式布局）：外观（主题三态 + 强调色色板）、通用（开关）、关于。
/// 左侧导航 + 右侧内容卡，全部走 W11.* 主题 token，跟随深浅色。
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly StackPanel _contentHost;
    private readonly Dictionary<string, Border> _navItems = new();
    private readonly List<RadioButton> _themeRadios = [];
    private readonly List<Border> _accentDots = [];

    private static readonly (string Name, string Tag)[] LayoutOptions =
    [
        ("双栏", "Two"),
        ("三栏", "Three"),
        ("四栏（田字）", "FourGrid"),
        ("四栏（并排）", "FourColumns"),
    ];

    private static readonly (string Name, Color Light, Color Dark)[] AccentOptions =
    [
        ("蓝", Color.FromRgb(0x0A, 0x60, 0xFF), Color.FromRgb(0x40, 0x9C, 0xFF)),
        ("紫", Color.FromRgb(0x7B, 0x5C, 0xF5), Color.FromRgb(0xA6, 0x8B, 0xFF)),
        ("绿", Color.FromRgb(0x0E, 0x9F, 0x6E), Color.FromRgb(0x2C, 0xC5, 0x8C)),
        ("粉", Color.FromRgb(0xE5, 0x46, 0x7F), Color.FromRgb(0xF2, 0x6B, 0x9C)),
        ("橙", Color.FromRgb(0xF0, 0x79, 0x3C), Color.FromRgb(0xF7, 0x9B, 0x68)),
    ];

    public SettingsWindow()
    {
        _main = (MainWindow)Application.Current.MainWindow;
        Title = "多栏资源管理器 · 设置";
        Width = 880;
        Height = 600;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.BuildBackdropBrush();
        FontSize = 13;

        var nav = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        nav.Children.Add(MakeNavItem("外观", "appearance", "\uE790"));
        nav.Children.Add(MakeNavItem("通用", "general", "\uE713"));
        nav.Children.Add(MakeNavItem("关于", "about", "\uE946"));
        SelectNav("appearance");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nav.SetValue(Grid.ColumnProperty, 0);
        _contentHost = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        _contentHost.SetValue(Grid.ColumnProperty, 1);
        _contentHost.Children.Add(BuildAppearancePage());
        _contentHost.Children.Add(BuildGeneralPage());
        _contentHost.Children.Add(BuildAboutPage());
        layout.Children.Add(nav);
        layout.Children.Add(_contentHost);

        Content = layout;
        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(this);
        };
    }

    // ---- 导航 ----

    private Border MakeNavItem(string title, string key, string glyph)
    {
        var item = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = glyph,
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                    },
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        item.MouseLeftButtonDown += (_, _) => SelectNav(key);
        _navItems[key] = item;
        return item;
    }

    private void SelectNav(string key)
    {
        foreach (var pair in _navItems)
        {
            var active = pair.Key == key;
            pair.Value.Background = active
                ? Application.Current?.TryFindResource("W11.CardBackground") as Brush
                : Brushes.Transparent;
        }
        foreach (var child in _contentHost.Children.OfType<Border>())
            child.Visibility = (string)child.Tag == key ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 卡片工厂 ----

    private Border MakeCard(string title, string? subtitle, out StackPanel body)
    {
        body = new StackPanel();
        var card = new Border
        {
            Background = Application.Current?.TryFindResource("W11.CardBackground") as Brush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 14, 18, 16),
            Margin = new Thickness(0, 0, 0, 14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.10,
            },
            Child = body,
            Tag = title,
        };
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Application.Current?.TryFindResource("W11.TextPrimary") as Brush,
            Margin = new Thickness(0, 0, 0, subtitle is null ? 10 : 2),
        });
        if (subtitle is not null)
            body.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Foreground = Application.Current?.TryFindResource("W11.TextSecondary") as Brush,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
            });
        return card;
    }

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Application.Current?.TryFindResource("W11.TextSecondary") as Brush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- 外观页 ----

    private Border BuildAppearancePage()
    {
        var card = MakeCard("外观", "主题与强调色即时生效，并随会话记忆。", out var body);
        card.Tag = "appearance";

        body.Children.Add(MakeLabel("主题"));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 14) };
        foreach (var (value, title) in new[]
                 {
                     (ThemeManager.Light, "浅色"),
                     (ThemeManager.Dark, "深色"),
                     (ThemeManager.System, "跟随系统"),
                 })
        {
            var radio = new RadioButton
            {
                Content = title,
                GroupName = "settings-theme",
                Margin = new Thickness(0, 0, 22, 0),
                IsChecked = ThemeManager.SelectedTheme == value,
            };
            var target = value;
            radio.Click += (_, _) =>
            {
                ThemeManager.ApplySelected(target);
                _main.SaveSessionSnapshot();
                SyncAccentDots(); // 深浅切换后强调色显示色变化，刷新圆点颜色
            };
            _themeRadios.Add(radio);
            themeRow.Children.Add(radio);
        }
        body.Children.Add(themeRow);

        body.Children.Add(MakeLabel("强调色"));
        var dotsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        for (var i = 0; i < AccentOptions.Length; i++)
        {
            var index = i;
            var dot = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand,
                Tag = index,
            };
            dot.MouseLeftButtonDown += (_, _) =>
            {
                ThemeManager.ApplyAccent(index);
                _main.SaveSessionSnapshot();
                SyncAccentDots();
            };
            _accentDots.Add(dot);
            dotsRow.Children.Add(dot);
        }
        body.Children.Add(dotsRow);
        SyncAccentDots();
        return card;
    }

    private void SyncAccentDots()
    {
        for (var i = 0; i < _accentDots.Count; i++)
        {
            var dot = _accentDots[i];
            var (name, light, dark) = AccentOptions[i];
            dot.Background = new SolidColorBrush(
                ThemeManager.CurrentEffective == ThemeManager.Dark ? dark : light);
            dot.BorderThickness = ThemeManager.SelectedAccentIndex == i
                ? new Thickness(2)
                : new Thickness(0);
            dot.BorderBrush = Application.Current?.TryFindResource("W11.TextPrimary") as Brush ?? Brushes.Black;
            ToolTipService.SetToolTip(dot, name);
        }
    }

    // ---- 通用页 ----

    private Border BuildGeneralPage()
    {
        var card = MakeCard("通用", "以下开关即时生效。", out var body);
        card.Tag = "general";

        body.Children.Add(MakeToggle("显示隐藏文件", _main.ShowHidden, show => _main.SetShowHiddenFilesAll(show)));
        body.Children.Add(MakeToggle("预览窗格 (Alt+P)", _main.ShowPreview, show => _main.SetPreviewVisible(show)));
        body.Children.Add(MakeToggle("缩略图画廊（双栏时显示）", _main.GalleryEnabled, show => _main.SetGalleryEnabled(show)));
        return card;
    }

    private StackPanel MakeToggle(string label, bool value, Action<bool> onChanged)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => onChanged(box.IsChecked == true);
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    // ---- 关于页 ----

    private Border BuildAboutPage()
    {
        var card = MakeCard("关于", null, out var body);
        card.Tag = "about";

        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "2.1.0";
        body.Children.Add(new TextBlock
        {
            Text = $"多栏资源管理器 v{version}",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        body.Children.Add(new TextBlock
        {
            Text = "单窗口多栏文件管理器：2/3/4 窗格并列、多标签页、暗色主题、\n撤销/重做、回收站视图、缩略图与预览。",
            Foreground = Application.Current?.TryFindResource("W11.TextSecondary") as Brush,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var link = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        var hyperlink = new System.Windows.Documents.Hyperlink
        {
            Foreground = Application.Current?.TryFindResource("W11.Accent") as Brush,
        };
        hyperlink.Inlines.Add("github.com/roseion/MultiPaneExplorer");
        hyperlink.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/roseion/MultiPaneExplorer")
                {
                    UseShellExecute = true,
                });
            }
            catch
            {
                // 浏览器打不开时忽略
            }
        };
        link.Inlines.Add(hyperlink);
        body.Children.Add(link);
        return card;
    }
}

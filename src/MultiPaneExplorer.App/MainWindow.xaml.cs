using System.Windows;

namespace MultiPaneExplorer.App;

/// <summary>
/// 主窗口：迭代 1 为左右双栏，后续版本将支持 3/4 栏布局切换。
/// 初始路径在构造函数中通过 InitialPath 依赖属性下发，与 Loaded 触发顺序无关。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        LeftPane.InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        RightPane.InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Loaded += (_, _) => LeftPane.FocusList();
    }
}

using System.Windows;
using FileOps.Core;

namespace MultiPaneExplorer.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>提权辅助模式命令行开关：--elevated-op &lt;payload.json&gt;。</summary>
    public const string ElevatedOpSwitch = "--elevated-op";

    protected override void OnStartup(StartupEventArgs e)
    {
        // 提权辅助进程：无窗口，执行载荷、写结果文件、退出
        if (e.Args.Length == 2
            && string.Equals(e.Args[0], ElevatedOpSwitch, StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var payloadPath = e.Args[1];
            ElevatedOpResult result;
            try
            {
                result = ElevatedOpExecutor.Execute(ElevatedOpExecutor.LoadPayload(payloadPath));
            }
            catch (Exception ex)
            {
                result = new ElevatedOpResult { Errors = { ex.Message } };
            }
            ElevatedOpExecutor.SaveResult(result, payloadPath + ".result.json");
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}

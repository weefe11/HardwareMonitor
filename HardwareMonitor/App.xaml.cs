using System.Windows;

namespace HardwareMonitor;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (MainWindow != null) return;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        (MainWindow as MainWindow)?.StopForShutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (MainWindow as MainWindow)?.StopForShutdown();
        base.OnExit(e);
    }
}

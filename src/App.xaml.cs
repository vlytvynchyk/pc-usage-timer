using System.Linq;
using System.Windows;

namespace PcUsageTimer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep app running even when main window is hidden
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow();

        if (e.Args.Contains("--minimized"))
        {
            // Start minimized to tray — don't show window
        }
        else
        {
            mainWindow.Show();
        }
    }
}

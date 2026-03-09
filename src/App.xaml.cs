using System.Linq;
using System.Threading;
using System.Windows;

namespace PcUsageTimer;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "PcUsageTimer_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("PC Usage Timer is already running.",
                "PC Usage Timer", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

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

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

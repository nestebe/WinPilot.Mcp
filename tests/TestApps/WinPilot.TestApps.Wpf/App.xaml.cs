using System.Windows;

namespace WinPilot.TestApps.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = AppOptions.Parse(e.Args);
        if (options.ShowDelayMs > 0)
        {
            Thread.Sleep(options.ShowDelayMs);
        }

        var window = new global::WinPilot.TestApps.Wpf.MainWindow(options);
        MainWindow = window;
        window.Show();
    }
}

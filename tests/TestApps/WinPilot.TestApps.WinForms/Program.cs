namespace WinPilot.TestApps.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = AppOptions.Parse(args);
        if (options.ShowDelayMs > 0)
        {
            Thread.Sleep(options.ShowDelayMs);
        }

        Application.Run(new MainForm(options));
    }
}

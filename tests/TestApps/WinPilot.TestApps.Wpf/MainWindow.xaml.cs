using System.Windows;

namespace WinPilot.TestApps.Wpf;

public partial class MainWindow : Window
{
    private readonly int _hangMs;

    private bool _lateShown;

    internal MainWindow(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _hangMs = options.HangMs;
        InitializeComponent();
    }

    private void OnHelloClick(object sender, RoutedEventArgs e)
    {
        lblResult.Text = $"Hello {txtName.Text}";
    }

    private void OnHangClick(object sender, RoutedEventArgs e)
    {
        if (_hangMs > 0)
        {
            Thread.Sleep(_hangMs);
        }
    }

    private async void OnShowLaterClick(object sender, RoutedEventArgs e)
    {
        await Task.Delay(800);
        if (_lateShown)
        {
            return;
        }

        _lateShown = true;
        btnLate.Visibility = Visibility.Visible;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

using System.Globalization;

namespace WinPilot.TestApps.Wpf;

internal sealed class AppOptions
{
    public int HangMs { get; private set; }

    public int ShowDelayMs { get; private set; }

    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                continue;
            }

            if (string.Equals(args[i], "--hang-ms", StringComparison.Ordinal))
            {
                options.HangMs = value;
            }
            else if (string.Equals(args[i], "--show-delay-ms", StringComparison.Ordinal))
            {
                options.ShowDelayMs = value;
            }
        }

        return options;
    }
}

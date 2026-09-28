using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WinPilot.Automation;
using WinPilot.Automation.Configuration;
using WinPilot.Automation.Windows;

namespace WinPilot.IntegrationTests;

/// <summary>Owns a real engine plus one launched WinForms test app for the duration of a test.</summary>
public sealed class AutomationFixture : IAsyncDisposable
{
    private AutomationFixture(WindowsAutomationEngine engine, WindowInfo window)
    {
        Engine = engine;
        Window = window;
    }

    public WindowsAutomationEngine Engine { get; }

    public WindowInfo Window { get; }

    public static string TestAppPath { get; } = ResolveTestAppPath();

    public static async Task<AutomationFixture> StartAsync(
        Action<WinPilotOptions>? configure = null,
        int hangMs = 0,
        int showDelayMs = 0)
    {
        var options = new WinPilotOptions();
        configure?.Invoke(options);
        options.Validate();

        var engine = new WindowsAutomationEngine(Options.Create(options), NullLoggerFactory.Instance);

        var args = new List<string>();
        if (hangMs > 0)
        {
            args.AddRange(["--hang-ms", hangMs.ToString(CultureInfo.InvariantCulture)]);
        }

        if (showDelayMs > 0)
        {
            args.AddRange(["--show-delay-ms", showDelayMs.ToString(CultureInfo.InvariantCulture)]);
        }

        try
        {
            var window = await engine.LaunchAsync(TestAppPath, args, timeoutMs: 20_000, CancellationToken.None);
            return new AutomationFixture(engine, window);
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await Engine.DisposeAsync();

    private static string ResolveTestAppPath()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutput.Parent!.Name; // bin/<Configuration>/<tfm>/
        var repoRoot = testOutput.Parent!.Parent!.Parent!.Parent!.Parent!;
        var exe = Path.Combine(
            repoRoot.FullName,
            "tests", "TestApps", "WinPilot.TestApps.WinForms", "bin", configuration, "net10.0-windows",
            "WinPilot.TestApps.WinForms.exe");

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException(
                $"The WinForms test app was not built ({exe}). Build tests/TestApps/WinPilot.TestApps.WinForms first.",
                exe);
        }

        return exe;
    }
}

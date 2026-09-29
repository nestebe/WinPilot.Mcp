using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using WinPilot.Automation;
using WinPilot.Automation.Configuration;
using WinPilot.Mcp;
using WinPilot.Mcp.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// WINPILOT_* environment variables (SNAKE_CASE) become configuration keys (PascalCase).
builder.Configuration.AddInMemoryCollection(OptionsBinding.TranslateEnvironment(
    Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .Select(entry => new KeyValuePair<string, string?>((string)entry.Key, (string?)entry.Value))));

var winPilotOptions = new WinPilotOptions();
builder.Configuration.GetSection(WinPilotOptions.SectionName).Bind(winPilotOptions);
builder.Configuration.Bind(winPilotOptions); // top-level keys from WINPILOT_* variables win over appsettings
try
{
    winPilotOptions.Validate();
}
catch (ArgumentOutOfRangeException exception)
{
    Console.Error.WriteLine($"WinPilot: invalid configuration: {exception.Message}");
    return 1;
}

builder.Services.AddSingleton(Options.Create(winPilotOptions));
builder.Services.AddSingleton<IWindowsAutomation>(services => new WindowsAutomationEngine(
    services.GetRequiredService<IOptions<WinPilotOptions>>(),
    services.GetRequiredService<ILoggerFactory>()));

builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
if (builder.Configuration["LogLevel"] is { Length: > 0 } levelText
    && Enum.TryParse<LogLevel>(levelText, ignoreCase: true, out var minimumLevel))
{
    builder.Logging.SetMinimumLevel(minimumLevel);
}

if (builder.Configuration["LogFile"] is { Length: > 0 } logFile)
{
    try
    {
        builder.Logging.AddProvider(new FileLoggerProvider(logFile));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
    {
        Console.Error.WriteLine($"WinPilot: file logging disabled ({exception.Message}).");
    }
}

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "winpilot",
            Title = "WinPilot — Windows Desktop Automation",
            Version = McpServerAssembly.InformationalVersion,
        };
        options.ServerInstructions =
            "Use windows_snapshot to get the accessibility tree with element refs (w1e5). Act on refs with " +
            "windows_click/type/fill/send_keys/get_text. Refs are invalidated by the next snapshot of that window; " +
            "run windows_snapshot again to refresh. Window-level tools (windows_list_windows, windows_focus, " +
            "windows_close, windows_launch) keep working while a UI Automation call is busy.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

DpiInitialization.EnablePerMonitorV2();

await builder.Build().RunAsync();
return 0;

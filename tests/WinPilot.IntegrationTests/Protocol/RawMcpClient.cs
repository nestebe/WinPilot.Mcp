using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Xunit;

namespace WinPilot.IntegrationTests.Protocol;

/// <summary>
/// Spawns the real server over stdio and speaks raw JSON-RPC lines, recording every stdout line
/// for protocol-purity assertions. Used for the anti-disconnect regression suite.
/// </summary>
internal sealed class RawMcpClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
    private readonly List<string> _stdoutLines = [];
    private readonly List<JsonDocument> _pending = [];
    private readonly StringBuilder _stderr = new();
    private readonly object _gate = new();
    private bool _disposed;

    private RawMcpClient(Process process)
    {
        _process = process;
        _ = Task.Run(ReadStdoutLoopAsync);
        _ = Task.Run(ReadStderrLoopAsync);
    }

    /// <summary>Every stdout line the server produced, in order.</summary>
    public IReadOnlyList<string> StdoutLines
    {
        get
        {
            lock (_gate)
            {
                return [.. _stdoutLines];
            }
        }
    }

    public static RawMcpClient Start()
    {
        var dll = ResolveServerDll();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = $"\"{dll}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["WINPILOT_LOG_LEVEL"] = "Warning";

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the server process.");
        return new RawMcpClient(process);
    }

    public async Task InitializeAsync(TimeSpan? timeout = null)
    {
        await SendRawAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw-test","version":"1"}}}""");
        await SendRawAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        var response = await WaitForResponseAsync(1, timeout ?? TimeSpan.FromSeconds(5));
        Assert.NotNull(response);
        Assert.Equal("winpilot", response.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    public async Task SendRawAsync(string json)
    {
        await _process.StandardInput.WriteLineAsync(json);
        await _process.StandardInput.FlushAsync();
    }

    public async Task SendRequestAsync(int id, string method, object? parameters = null)
    {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await SendRawAsync(request);
    }

    /// <summary>Reads lines until the response with <paramref name="id"/> arrives; null on timeout.</summary>
    public async Task<JsonDocument?> WaitForResponseAsync(int id, TimeSpan timeout)
    {
        lock (_gate)
        {
            var buffered = _pending.Find(document => IsResponseFor(document, id));
            if (buffered is not null)
            {
                _pending.Remove(buffered);
                return buffered;
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var line = await ReadLineAsync(deadline - DateTime.UtcNow);
            if (line is null)
            {
                return null;
            }

            var document = JsonDocument.Parse(line);
            if (IsResponseFor(document, id))
            {
                return document;
            }

            lock (_gate)
            {
                _pending.Add(document); // responses may arrive out of order; keep them for later matches
            }
        }

        return null;
    }

    private static bool IsResponseFor(JsonDocument document, int id)
        => document.RootElement.TryGetProperty("id", out var responseId)
           && responseId.ValueKind == JsonValueKind.Number
           && responseId.GetInt32() == id;

    public async Task<string?> ReadLineAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await _incoming.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public string StderrText
    {
        get
        {
            lock (_gate)
            {
                return _stderr.ToString();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }

        if (!await WaitForExitAsync(TimeSpan.FromSeconds(5)))
        {
            _process.Kill(entireProcessTree: true);
        }

        lock (_gate)
        {
            foreach (var document in _pending)
            {
                document.Dispose();
            }

            _pending.Clear();
        }

        _process.Dispose();
    }

    private async Task ReadStdoutLoopAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            lock (_gate)
            {
                _stdoutLines.Add(line);
            }

            _incoming.Writer.TryWrite(line);
        }

        _incoming.Writer.TryComplete();
    }

    private async Task ReadStderrLoopAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line)
        {
            lock (_gate)
            {
                if (_stderr.Length < 64_000)
                {
                    _stderr.AppendLine(line);
                }
            }
        }
    }

    private static string ResolveServerDll()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutput.Parent!.Name;
        var repoRoot = testOutput.Parent!.Parent!.Parent!.Parent!.Parent!;
        var dll = Path.Combine(
            repoRoot.FullName,
            "src", "WinPilot.Mcp", "bin", configuration, "net10.0-windows", "winpilot-mcp.dll");

        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"The MCP server was not built ({dll}).", dll);
        }

        return dll;
    }
}

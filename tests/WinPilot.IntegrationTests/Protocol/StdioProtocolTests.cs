using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace WinPilot.IntegrationTests.Protocol;

/// <summary>
/// The anti-disconnect regression suite: the server must answer pings during long operations,
/// honor cancellation, survive error storms, keep stdout protocol-pure, and shut down cleanly.
/// </summary>
public class StdioProtocolTests
{
    [Fact]
    public async Task Initialize_handshake_completes_under_5s()
    {
        await using var client = RawMcpClient.Start();
        var stopwatch = Stopwatch.StartNew();

        await client.InitializeAsync();

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"handshake took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Ping_is_answered_within_2s_while_a_long_tool_call_is_running()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        await client.SendRequestAsync(2, "tools/call", new
        {
            name = "windows_batch",
            arguments = new { actions = new[] { new { action = "wait", ms = 4000 } } },
        });
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await client.SendRequestAsync(99, "ping");
        var ping = await client.WaitForResponseAsync(99, TimeSpan.FromSeconds(2));

        Assert.NotNull(ping);
        var batch = await client.WaitForResponseAsync(2, TimeSpan.FromSeconds(6));
        Assert.NotNull(batch);
        Assert.Contains("Waited 4000ms", batch.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_tool_is_stopped_and_the_server_stays_healthy()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        await client.SendRequestAsync(3, "tools/call", new
        {
            name = "windows_batch",
            arguments = new { actions = new[] { new { action = "wait", ms = 10_000 } } },
        });
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await client.SendRawAsync("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3,"reason":"test"}}""");

        // Per MCP, a cancelled request gets no response. Proof of cancellation: the next call
        // is not stuck behind the 10 s wait (the engine serializes tool operations).
        await client.SendRequestAsync(4, "tools/call", new
        {
            name = "windows_batch",
            arguments = new { actions = new[] { new { action = "wait", ms = 100 } } },
        });
        var followUp = await client.WaitForResponseAsync(4, TimeSpan.FromSeconds(3));

        Assert.NotNull(followUp);
        Assert.Contains("Waited 100ms", followUp.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Twenty_consecutive_tool_errors_do_not_kill_the_server()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        for (var i = 0; i < 20; i++)
        {
            await client.SendRequestAsync(100 + i, "tools/call", new
            {
                name = "windows_get_text",
                arguments = new { elementRef = "w99e99" },
            });
        }

        for (var i = 0; i < 20; i++)
        {
            var response = await client.WaitForResponseAsync(100 + i, TimeSpan.FromSeconds(5));
            Assert.NotNull(response);
            Assert.True(IsError(response), $"call {i} should have failed");
        }

        await client.SendRequestAsync(97, "ping");
        Assert.NotNull(await client.WaitForResponseAsync(97, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Every_stdout_line_is_valid_jsonrpc_during_a_full_workflow()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        await client.SendRequestAsync(10, "tools/list");
        Assert.NotNull(await client.WaitForResponseAsync(10, TimeSpan.FromSeconds(5)));

        await client.SendRequestAsync(11, "tools/call", new { name = "windows_list_windows", arguments = new { } });
        Assert.NotNull(await client.WaitForResponseAsync(11, TimeSpan.FromSeconds(5)));

        await client.SendRequestAsync(12, "tools/call", new
        {
            name = "windows_batch",
            arguments = new { actions = new[] { new { action = "wait", ms = 100 } } },
        });
        Assert.NotNull(await client.WaitForResponseAsync(12, TimeSpan.FromSeconds(5)));

        Assert.NotEmpty(client.StdoutLines);
        foreach (var line in client.StdoutLines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        }
    }

    [Fact]
    public async Task Concurrent_tool_calls_are_serialized_and_both_succeed()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        await client.SendRequestAsync(20, "tools/call", new { name = "windows_list_windows", arguments = new { } });
        await client.SendRequestAsync(21, "tools/call", new { name = "windows_list_windows", arguments = new { } });

        var first = await client.WaitForResponseAsync(20, TimeSpan.FromSeconds(5));
        var second = await client.WaitForResponseAsync(21, TimeSpan.FromSeconds(5));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.False(IsError(first));
        Assert.False(IsError(second));
    }

    [Fact]
    public async Task Tool_list_matches_the_committed_schema_snapshot()
    {
        await using var client = RawMcpClient.Start();
        await client.InitializeAsync();

        await client.SendRequestAsync(30, "tools/list");
        var response = await client.WaitForResponseAsync(30, TimeSpan.FromSeconds(5));
        Assert.NotNull(response);

        var tools = JsonNode.Parse(response.RootElement.GetProperty("result").GetRawText())!["tools"]!.AsArray();
        var normalized = new JsonArray(
            tools
                .Select(tool => new JsonObject
                {
                    ["name"] = tool!["name"]!.GetValue<string>(),
                    ["inputSchema"] = tool["inputSchema"]!.DeepClone(),
                })
                .OrderBy(tool => tool!["name"]!.GetValue<string>(), StringComparer.Ordinal)
                .ToArray());
        var json = new JsonObject { ["tools"] = normalized }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .ReplaceLineEndings("\n");

        var snapshotPath = Path.Combine(
            new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.Parent!.FullName,
            "tests", "WinPilot.IntegrationTests", "Protocol", "tool-schemas.snap.json");

        if (!File.Exists(snapshotPath))
        {
            await File.WriteAllTextAsync(snapshotPath, json + "\n", TestContext.Current.CancellationToken);
            Assert.Fail($"Schema snapshot created at {snapshotPath}; review and commit it.");
        }

        var committed = (await File.ReadAllTextAsync(snapshotPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.Equal(committed, json + "\n");
    }

    [Fact]
    public async Task Graceful_shutdown_closes_launched_apps()
    {
        TestEnvironment.RequireInteractiveDesktop();

        var before = CurrentAppProcessIds();
        var client = RawMcpClient.Start();
        try
        {
            await client.InitializeAsync();
            await client.SendRequestAsync(40, "tools/call", new
            {
                name = "windows_launch",
                arguments = new { app = AutomationFixture.TestAppPath },
            });
            var launch = await client.WaitForResponseAsync(40, TimeSpan.FromSeconds(20));
            Assert.NotNull(launch);
            Assert.Contains("Window handle:", launch.RootElement.GetRawText(), StringComparison.Ordinal);

            var after = CurrentAppProcessIds();
            var launched = after.Except(before).ToArray();
            Assert.NotEmpty(launched);

            await client.DisposeAsync();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && launched.Any(IsAlive))
            {
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }

            Assert.All(launched, pid => Assert.False(IsAlive(pid), $"test app process {pid} survived server shutdown"));
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static bool IsError(JsonDocument response)
        => response.RootElement.GetProperty("result").TryGetProperty("isError", out var isError)
           && isError.ValueKind == JsonValueKind.True;

    private static int[] CurrentAppProcessIds()
        => [.. Process.GetProcessesByName("WinPilot.TestApps.WinForms").Select(process => process.Id)];

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

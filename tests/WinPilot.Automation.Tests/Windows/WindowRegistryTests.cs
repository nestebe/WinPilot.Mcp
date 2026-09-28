using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Tests.Windows;

public class WindowRegistryTests
{
    [Fact]
    public void Same_hwnd_returns_the_same_handle_and_refreshes_metadata()
    {
        var registry = new WindowRegistry();
        var hwnd = new IntPtr(0x100);

        var first = registry.RegisterOrGet(hwnd, "Old title", 42, "app");
        var second = registry.RegisterOrGet(hwnd, "New title", 42, "app");

        Assert.Equal("w1", first.Handle);
        Assert.Equal("w1", second.Handle);
        Assert.Equal("New title", second.Title);
        Assert.Equal(hwnd, second.Hwnd);
        Assert.Equal(42, second.ProcessId);
        Assert.Equal("app", second.ProcessName);
    }

    [Fact]
    public void Distinct_hwnds_get_increasing_handles()
    {
        var registry = new WindowRegistry();

        var first = registry.RegisterOrGet(new IntPtr(1), "A", 10, null);
        var second = registry.RegisterOrGet(new IntPtr(2), "B", 11, null);
        var third = registry.RegisterOrGet(new IntPtr(3), "C", 12, null);

        Assert.Equal(["w1", "w2", "w3"], new[] { first.Handle, second.Handle, third.Handle });
    }

    [Fact]
    public void Prune_removes_dead_windows_and_get_returns_null_afterwards()
    {
        var registry = new WindowRegistry();
        var alive = new IntPtr(1);
        var dead = new IntPtr(2);
        var aliveInfo = registry.RegisterOrGet(alive, "A", 10, null);
        var deadInfo = registry.RegisterOrGet(dead, "B", 11, null);

        registry.Prune(hwnd => hwnd == alive);

        Assert.Equal(aliveInfo, registry.Get(aliveInfo.Handle));
        Assert.Null(registry.Get(deadInfo.Handle));
        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public void Get_returns_null_for_unknown_handles()
        => Assert.Null(new WindowRegistry().Get("w99"));

    [Fact]
    public void Concurrent_registration_and_reads_are_safe()
    {
        var registry = new WindowRegistry();
        var hwnds = Enumerable.Range(1, 16).Select(i => new IntPtr(i)).ToArray();

        Parallel.For(0, 64, i =>
        {
            var hwnd = hwnds[i % hwnds.Length];
            _ = registry.RegisterOrGet(hwnd, $"T{int.Min(i, hwnds.Length)}", 100 + (i % hwnds.Length), null);
            _ = registry.Snapshot();
        });

        Assert.Equal(hwnds.Length, registry.Snapshot().Count);
    }
}

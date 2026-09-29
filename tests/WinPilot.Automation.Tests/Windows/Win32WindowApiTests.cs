using WinPilot.Automation.Windows;

namespace WinPilot.Automation.Tests.Windows;

public class Win32WindowApiTests
{
    [Fact]
    public void Own_process_is_not_interaction_blocked()
        => Assert.False(Win32WindowApi.IsInteractionBlocked(Environment.ProcessId));

    [Fact]
    public void Unknown_process_is_interaction_blocked()
        => Assert.True(Win32WindowApi.IsInteractionBlocked(int.MaxValue));
}

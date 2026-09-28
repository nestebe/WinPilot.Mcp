using WinPilot.Automation.Capture;
using Xunit;

namespace WinPilot.IntegrationTests;

public class InteractionIntegrationTests
{
    [Fact]
    public async Task Fill_click_and_get_text_round_trip()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var snapshot = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);

        var nameRef = TestHelpers.FindRef(snapshot, "[txtName]");
        var helloRef = TestHelpers.FindRef(snapshot, "Hello");

        await fixture.Engine.FillAsync(nameRef, "Ada", CancellationToken.None);
        await fixture.Engine.ClickAsync(helloRef, "left", false, CancellationToken.None);

        var after = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var labelRef = TestHelpers.FindRef(after, "Hello Ada");

        Assert.Equal("Hello Ada", await fixture.Engine.GetTextAsync(labelRef, CancellationToken.None));
    }

    [Fact]
    public async Task Fill_supports_non_ascii_text()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var snapshot = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var nameRef = TestHelpers.FindRef(snapshot, "[txtName]");
        const string text = "héllo — 你好 🚀";

        await fixture.Engine.FillAsync(nameRef, text, CancellationToken.None);

        Assert.Equal(text, await fixture.Engine.GetTextAsync(nameRef, CancellationToken.None));
    }

    [Fact]
    public async Task Send_keys_and_type_use_the_keyboard_path()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var snapshot = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var nameRef = TestHelpers.FindRef(snapshot, "[txtName]");

        await fixture.Engine.FillAsync(nameRef, "replace-me", CancellationToken.None);
        await fixture.Engine.SendKeysAsync(nameRef, "Ctrl+A", null, CancellationToken.None);
        await fixture.Engine.TypeAsync(nameRef, "typed", submit: false, CancellationToken.None);

        Assert.Equal("typed", await fixture.Engine.GetTextAsync(nameRef, CancellationToken.None));
    }

    [Fact]
    public async Task Stale_refs_are_re_resolved_once_and_the_operation_succeeds()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var first = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var helloRef = TestHelpers.FindRef(first, "Hello");

        _ = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None); // ages helloRef

        var message = await fixture.Engine.ClickAsync(helloRef, "left", false, CancellationToken.None);

        Assert.Contains("Invoked Hello", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wait_for_finds_a_button_that_appears_later()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var snapshot = await fixture.Engine.SnapshotAsync(fixture.Window.Handle, null, CancellationToken.None);
        var showLaterRef = TestHelpers.FindRef(snapshot, "Show Later");

        await fixture.Engine.ClickAsync(showLaterRef, "left", false, CancellationToken.None);

        var info = await fixture.Engine.WaitForElementAsync(
            fixture.Window.Handle,
            elementRef: null,
            new WinPilot.Automation.Elements.ElementSelector("Late", null, null),
            timeoutMs: 5_000,
            CancellationToken.None);

        Assert.Contains("Late", info.Line, StringComparison.Ordinal);
        Assert.Matches("""w\d+e\d+""", info.Ref);
    }

    [Fact]
    public async Task Screenshot_returns_png_bytes_and_saves_when_asked()
    {
        TestEnvironment.RequireInteractiveDesktop();
        await using var fixture = await AutomationFixture.StartAsync();
        var path = Path.Combine(Path.GetTempPath(), $"winpilot-it-{Guid.NewGuid():N}.png");

        try
        {
            var result = await fixture.Engine.CaptureAsync(
                new CaptureRequest(fixture.Window.Handle, null, false, false, path, false),
                CancellationToken.None);

            Assert.True(result.Png.Length > 1000, $"expected a real screenshot, got {result.Png.Length} bytes");
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], result.Png[..4]);
            Assert.Equal(path, result.SavedPath);
            Assert.Equal(result.Png, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using WinPilot.Automation.Elements;

namespace WinPilot.Automation.Tests.Elements;

public class ElementRegistryTests
{
    private sealed record FakeElement(string Name);

    private static readonly ElementFingerprint Fingerprint = new("btnHello", "Hello", "Button", "42-1");

    [Fact]
    public void Registers_sequential_refs_per_window_and_resets_counter_per_snapshot()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");

        var first = registry.Register("w1", new FakeElement("a"), Fingerprint);
        var second = registry.Register("w1", new FakeElement("b"), Fingerprint);

        Assert.Equal("w1e1", first);
        Assert.Equal("w1e2", second);

        registry.BeginSnapshot("w1");

        Assert.Equal("w1e1", registry.Register("w1", new FakeElement("c"), Fingerprint));
    }

    [Fact]
    public void Find_returns_found_for_the_current_snapshot()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");
        var element = new FakeElement("a");
        var refId = registry.Register("w1", element, Fingerprint);

        var lookup = registry.Find(refId);

        Assert.Equal(ElementLookupStatus.Found, lookup.Status);
        Assert.Same(element, lookup.Entry!.Element);
        Assert.Equal(Fingerprint, lookup.Entry.Fingerprint);
    }

    [Fact]
    public void Find_after_a_new_snapshot_reports_stale_with_the_original_fingerprint()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");
        var refId = registry.Register("w1", new FakeElement("a"), Fingerprint);

        registry.BeginSnapshot("w1");

        var lookup = registry.Find(refId);

        Assert.Equal(ElementLookupStatus.Stale, lookup.Status);
        Assert.Equal(Fingerprint, lookup.Entry!.Fingerprint);
    }

    [Fact]
    public void Find_reports_not_found_for_unknown_or_malformed_refs()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");
        _ = registry.Register("w1", new FakeElement("a"), Fingerprint);

        Assert.Equal(ElementLookupStatus.NotFound, registry.Find("w1e9").Status);
        Assert.Equal(ElementLookupStatus.NotFound, registry.Find("w2e1").Status);
        Assert.Equal(ElementLookupStatus.NotFound, registry.Find("bogus").Status);
    }

    [Fact]
    public void Entries_two_versions_old_are_pruned()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");
        var oldest = registry.Register("w1", new FakeElement("a"), Fingerprint);       // w1e1 v1
        var oldSecond = registry.Register("w1", new FakeElement("b"), Fingerprint);    // w1e2 v1
        registry.BeginSnapshot("w1");
        var previous = registry.Register("w1", new FakeElement("c"), Fingerprint);     // w1e1 v2
        registry.BeginSnapshot("w1");                                                   // v3 prunes v1

        Assert.Equal("w1e1", oldest);
        Assert.Equal(ElementLookupStatus.Stale, registry.Find(previous).Status);        // v2 retained for stale detection
        Assert.Equal(ElementLookupStatus.NotFound, registry.Find(oldSecond).Status);    // v1 pruned
    }

    [Fact]
    public void Windows_have_independent_versions_and_counters()
    {
        var registry = new ElementRegistry<FakeElement>();
        registry.BeginSnapshot("w1");
        registry.BeginSnapshot("w2");
        var w1Ref = registry.Register("w1", new FakeElement("a"), Fingerprint);
        var w2Ref = registry.Register("w2", new FakeElement("b"), Fingerprint);

        registry.BeginSnapshot("w2"); // only w2 ages

        Assert.Equal(ElementLookupStatus.Found, registry.Find(w1Ref).Status);
        Assert.Equal(ElementLookupStatus.Stale, registry.Find(w2Ref).Status);
    }
}

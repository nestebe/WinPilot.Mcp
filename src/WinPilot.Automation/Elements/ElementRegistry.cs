namespace WinPilot.Automation.Elements;

/// <summary>Result classification of an element reference lookup.</summary>
internal enum ElementLookupStatus
{
    /// <summary>The reference resolves to an element of the current snapshot.</summary>
    Found,

    /// <summary>The reference existed but belongs to a previous snapshot; re-resolution is possible.</summary>
    Stale,

    /// <summary>The reference was never registered (or its window is unknown).</summary>
    NotFound,
}

/// <summary>A registered element reference and everything needed to re-resolve it.</summary>
internal sealed record RegisteredElement<TElement>(
    string Ref,
    TElement Element,
    ElementFingerprint Fingerprint,
    string WindowHandle,
    int SnapshotVersion)
    where TElement : class;

/// <summary>Outcome of an element reference lookup.</summary>
internal readonly record struct ElementLookup<TElement>(ElementLookupStatus Status, RegisteredElement<TElement>? Entry)
    where TElement : class;

/// <summary>
/// Maps element references to elements for one snapshot generation per window. Confined to the
/// UI Automation worker thread. Entries of one previous snapshot are retained so a stale reference
/// can be distinguished from an unknown one and re-resolved by fingerprint.
/// </summary>
internal sealed class ElementRegistry<TElement>
    where TElement : class
{
    private readonly Dictionary<string, RegisteredElement<TElement>> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    /// <summary>Starts a new snapshot for a window: resets its reference counter and ages its entries.</summary>
    public int BeginSnapshot(string windowHandle)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowHandle);

        var version = _versions.GetValueOrDefault(windowHandle) + 1;
        _versions[windowHandle] = version;
        _counters[windowHandle] = 0;

        var pruneBefore = version - 1;
        foreach (var key in _elements
                     .Where(pair => pair.Value.WindowHandle == windowHandle && pair.Value.SnapshotVersion < pruneBefore)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _elements.Remove(key);
        }

        return version;
    }

    /// <summary>
    /// Registers an element into the current snapshot, starting a snapshot when the window none yet.
    /// Used by wait-for lookups so refs coexist with the previous snapshot's refs.
    /// </summary>
    public string RegisterPreservingSnapshot(string windowHandle, TElement element, ElementFingerprint fingerprint)
    {
        if (_versions.GetValueOrDefault(windowHandle) == 0)
        {
            BeginSnapshot(windowHandle);
        }

        return Register(windowHandle, element, fingerprint);
    }

    /// <summary>Registers an element in the current snapshot and returns its reference.</summary>
    public string Register(string windowHandle, TElement element, ElementFingerprint fingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowHandle);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var version = _versions.GetValueOrDefault(windowHandle);
        if (version == 0)
        {
            throw new InvalidOperationException($"BeginSnapshot must be called for '{windowHandle}' before registering elements.");
        }

        var index = _counters.GetValueOrDefault(windowHandle) + 1;
        _counters[windowHandle] = index;

        var refId = ElementRef.For(windowHandle, index);
        _elements[refId] = new RegisteredElement<TElement>(refId, element, fingerprint, windowHandle, version);
        return refId;
    }

    /// <summary>Looks up a reference and classifies it as found, stale, or unknown.</summary>
    public ElementLookup<TElement> Find(string elementRef)
    {
        if (!ElementRef.TryParse(elementRef, out var windowHandle, out _) || !_elements.TryGetValue(elementRef, out var entry))
        {
            return new ElementLookup<TElement>(ElementLookupStatus.NotFound, null);
        }

        return entry.SnapshotVersion == _versions.GetValueOrDefault(windowHandle)
            ? new ElementLookup<TElement>(ElementLookupStatus.Found, entry)
            : new ElementLookup<TElement>(ElementLookupStatus.Stale, entry);
    }
}

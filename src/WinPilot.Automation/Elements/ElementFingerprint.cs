using FlaUI.Core.AutomationElements;

namespace WinPilot.Automation.Elements;

/// <summary>
/// A cheap identity fingerprint of an element, captured at snapshot time and used to re-resolve
/// the element after the UI has been recreated.
/// </summary>
internal sealed record ElementFingerprint(string? AutomationId, string? Name, string ControlTypeName, string? RuntimeId)
{
    /// <summary>Captures the fingerprint of a live element; individual property failures degrade to null.</summary>
    public static ElementFingerprint From(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new ElementFingerprint(
            TryGet(() => element.Properties.AutomationId.ValueOrDefault),
            TryGet(() => element.Properties.Name.ValueOrDefault),
            TryGet(() => element.Properties.ControlType.ValueOrDefault.ToString()) ?? "Unknown",
            TryGet(() =>
            {
                var runtimeId = element.Properties.RuntimeId.ValueOrDefault;
                return runtimeId is { Length: > 0 } ? string.Join("-", runtimeId) : null;
            }));
    }

    private static T? TryGet<T>(Func<T?> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return default;
        }
    }
}

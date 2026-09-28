using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WinPilot.Automation.Elements;

/// <summary>
/// Re-resolves a stale element reference against the live UI Automation tree using its fingerprint:
/// automation id first, then name plus control type. Runs on the UI Automation worker thread.
/// </summary>
internal static class ElementResolver
{
    /// <summary>
    /// Polls the window for an element matching <paramref name="fingerprint"/> until
    /// <paramref name="timeout"/> elapses; returns null when no match appears.
    /// </summary>
    public static AutomationElement? TryResolve(
        Window window,
        ElementFingerprint fingerprint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var deadline = DateTime.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = FindOnce(window, fingerprint);
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private static AutomationElement? FindOnce(Window window, ElementFingerprint fingerprint)
    {
        try
        {
            if (!string.IsNullOrEmpty(fingerprint.AutomationId))
            {
                var byId = window.FindFirstDescendant(cf => cf.ByAutomationId(fingerprint.AutomationId));
                if (byId is not null)
                {
                    return byId;
                }
            }

            if (!string.IsNullOrEmpty(fingerprint.Name)
                && Enum.TryParse<ControlType>(fingerprint.ControlTypeName, out var controlType))
            {
                return window.FindFirstDescendant(cf => cf.ByName(fingerprint.Name).And(cf.ByControlType(controlType)));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Provider races while the UI changes are expected during re-resolution; retry on the next poll.
        }

        return null;
    }
}

namespace WinPilot.Mcp.Tools;

/// <summary>One action of a <c>windows_batch</c> call.</summary>
/// <param name="Action">Action name: click, type, fill, wait, snapshot, sendKeys, getText.</param>
/// <param name="Ref">Element ref for click/type/fill/sendKeys/getText.</param>
/// <param name="Text">Text for the type action.</param>
/// <param name="Value">Value for the fill action.</param>
/// <param name="Ms">Milliseconds for the wait action (default 100).</param>
/// <param name="Handle">Window handle for the snapshot action.</param>
/// <param name="Chord">Key chord for the sendKeys action.</param>
/// <param name="Keys">Key sequence for the sendKeys action.</param>
public sealed record BatchAction(
    string Action,
    string? Ref = null,
    string? Text = null,
    string? Value = null,
    int? Ms = null,
    string? Handle = null,
    string? Chord = null,
    IReadOnlyList<string>? Keys = null);

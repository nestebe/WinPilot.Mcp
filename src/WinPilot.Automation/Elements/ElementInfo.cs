namespace WinPilot.Automation.Elements;

/// <summary>An element resolved by reference: its agent-facing ref and snapshot-format line.</summary>
/// <param name="Ref">Element reference, for example <c>w1e5</c>.</param>
/// <param name="Line">Snapshot-format line, for example <c>button "Save" [ref=w1e5]</c>.</param>
public sealed record ElementInfo(string Ref, string Line);

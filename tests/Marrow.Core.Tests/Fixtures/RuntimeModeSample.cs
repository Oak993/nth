using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

// Trimmed shape of a real Codable type (B2), to prove Coding.swift's value(_:default:); not port code.

/// <summary>
/// Core/Models/Provider.swift:161-165 RuntimeMode (String raw values, Codable), trimmed to its cases. Library.swift:234-237
/// reads it with <c>container.value(.runtimeMode, default: .supervised)</c>: an unreadable mode must fall back to the one
/// that asks before it acts.
/// </summary>
public enum RuntimeModeSample
{
    [RawValue("supervised")] Supervised,
    [RawValue("autoAcceptEdits")] AutoAcceptEdits,
    [RawValue("auto")] Auto,
    [RawValue("fullAccess")] FullAccess,
}

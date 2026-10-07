// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.7): one progress item of the voice preparation.
namespace Marrow.App.Port.Voice;

/// <summary>
/// <c>(Id, Label, Fraction?, State)</c> (V-10.7). <see cref="Fraction"/> is real, never a guess: parts of the probe done, bytes of the model received; null when the work has
/// no measure (the speed test while it runs). OD-0096: real progress only, no timers.
/// </summary>
public sealed record VoicePrepItem(string Id, string Label, double? Fraction, VoicePrepState State)
{
    /// <summary>The actual model whose bytes are measured, including a smaller model after a step-down.</summary>
    public string? ModelId { get; init; }

    // V-10.7's progress value remains its four-tuple; model provenance is additional presentation metadata.
    public bool Equals(VoicePrepItem? other) => other is not null && Id == other.Id && Label == other.Label && Fraction == other.Fraction && State == other.State;
    public override int GetHashCode() => HashCode.Combine(Id, Label, Fraction, State);
}

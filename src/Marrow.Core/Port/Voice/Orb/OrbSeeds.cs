// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment: "the EXACT site exports"; first-release voice typing, VT-4, voice-SPEC V-11.1).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>
/// The two presets' <c>stateSeeds</c>, verbatim from the owner's exports (E:\Droppy\work\voice\reference\orb-exports\seeds.json, copied to
/// tests\fixtures\voice\seeds.json): Siri Wave (style 9) and Voice Membrane (style 19), each Idle and Thinking, 136 floats in the WGSL
/// <c>Uniforms</c> layout. The values are float literals generated from seeds.json (<c>OrbSeedData.cs</c>); a test compares every float.
/// </summary>
public static class OrbSeeds
{
    /// <summary>The number of floats in one seed (the export's 544-byte uniform block).</summary>
    public const int Length = 136;

    /// <summary>SHA-256 of the seeds.json the literals were generated from.</summary>
    public const string SourceSha256 = OrbSeedData.SourceSha256;

    /// <summary>The preset's Idle seed.</summary>
    public static ReadOnlySpan<float> Idle(VoiceOrbKind kind) => kind == VoiceOrbKind.Siri ? OrbSeedData.SiriIdle : OrbSeedData.MembraneIdle;

    /// <summary>The preset's Thinking seed.</summary>
    public static ReadOnlySpan<float> Thinking(VoiceOrbKind kind) => kind == VoiceOrbKind.Siri ? OrbSeedData.SiriThinking : OrbSeedData.MembraneThinking;

    /// <summary>The flow style the shader branches on (<c>values[15]</c>): 9 for Siri Wave, 19 for Voice Membrane.</summary>
    public static int Style(VoiceOrbKind kind) => kind == VoiceOrbKind.Siri ? 9 : 19;

    /// <summary>The export's page background for the preset (documentation of the source; the orb itself is drawn transparent).</summary>
    public static string Background(VoiceOrbKind kind) => kind == VoiceOrbKind.Siri ? OrbSeedData.SiriBackground : OrbSeedData.MembraneBackground;

    /// <summary>The preset's rest speed, the Idle seed's <c>values[3]</c> (Siri Wave 0.246, Voice Membrane 0.266).</summary>
    public static float IdleSpeed(VoiceOrbKind kind) => Idle(kind)[3];
}

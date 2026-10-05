using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-9: tests/fixtures/voice/glow-recipe.json, made by make-vt9-fixtures.js from voice-glow.js's Ya().</summary>
internal static class VoiceGlowRecipeFixture
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static JsonDocument Load([CallerFilePath] string here = "") =>
        JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "fixtures", "voice", "glow-recipe.json"))));

    /// <summary>The set's Ya argument (VT-4's glow-config.json <c>style</c>) as the type VT-4's resolver produces.</summary>
    public static VoiceGlowStyleArgs Style(JsonElement style) => style.Deserialize<VoiceGlowStyleArgs>(Options)!;
}

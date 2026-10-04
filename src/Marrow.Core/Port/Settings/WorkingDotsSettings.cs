// PORT-ONLY, owner-ruled addition (OD-0085, OD-0099 Look picks; first-release SA-1): the working dots colour setting
// (customization-SPEC CZ-13.3, CZ-10.3): the dots follow the accent, the model colour, or one palette colour.
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Marrow.Core.Port.Settings;

/// <summary>Where the working dots take their colour from (CZ-10.3): the accent (default), the model colour, or a palette colour.</summary>
public enum WorkingDotsMode { Accent, Model, Custom }

/// <summary>
/// The <c>workingDots</c> setting: compact JSON <c>{"mode":"accent","custom":"blue"}</c> for the defaults. <see cref="Custom"/> is an
/// accent palette id (<c>windows</c> for the Windows accent). Lenient read, as <c>ComposerLightSettings.FromJson</c>: a missing,
/// mistyped or unknown value keeps its default, and unreadable data as a whole is the default.
/// </summary>
public sealed record WorkingDotsSettings
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "workingDots";

    public WorkingDotsMode Mode { get; init; } = WorkingDotsMode.Accent;

    public string Custom { get; init; } = "blue";

    public static WorkingDotsSettings Default { get; } = new();

    /// <summary>
    /// The accent palette 22 ids, in the palette file order (the Windows accent first). Core cannot see the UI
    /// <c>AccentPalette</c>; a test in Marrow.App.Tests pins this list to it.
    /// </summary>
    public static IReadOnlyList<string> KnownCustomIds { get; } =
    [
        "windows", "blue", "purple", "pink", "red", "orange", "yellow", "green", "teal", "graphite", "ocean", "denim", "indigo",
        "lilac", "orchid", "rose", "coral", "bronze", "sage", "alpine", "mint", "slate",
    ];

    private static readonly string[] ModeNames = ["accent", "model", "custom"];

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Compact JSON; a mode or id outside the lists writes the default for that field, so what is stored always reads back.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("mode", (int)Mode >= 0 && (int)Mode < ModeNames.Length ? ModeNames[(int)Mode] : ModeNames[(int)Default.Mode]);
            w.WriteString("custom", KnownCustomIds.Contains(Custom) ? Custom : Default.Custom);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>The lenient read (CZ-13.3).</summary>
    public static WorkingDotsSettings FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default;
            var mode = Text(root, "mode") is { } modeName && Array.IndexOf(ModeNames, modeName) is var index and >= 0 ? (WorkingDotsMode)index : Default.Mode;
            var custom = Text(root, "custom") is { } id && KnownCustomIds.Contains(id) ? id : Default.Custom;
            return new WorkingDotsSettings { Mode = mode, Custom = custom };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // malformed JSON, invalid UTF-8, a lone surrogate escape (GetString throws InvalidOperationException)
            return Default;
        }
    }

    private static string? Text(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

// PORT-ONLY, owner-ruled addition (OD-0092 and its amendment, OD-0099 Q-V-14; first-release voice typing, VT-1): the catalogue of
// Whisper models the Voice typing page offers (voice-SPEC V-9.1, V-9.2, V-9.6). The six models and their dots are the design's
// (DESIGN voice.js:8-17); the files and hashes are the embedded manifest (ModelManifest.cs).
using System.Globalization;

namespace Marrow.Core.Port.Voice;

/// <summary>One of the six models the page lists (V-9.1): its name, speed and accuracy dots (of 5), whether an English-only form exists, and its note.</summary>
public sealed record WhisperModel(string Name, int Speed, int Accuracy, bool HasEnglishOnly, string? Note);

/// <summary>The model catalogue: six models, 29 files (V-9.1), names (V-9.6) and sizes as shown (V-9.2).</summary>
public static class ModelCatalog
{
    /// <summary>The Hugging Face ggerganov/whisper.cpp commit every file is pinned to (V-3.3).</summary>
    public const string HuggingFaceCommit = "5359861c739e955e79d9a303bcbc70fb988958b1";

    private const string EnglishNote = "English only is a little more accurate for English and knows no other language.";

    /// <summary>
    /// The models in the page's order (V-16.3.3 lists six rows). Notes (V-9.1, first match wins): Large v3 Turbo and Tiny, Base, Small.
    /// DEVIATION ED-267 (4) / OD-0099 Q-V-14: the notes "Can also translate speech into English." of Medium and Large v3 are dropped,
    /// because the page has no translate option, so those two have no note.
    /// </summary>
    public static IReadOnlyList<WhisperModel> Models { get; } =
    [
        new("Tiny", 5, 1, true, EnglishNote),
        new("Base", 5, 2, true, EnglishNote),
        new("Small", 4, 3, true, EnglishNote),
        new("Medium", 2, 4, true, null),
        new("Large v3 Turbo", 4, 5, false, "About as accurate as Large v3, far quicker; it does not translate."),
        new("Large v3", 1, 5, false, null),
    ];

    /// <summary>The 29 files in the manifest's order (V-9.1).</summary>
    public static IReadOnlyList<ModelFile> Files { get; } = ModelManifest.Files;

    /// <summary>The file with this id, or null (large-v1, large-v2 and the like are not offered).</summary>
    public static ModelFile? Find(string? id)
    {
        if (id is null) return null;
        foreach (var file in Files)
            if (string.Equals(file.Id, id, StringComparison.Ordinal)) return file;
        return null;
    }

    /// <summary>The model called <paramref name="name"/> ("Tiny", "Large v3 Turbo", ...).</summary>
    public static WhisperModel Model(string name)
    {
        foreach (var model in Models)
            if (string.Equals(model.Name, name, StringComparison.Ordinal)) return model;
        throw new ArgumentException("no such model: " + name, nameof(name));
    }

    /// <summary>The model a file belongs to.</summary>
    public static WhisperModel ModelOf(string id) =>
        Model((Find(id) ?? throw new ArgumentException("no such file: " + id, nameof(id))).Model);

    /// <summary>The files of one model, in the manifest's order.</summary>
    public static IReadOnlyList<ModelFile> FilesOf(string modelName) =>
        Files.Where(f => string.Equals(f.Model, modelName, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// V-9.2 (DESIGN voice.js:32): MB = bytes / 1,000,000; <c>fmt(MB / 1000, 2) + " GB"</c> when MB &gt;= 1000, else <c>round(MB) + " MB"</c>
    /// (fmt and round as SPEC 1 CZ-12.10: JavaScript Math.round, half up; shortest digits, no trailing zeros).
    /// </summary>
    public static string SizeText(long bytes)
    {
        var mb = bytes / 1_000_000.0;
        return mb >= 1000
            ? Fmt(mb / 1000, 2) + " GB"
            : Fmt(mb, 0) + " MB";
    }

    /// <summary>V-9.6 (DESIGN voice.js:33): <c>Model + (en ? " (English only)" : "") + ", " + version lower case + " " + size</c>; null for an unknown id.</summary>
    public static string? ModelName(string? id) =>
        Find(id) is { } file ? ShortName(id) + ", " + file.Version.ToString().ToLowerInvariant() + " " + SizeText(file.Bytes) : null;

    /// <summary>The model's name with its English-only mark and no version or size (the head of V-9.6); null for an unknown id.</summary>
    public static string? ShortName(string? id) =>
        Find(id) is { } file ? file.Model + (file.EnglishOnly ? " (English only)" : "") : null;

    // fmt(v, n): (Math.round(v * 10^n) / 10^n).toString(); Math.round rounds a half up.
    private static string Fmt(double value, int digits)
    {
        var scale = Math.Pow(10, digits);
        return (Math.Floor(value * scale + 0.5) / scale).ToString("R", CultureInfo.InvariantCulture);
    }
}

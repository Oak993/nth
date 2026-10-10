// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-13 B and Q-V-14; first-release voice typing, VT-14): what the Voice typing page's model list says about a model and its
// variants (voice-SPEC V-16.3.3, V-16.3.4, V-9.1, V-9.2). Pure: the page asks it for texts and for the file a row stands for.
namespace Marrow.Core.Port.Voice;

/// <summary>The variant a model row shows (V-16.3.3): a version, and the English only form where the model has one.</summary>
public sealed record ModelRowChoice(ModelVersion Version, bool EnglishOnly);

/// <summary>The model list's words and its choices of file (V-16.3.3, V-16.3.4).</summary>
public static class SpeechModelList
{
    /// <summary>V-16.3.3: the card's caption (DESIGN voice.js:10 family title).</summary>
    public const string Caption = "Whisper";

    /// <summary>V-16.3.3: the caption's subtitle (DESIGN voice.js:10).</summary>
    public const string Subtitle = "OpenAI, 99 languages. Speed as on a graphics card; the processor alone is slower";

    /// <summary>The middle dot between a version and its size in a segment (V-16.3.3 `{Version} · {size}`).</summary>
    private const string Dot = " · ";

    /// <summary>V-16.3.3: the version button, `{Version}, {size}` and `, English only` (DESIGN voice.js:132).</summary>
    public static string Meta(ModelFile file) =>
        file.Version + ", " + ModelCatalog.SizeText(file.Bytes) + (file.EnglishOnly ? ", English only" : "");

    /// <summary>V-16.3.3: one segment of the version control, `{Version} · {size}` (DESIGN voice.js:136).</summary>
    public static string SegmentText(ModelFile file) => file.Version + Dot + ModelCatalog.SizeText(file.Bytes);

    /// <summary>V-16.3.3: the meters' accessible name, `{name}: speed {n} of 5, accuracy {m} of 5` (DESIGN voice.js:134).</summary>
    public static string MeterName(WhisperModel model) => model.Name + ": speed " + model.Speed + " of 5, accuracy " + model.Accuracy + " of 5";

    /// <summary>V-16.3.4: the footer, `Installed: {size}, in Marrow Code’s models folder` (DESIGN voice.js:200), the size as V-9.2 formats it.</summary>
    public static string Footer(long installedBytes) => "Installed: " + ModelCatalog.SizeText(installedBytes) + ", in nth’s models folder";

    /// <summary>The versions a model has in the manifest, in the order Compact, Balanced, Full (Large v3 has no Balanced).</summary>
    public static IReadOnlyList<ModelVersion> VersionsOf(string modelName) =>
        ModelCatalog.FilesOf(modelName).Select(f => f.Version).Distinct().Order().ToList();

    /// <summary>The file a choice stands for. A model with no English only form ignores the flag; a version it does not have is a mistake.</summary>
    public static ModelFile File(string modelName, ModelRowChoice choice)
    {
        var files = ModelCatalog.FilesOf(modelName);
        var english = choice.EnglishOnly && files.Any(f => f.EnglishOnly);
        return files.FirstOrDefault(f => f.Version == choice.Version && f.EnglishOnly == english)
            ?? throw new ArgumentException($"{modelName} has no {choice.Version} version", nameof(choice));
    }

    /// <summary>
    /// V-16.3.3 (ED): the variant a row shows to begin with is the in-use one, else the recommended one, else Compact (not English only).
    /// An id that belongs to another model, or to no model, counts for nothing.
    /// </summary>
    public static ModelRowChoice InitialChoice(string modelName, string? inUseId, string? recommendedId)
    {
        foreach (var id in new[] { inUseId, recommendedId })
            if (ModelCatalog.Find(id) is { } file && string.Equals(file.Model, modelName, StringComparison.Ordinal))
                return new ModelRowChoice(file.Version, file.EnglishOnly);
        return new ModelRowChoice(ModelVersion.Compact, false);
    }
}

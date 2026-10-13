// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22: "every new string in the resource files"; ED-NEW-web-w22-1). No Swift counterpart.
// The resource files for nth's own words: flat JSON files, one per area and language (Localisation/Strings/<area>-<language>.json, embedded as
// Marrow.Core.Strings.<area>-<language>.json; no dot before the language, or MSBuild takes it for a culture and moves the file to a satellite assembly), read once into one table per language. The repository's own idiom (an embedded JSON file, like
// the skills catalogue) rather than a new package or a generated class.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.Core.Localisation;

/// <summary>One resource file: its name (for an error), its language and its JSON.</summary>
public readonly record struct UiCatalogueFile(string Name, string Language, string Json);

public sealed partial class UiCatalogue
{
    private const string ResourcePrefix = "Marrow.Core.Strings.";

    private static readonly Lazy<UiCatalogue> embedded = new(LoadEmbedded);

    private readonly Dictionary<string, Dictionary<string, string>> byLanguage;

    /// <summary>Lower-case words joined by dots, an area first: <c>visus.toolbar.back</c>.</summary>
    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:\.[a-z0-9_]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShape();

    private UiCatalogue(Dictionary<string, Dictionary<string, string>> byLanguage) => this.byLanguage = byLanguage;

    /// <summary>The files nth ships, read from the assembly.</summary>
    public static UiCatalogue Embedded => embedded.Value;

    public IReadOnlyCollection<string> Languages => byLanguage.Keys;

    /// <summary>Every key and template of a language (empty when there is none).</summary>
    public IReadOnlyDictionary<string, string> Entries(string language) =>
        byLanguage.TryGetValue(language, out var entries) ? entries : new Dictionary<string, string>();

    public bool TryGet(string language, string key, out string template)
    {
        if (byLanguage.TryGetValue(language, out var entries) && entries.TryGetValue(key, out var found))
        {
            template = found;
            return true;
        }
        template = "";
        return false;
    }

    /// <summary>
    /// What is wrong with a language's file set next to English, as one line each: a key English has and it lacks, a key it has and English lacks,
    /// and a string whose <c>{0}</c> holes are not the ones English has (a translation that drops the count would read wrong or throw). Empty is
    /// complete. English itself is always complete.
    /// </summary>
    public IReadOnlyList<string> Problems(string language)
    {
        var problems = new List<string>();
        var english = Entries(UiLanguages.Fallback);
        var other = Entries(language);
        if (string.Equals(language, UiLanguages.Fallback, StringComparison.Ordinal)) return problems;
        foreach (var (key, template) in english)
        {
            if (!other.TryGetValue(key, out var translated)) problems.Add(language + " lacks " + key);
            else if (!Holes(template).SequenceEqual(Holes(translated))) problems.Add(language + " " + key + " has other holes than English");
        }
        foreach (var key in other.Keys)
            if (!english.ContainsKey(key)) problems.Add(language + " has " + key + ", which English does not");
        return problems;
    }

    /// <summary>The numbers of the <c>{n}</c> holes a template fills, sorted and without repeats ("{{" and "}}" are braces, not holes).</summary>
    public static IReadOnlyList<int> Holes(string template) =>
        [.. HoleShape().Matches(template.Replace("{{", "", StringComparison.Ordinal).Replace("}}", "", StringComparison.Ordinal))
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];

    [GeneratedRegex(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex HoleShape();

    /// <summary>
    /// Reads files into a catalogue. A key shape that is not <c>area.name</c>, a value that is not a string, a key twice in a file, or the same
    /// key in two files of one language throws <see cref="InvalidDataException"/> naming the file.
    /// </summary>
    public static UiCatalogue FromFiles(IEnumerable<UiCatalogueFile> files)
    {
        var byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var where = new Dictionary<(string Language, string Key), string>();
        foreach (var file in files)
        {
            if (!byLanguage.TryGetValue(file.Language, out var entries)) byLanguage[file.Language] = entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in ReadFlat(file))
            {
                if (!KeyShape().IsMatch(key)) throw new InvalidDataException(file.Name + ": \"" + key + "\" is not a key of the form area.name.");
                if (where.TryGetValue((file.Language, key), out var first))
                    throw new InvalidDataException(file.Name + ": \"" + key + "\" is already in " + first + ".");
                where[(file.Language, key)] = file.Name;
                entries[key] = value;
            }
        }
        return new UiCatalogue(byLanguage);
    }

    // Utf8JsonReader sees a key twice, which JsonDocument would hide.
    private static List<(string Key, string Value)> ReadFlat(UiCatalogueFile file)
    {
        var pairs = new List<(string Key, string Value)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(file.Json), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException(file.Name + ": the file is not an object.");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var key = reader.GetString() ?? "";
                if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new InvalidDataException(file.Name + ": \"" + key + "\" is not a string.");
                if (!seen.Add(key)) throw new InvalidDataException(file.Name + ": \"" + key + "\" is in the file twice.");
                pairs.Add((key, reader.GetString() ?? ""));
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new InvalidDataException(file.Name + ": the file is not a flat object of strings.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(file.Name + ": " + error.Message, error);
        }
        return pairs;
    }

    private static UiCatalogue LoadEmbedded()
    {
        var assembly = typeof(UiCatalogue).Assembly;
        var files = new List<UiCatalogueFile>();
        foreach (var resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal)) continue;
            // Marrow.Core.Strings.<area>-<language>.json: the area has no hyphen; the language may ("ur-PK").
            var name = resource[ResourcePrefix.Length..^".json".Length];
            var cut = name.IndexOf('-', StringComparison.Ordinal);
            if (cut <= 0 || cut == name.Length - 1 || name.Contains('.', StringComparison.Ordinal))
                throw new InvalidDataException("A string file is named <area>-<language>.json: " + resource);
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("Missing resource " + resource);
            using var reader = new StreamReader(stream);
            files.Add(new UiCatalogueFile(resource, name[(cut + 1)..], reader.ReadToEnd()));
        }
        return FromFiles(files);
    }
}

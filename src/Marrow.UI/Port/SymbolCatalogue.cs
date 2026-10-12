// PORT-ONLY: SF Symbols ship with macOS and cannot ship on Windows (PA-0012); OD-0010 maps each one to a Lucide icon
// through porting/icons/sf-symbols-to-lucide.json, embedded with the icons under Assets/Icons (ED-179 (1)).
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Platform;

namespace Marrow.UI.Port;

// Decisions: ED-179, ED-182 (the composition), OD-0010, PA-0012. The field meanings are porting/icons/README.md's ("Files", "Rendering rules").

/// <summary>The mapping's <c>variant</c>: what the SF name adds to its base glyph (README "Rendering rules").</summary>
public sealed record SymbolVariant(bool Fill, string? Container, bool Slash, string? Badge);

/// <summary>One entry of the mapping's <c>symbols</c> array.</summary>
public sealed record SymbolMapping(
    string Sf,
    string Lucide,
    string Confidence,
    string? Note,
    IReadOnlyList<string> UsedIn,
    SymbolVariant Variant,
    bool ComposedRendering,
    string? FillRendering);

/// <summary>The picker shortlist or one picker category, in source order (the mapping's <c>catalogue</c>).</summary>
public sealed record SymbolCategory(string Title, string Site, IReadOnlyList<string> Symbols);

/// <summary>
/// What a composed_rendering entry's note asks to be drawn over its Lucide icon (ED-182 (Q5)), read from the note's
/// "composed:" clauses. <see cref="Text"/>: the characters of a "draw "X" as text inside the Lucide circle|square" clause
/// (the Lucide icon is then the container itself). <see cref="Container"/>: the SF container name (the variant's
/// container) for an "enclose" clause, or the text clause's container. <see cref="Slash"/>: a slash clause.
/// <see cref="Badge"/>: a badge clause. <see cref="Undrawn"/>: every other composed clause, verbatim; ED-182 gives
/// those no geometry, so the Lucide icon is drawn without them (UNRESOLVED, tracked in SwiftImage.cs's header).
/// </summary>
public sealed record SymbolComposition(string? Text, string? Container, bool Slash, bool Badge, IReadOnlyList<string> Undrawn)
{
    public static SymbolComposition None { get; } = new(null, null, false, false, []);

    public bool Equals(SymbolComposition? other) => other is not null && Text == other.Text && Container == other.Container
        && Slash == other.Slash && Badge == other.Badge && Undrawn.SequenceEqual(other.Undrawn);

    public override int GetHashCode() => HashCode.Combine(Text, Container, Slash, Badge, Undrawn.Count);
}

/// <summary>The embedded SF Symbol to Lucide mapping and the Lucide icons it names.</summary>
public static class SymbolCatalogue
{
    private const string Root = "avares://Marrow.UI/Assets/Icons/";

    private static readonly Lazy<Data> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, SvgIcon?> Icons = new(StringComparer.Ordinal);

    private sealed record Data(
        IReadOnlyDictionary<string, SymbolMapping> Symbols,
        IReadOnlyCollection<string> Names,
        SymbolCategory Popular,
        IReadOnlyList<SymbolCategory> Categories);

    /// <summary>PORT-ONLY (ED-NEW-perf-startup-structure-5): whether the mapping has been read (or has failed to read), for the start-up prime's test.</summary>
    internal static bool IsLoaded => Loaded.IsValueCreated;

    /// <summary>Every SF name the mapping holds (2753 at B2), in the mapping's order.</summary>
    public static IReadOnlyCollection<string> SymbolNames => Loaded.Value.Names;

    /// <summary>The picker's Popular shortlist (ProjectIconPicker.swift:87-102).</summary>
    public static SymbolCategory Popular => Loaded.Value.Popular;

    /// <summary>The picker's categories in source order (ProjectSymbolCatalogue.swift:25-51).</summary>
    public static IReadOnlyList<SymbolCategory> Categories => Loaded.Value.Categories;

    /// <summary>The mapping entry for an SF name, or null when the name is not an SF Symbol the app can draw.</summary>
    public static SymbolMapping? Mapping(string sf) => Loaded.Value.Symbols.GetValueOrDefault(sf) ?? PortAdded.GetValueOrDefault(sf);

    // PORT-ONLY (customization-SPEC CZ-2.1.1; first-release SA-1): the symbols of the Settings pages the owner added (Voice typing,
    // Updates, Plugins) that the B2 table does not hold. The table (porting/icons/sf-symbols-to-lucide.json) stays embedded
    // unmodified (ED-179), so these live here, beside it, and are not part of SymbolNames or the picker.
    private static SymbolMapping Added(string sf, string lucide, string confidence, string note) =>
        new(sf, lucide, confidence, note, ["app"], new SymbolVariant(false, null, false, null), false, null);

    private static readonly IReadOnlyDictionary<string, SymbolMapping> PortAdded = new[]
    {
        Added("waveform", "audio-waveform", "close", "Voice typing page (CZ-2.1.1): SF waveform is vertical bars of different heights; Lucide audio-waveform is the same idea drawn as one line"),
        Added("arrow.triangle.2.circlepath", "refresh-cw", "exact", "Updates page: two arrows chasing round"),
        Added("powerplug", "plug", "close", "Plugins page: a plug"),
    }.ToDictionary(mapping => mapping.Sf, StringComparer.Ordinal);

    /// <summary>The parsed Lucide icon of that name, or null when no such icon is embedded. Parsed once and shared.</summary>
    public static SvgIcon? Icon(string lucideName) => Icons.GetOrAdd(lucideName, static name =>
    {
        if (name.Length == 0 || name.IndexOfAny(['/', '\\', '.']) >= 0) return null;
        var uri = new Uri($"{Root}lucide/{name}.svg");
        if (!AssetLoader.Exists(uri)) return null;
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return SvgIcon.Parse(reader.ReadToEnd());
    });

    private static readonly System.Text.RegularExpressions.Regex TextClause =
        new("^composed: draw \"(?<text>[^\"]+)\" as text inside the Lucide (?<container>circle|square)(?![a-z])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The composition a mapping entry's note asks for (ED-182 (Q5)); <see cref="SymbolComposition.None"/> when the
    /// entry is not composed_rendering. A clause is recognised by its wording in porting/icons/README.md's own table:
    /// "draw "X" as text inside the Lucide circle|square", "enclose ...", "... slash ..." and "... badge ...".
    /// </summary>
    public static SymbolComposition Composition(SymbolMapping mapping)
    {
        if (!mapping.ComposedRendering || mapping.Note is null) return SymbolComposition.None;
        string? text = null, container = null;
        bool enclose = false, slash = false, badge = false;
        var undrawn = new List<string>();
        foreach (var clause in mapping.Note.Split("; ").Select(c => c.Trim()).Where(c => c.StartsWith("composed:", StringComparison.Ordinal)))
        {
            var words = clause["composed:".Length..];
            if (TextClause.Match(clause) is { Success: true } match)
            {
                text = match.Groups["text"].Value;
                container = match.Groups["container"].Value;
            }
            else if (words.TrimStart().StartsWith("enclose", StringComparison.Ordinal) && mapping.Variant.Container is not null) enclose = true;
            else if (ContainsWord(words, "slash") && mapping.Variant.Slash) slash = true;
            else if (ContainsWord(words, "badge") && mapping.Variant.Badge is not null) badge = true;
            else undrawn.Add(clause);
        }
        // Text drawn inside the Lucide circle or square is already the enclosed glyph: an "enclose" clause beside it
        // (left, right and sos .circle.fill) asks for nothing more.
        if (enclose && text is null) container = mapping.Variant.Container;
        return new SymbolComposition(text, container, slash, badge, undrawn.AsReadOnly());
    }

    private static bool ContainsWord(string text, string word) =>
        text.Split([' ', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries).Contains(word, StringComparer.Ordinal);

    /// <summary>
    /// The Lucide icon that draws an SF container (ED-182 (Q5)): the mapping's own icon for the filled container
    /// symbol, <c>&lt;container&gt;.fill</c> (circle -> circle, square -> square, rectangle -> rectangle-horizontal,
    /// app -> squircle, diamond -> diamond, capsule -> rectangle-horizontal, shield -> shield, triangle -> triangle);
    /// null for a container the mapping has no symbol for.
    /// </summary>
    public static SvgIcon? ContainerIcon(string container) =>
        Mapping(container + ".fill") is { } mapping ? Icon(mapping.Lucide) : null;

    private static Data Load()
    {
        using var stream = AssetLoader.Open(new Uri($"{Root}sf-symbols-to-lucide.json"));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var symbols = new Dictionary<string, SymbolMapping>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var entry in root.GetProperty("symbols").EnumerateArray())
        {
            var variant = entry.GetProperty("variant");
            var mapping = new SymbolMapping(
                entry.GetProperty("sf").GetString()!,
                entry.GetProperty("lucide").GetString()!,
                entry.GetProperty("confidence").GetString()!,
                OptionalString(entry, "note"),
                entry.GetProperty("used_in").EnumerateArray().Select(u => u.GetString()!).ToArray(),
                new SymbolVariant(
                    variant.GetProperty("fill").GetBoolean(),
                    OptionalString(variant, "container"),
                    variant.GetProperty("slash").GetBoolean(),
                    OptionalString(variant, "badge")),
                entry.GetProperty("composed_rendering").GetBoolean(),
                OptionalString(entry, "fill_rendering"));
            if (!symbols.TryAdd(mapping.Sf, mapping)) throw new InvalidDataException($"Duplicate SF name {mapping.Sf} in the mapping");
            names.Add(mapping.Sf);
        }
        var catalogue = root.GetProperty("catalogue");
        return new Data(symbols, names.AsReadOnly(), Category(catalogue.GetProperty("popular")),
            catalogue.GetProperty("categories").EnumerateArray().Select(Category).ToArray());
    }

    private static SymbolCategory Category(JsonElement element) => new(
        element.GetProperty("title").GetString()!,
        element.GetProperty("site").GetString()!,
        element.GetProperty("symbols").EnumerateArray().Select(s => s.GetProperty("sf").GetString()!).ToArray());

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

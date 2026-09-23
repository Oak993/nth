// PORT-ONLY: SF Symbols ship with macOS and cannot ship on Windows (PA-0012); OD-0010 maps each one to a Lucide icon
// through porting/icons/sf-symbols-to-lucide.json, embedded with the icons under Assets/Icons (ED-179 (1)).
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Platform;

namespace Marrow.UI.Port;

// Decisions: ED-179, OD-0010, PA-0012. The field meanings are porting/icons/README.md's ("Files", "Rendering rules").

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

    /// <summary>Every SF name the mapping holds (2753 at B2), in the mapping's order.</summary>
    public static IReadOnlyCollection<string> SymbolNames => Loaded.Value.Names;

    /// <summary>The picker's Popular shortlist (ProjectIconPicker.swift:87-102).</summary>
    public static SymbolCategory Popular => Loaded.Value.Popular;

    /// <summary>The picker's categories in source order (ProjectSymbolCatalogue.swift:25-51).</summary>
    public static IReadOnlyList<SymbolCategory> Categories => Loaded.Value.Categories;

    /// <summary>The mapping entry for an SF name, or null when the name is not an SF Symbol the app can draw.</summary>
    public static SymbolMapping? Mapping(string sf) => Loaded.Value.Symbols.GetValueOrDefault(sf);

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

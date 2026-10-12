using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Port-only selection; the upstream custom-image keys remain independently usable.</summary>
public sealed class WallpaperLibraryStore : ObservableModel
{
    private const string Key = "wallpaperLibrary";
    // Each wallpaper's own last choices (pack including match, speed, options, dynamic, persp, focus), by "kind/id", as the page keeps them per wallpaper.
    private const string ChoicesKey = "wallpaperLibraryChoices";
    private static readonly JsonSerializerOptions json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<WallpaperKind>(JsonNamingPolicy.CamelCase) },
    };
    public static Ambient<WallpaperLibraryStore> Shared { get; } = new(() => new(UserDefaults.Standard.Current));
    private readonly ISettingsStore defaults;
    private WallpaperSelection selection;
    private readonly Dictionary<string, WallpaperSelection> choices = new(StringComparer.Ordinal);

    public WallpaperLibraryStore(ISettingsStore defaults)
    {
        this.defaults = defaults;
        selection = new();
        if (defaults.Object(Key) is string stored)
        {
            try { selection = Normalize(JsonSerializer.Deserialize<WallpaperSelection>(stored, json) ?? new()); }
            catch (JsonException) { }
        }
        LoadLegacyStaticChoices();
        LoadChoices();
    }

    // Read WL-4's experimental key once as a migration input; never write a second schema.
    private void LoadLegacyStaticChoices()
    {
        if (defaults.Object("wallpaperLibraryStatic") is not string stored) return;
        try
        {
            foreach (var (id, value) in JsonSerializer.Deserialize<Dictionary<string, WallpaperSelection?>>(stored, json) ?? [])
                if (value is not null && Normalize(value) is { Kind: WallpaperKind.Static } valid && valid.Id == id)
                    choices[ChoiceKey(WallpaperKind.Static, id)] = Persisted(valid);
        }
        catch (JsonException) { }
    }

    private static WallpaperSelection Persisted(WallpaperSelection value) => value with
    {
        Options = value.Options?.Where(o => o.Key is not ("dynamic" or "persp" or "focus"))
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
    };

    /// <summary>
    /// PORT-ONLY (ED-NEW-perf-startup-structure-5): whether the settings hold a library wallpaper. Making the store then builds the whole catalogue
    /// (two JSON documents and a definition per wallpaper) to check the choice, so start-up reads the catalogue on the pool first; with none saved,
    /// nothing is read.
    /// </summary>
    public static bool HasSavedSelection(ISettingsStore defaults) => defaults.Object(Key) is string;

    public void Select(WallpaperKind kind, string id) => Selection = Recall(kind, id);

    private void LoadChoices()
    {
        if (defaults.Object(ChoicesKey) is not string stored) return;
        try
        {
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, WallpaperSelection>>(stored, json) ?? [])
                if (value is not null && Normalize(value) is { Kind: not WallpaperKind.None } valid
                    && key == ChoiceKey(valid.Kind, valid.Id!)) choices[key] = Persisted(valid);
        }
        catch (JsonException) { }
    }

    private static string ChoiceKey(WallpaperKind kind, string id) => $"{kind.ToString().ToLowerInvariant()}/{id}";

    /// <summary>The wallpaper's own last choices, or its source defaults when it has none yet (what a picker shows when it is chosen again).</summary>
    public WallpaperSelection Recall(WallpaperKind kind, string? id)
    {
        if (kind == WallpaperKind.None || id is null) return new();
        return Normalize(choices.TryGetValue(ChoiceKey(kind, id), out var remembered) ? remembered : new(kind, id));
    }

    private void Remember()
    {
        if (selection.Kind == WallpaperKind.None) return;
        choices[ChoiceKey(selection.Kind, selection.Id!)] = Persisted(selection);
        defaults.Set(ChoicesKey, JsonSerializer.Serialize(choices, json));
    }

    public WallpaperSelection Selection
    {
        get => Read(selection);
        set => Set(ref selection, Normalize(value), _ => { defaults.Set(Key, JsonSerializer.Serialize(Persisted(selection), json)); Remember(); });
    }

    public static void CustomImageInstalled()
    {
        // Existing custom-image installations with no library setting need no library singleton.
        if (UserDefaults.Standard.Current.Object(Key) is null) return;
        Shared.Current.Selection = new();
    }

    public static WallpaperSelection Normalize(WallpaperSelection value)
    {
        if (value.Kind == WallpaperKind.None || !WallpaperCatalogue.All.Any(w => w.Kind == value.Kind && w.Id == value.Id)) return new();
        var wall = WallpaperCatalogue.Get(value.Kind, value.Id!);
        var premium = wall.Premium;
        var data = wall.Static;
        var pack = wall.Packs.Any(p => p.Id == value.Pack)
            || (premium is not null && value.Pack == WallpaperMatchPack.Id)
            || (data is not null && value.Pack == StaticWallpaperPalette.AccentPack) ? value.Pack : wall.DefaultPack;
        var options = wall.Options.ToDictionary(o => o.Id, o => value.Options is not null && value.Options.TryGetValue(o.Id, out var v)
            && double.IsFinite(v) ? Math.Clamp(v, o.Min, o.Max) : o.Default, StringComparer.Ordinal);
        double Legacy(string id, double fallback) => value.Options is not null && value.Options.TryGetValue(id, out var v)
            && double.IsFinite(v) ? Math.Clamp(v, 0, 1) : fallback;
        var dynamic = value.Dynamic ?? (data is not null ? Legacy("dynamic", 0) > .5 : premium?.DynamicDefault ?? false);
        var persp = value.Persp ?? (data is not null ? Legacy("persp", data.PerspectiveDefault) > .5 : premium?.Perspective.Default ?? false);
        var focus = value.Focus is { } f && double.IsFinite(f) ? Math.Clamp(f, 0, 1)
            : data is not null ? Legacy("focus", data.FocusDefault) : premium?.FocusDefault ?? 0;
        // Legacy static callers see their option aliases; persistence and both render paths use the canonical fields.
        if (data is not null) { options["dynamic"] = dynamic ? 1 : 0; options["persp"] = persp ? 1 : 0; options["focus"] = focus; }
        return value with { Pack = pack, Dynamic = dynamic, Persp = persp, Focus = focus,
            Speed = double.IsFinite(value.Speed) ? Math.Clamp(value.Speed, .25, 2.5) : 1,
            Options = new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(options) };
    }
}

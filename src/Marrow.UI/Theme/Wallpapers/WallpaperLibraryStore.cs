using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Port-only selection; the upstream custom-image keys remain independently usable.</summary>
public sealed class WallpaperLibraryStore : ObservableModel
{
    private const string Key = "wallpaperLibrary";
    private static readonly JsonSerializerOptions json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<WallpaperKind>(JsonNamingPolicy.CamelCase) },
    };
    public static Ambient<WallpaperLibraryStore> Shared { get; } = new(() => new(UserDefaults.Standard.Current));
    private readonly ISettingsStore defaults;
    private WallpaperSelection selection;

    public WallpaperLibraryStore(ISettingsStore defaults)
    {
        this.defaults = defaults;
        selection = new();
        if (defaults.Object(Key) is not string stored) return;
        try { selection = Normalize(JsonSerializer.Deserialize<WallpaperSelection>(stored, json) ?? new()); }
        catch (JsonException) { }
    }

    public WallpaperSelection Selection
    {
        get => Read(selection);
        set => Set(ref selection, Normalize(value), _ => defaults.Set(Key, JsonSerializer.Serialize(selection, json)));
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
        var pack = wall.Packs.Any(p => p.Id == value.Pack) ? value.Pack : wall.DefaultPack;
        var options = wall.Options.ToDictionary(o => o.Id, o => value.Options is not null && value.Options.TryGetValue(o.Id, out var v)
            && double.IsFinite(v) ? Math.Clamp(v, o.Min, o.Max) : o.Default, StringComparer.Ordinal);
        return value with { Pack = pack, Speed = double.IsFinite(value.Speed) ? Math.Clamp(value.Speed, .25, 2.5) : 1,
            Options = new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(options) };
    }
}

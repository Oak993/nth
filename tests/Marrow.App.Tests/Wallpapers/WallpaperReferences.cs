using System.Text.Json;

namespace Marrow.App.Tests.Wallpapers;

public static class WallpaperReferences
{
    private static readonly Lazy<JsonDocument> document = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json"))));
    public static JsonElement Manifest => document.Value.RootElement;
    public static IReadOnlyList<WallpaperFidelityCase> Cases { get; } = Load();
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "fixtures", "wallpapers", "reference");
    public static IEnumerable<object[]> Wallpapers(string collection) => Cases.Where(c => string.Equals(c.Collection, collection, StringComparison.Ordinal))
        .Select(c => c.Wallpaper).Distinct(StringComparer.Ordinal).Select(id => new object[] { id });
    private static IReadOnlyList<WallpaperFidelityCase> Load() => Manifest.GetProperty("renders").EnumerateArray().Select(e => new WallpaperFidelityCase(e)).ToArray();
}

using System.Text.Json;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>The numbers the snapshot page's own colour code produced (tests/fixtures/wallpaper-library/gen_page_fixture.cjs).</summary>
internal static class WallpaperPageFixture
{
    public static JsonElement Root { get; } = Load();

    private static JsonElement Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "wallpaper-library", "page_fixture.json")));
        return json.RootElement.Clone();
    }
}

using Marrow.Testing;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :97-109,234-245,262-269. Moved from
// Marrow.App.Tests/Theme to Marrow.UI.Tests/Theme at lane A10/ED-195, alongside src/Marrow.UI/Theme/Wallpaper.cs.
public sealed class WallpaperTests
{
    [Fact]
    [Trait("Req", "PR-THEME-054")] // UI/Theme/Wallpaper.swift:97-109
    public void Accepts_an_image_extension_without_reading_the_file()
    {
        Assert.True(WallpaperStore.Accepts(@"C:\marrow-wallpaper-tests\picture.png"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-054")] // UI/Theme/Wallpaper.swift:97-109
    public void Accepts_an_extensionless_file_when_its_content_is_an_image()
    {
        var directory = CreateTestDirectory();
        var file = Path.Combine(directory, "picture");
        File.WriteAllBytes(file, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.True(WallpaperStore.Accepts(file));
    }

    [Fact]
    [Trait("Req", "PR-THEME-054")] // UI/Theme/Wallpaper.swift:97-109
    public void Rejects_a_non_image_extension_and_content()
    {
        var directory = CreateTestDirectory();
        var file = Path.Combine(directory, "picture.txt");
        File.WriteAllBytes(file, "not an image"u8.ToArray());

        Assert.False(WallpaperStore.Accepts(file));
    }

    [Fact]
    [Trait("Req", "PR-THEME-065")] // UI/Theme/Wallpaper.swift:234-245
    public void Thumbnail_returns_nothing_when_the_image_decoder_cannot_open_the_file()
    {
        var directory = CreateTestDirectory();
        var file = Path.Combine(directory, "not-an-image.png");
        File.WriteAllBytes(file, "not an image"u8.ToArray());

        Assert.Null(WallpaperStore.Thumbnail(file, 3200));
    }

    [Fact]
    [Trait("Req", "PR-THEME-067")] // UI/Theme/Wallpaper.swift:262-269
    public void Decode_returns_nothing_when_the_image_decoder_cannot_open_the_file()
    {
        var directory = CreateTestDirectory();
        var file = Path.Combine(directory, "not-an-image.png");
        File.WriteAllBytes(file, "not an image"u8.ToArray());

        Assert.Null(WallpaperStore.Decode(file, 0));
    }

    private static string CreateTestDirectory()
    {
        var root = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must name a throwaway test-data directory.");

        var directory = Path.Combine(root!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

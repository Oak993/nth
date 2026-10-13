using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 2.5 size ladder "Notifications: the needs-you toast"; ED-NEW-dextra-as9-7): Armilla's needs-you pose is written once for
// each theme into the folder beside nth's other notification picture, only when its bytes differ, and the toast names it by path. A folder that cannot
// be written leaves the toast as text alone.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-7")]
public sealed class DecisionToastLogoTests
{
    private const string Folder = @"C:\nth\settings";

    [Fact]
    public void The_picture_is_a_96_px_png_written_once_for_the_theme_in_force()
    {
        var files = new RecordingToastIconFiles();
        var dark = true;
        var logo = new DecisionToastLogo(files, Folder, () => dark);
        var path = logo.Path();
        Assert.Equal(Path.Combine(Folder, "dextra-needs-you-dark.png"), path);
        Assert.Equal(path, logo.Path());
        Assert.Equal(1, files.Writes);
        using var bitmap = SKBitmap.Decode(files.Files[path!]);
        Assert.Equal(96, bitmap.Width);
        Assert.Equal(96, bitmap.Height);

        dark = false;                                                       // the owner switched Windows to the light mode
        var light = logo.Path();
        Assert.Equal(Path.Combine(Folder, "dextra-needs-you-light.png"), light);
        Assert.Equal(2, files.Writes);
        Assert.NotEqual(files.Files[path!], files.Files[light!]);
    }

    [Fact]
    public void A_picture_already_on_disk_with_the_same_bytes_is_not_written_again()
    {
        var files = new RecordingToastIconFiles();
        var first = new DecisionToastLogo(files, Folder, () => true).Path();
        var again = new DecisionToastLogo(files, Folder, () => true).Path();
        Assert.Equal(first, again);
        Assert.Equal(1, files.Writes);
    }

    [Fact]
    public void A_folder_that_cannot_be_written_gives_no_picture_and_no_error()
    {
        var logo = new DecisionToastLogo(new RefusingToastIconFiles(), Folder, () => true);
        Assert.Null(logo.Path());
        Assert.Null(logo.Path());
    }

    [Fact]
    public void The_apps_own_logo_is_built_without_writing_anything()
    {
        var logo = DecisionToastLogo.Production();
        Assert.NotNull(logo);                                               // built, never used by a test: nothing is written
    }
}

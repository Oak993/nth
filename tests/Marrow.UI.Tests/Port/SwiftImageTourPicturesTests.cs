using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-perf-memory-6: the eight welcome-tour pictures are 660 x 412 and 1320 x 824 files. Both used to be decoded for each page the tour showed and kept for
// the life of the process (about 42 MB). Now the 2x picture is decoded only when a draw at a render scaling of 1.5 or more (or a caller) asks for it, and the
// tour gives every picture back when it closes. The pictures themselves, and which one is drawn at which scaling, are unchanged (OD-0027).
[Collection("Headless")]
public sealed class SwiftImageTourPicturesTests(HeadlessSession headless)
{
    private static IDictionary Table() =>
        (IDictionary)typeof(SwiftImage).GetField("TourAssets", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static bool Decoded2x(string name)
    {
        var asset = Table()[name]!;
        var lazy = asset.GetType().GetProperty("Lazy2x")!.GetValue(asset)!;
        return (bool)lazy.GetType().GetProperty("IsValueCreated")!.GetValue(lazy)!;
    }

    [Fact]
    [Trait("Decision", "OD-0027")]
    public void A_tour_picture_decodes_its_2x_file_only_when_something_asks_for_it() => headless.OnUiThread(() =>
    {
        SwiftImage.ReleaseTourPictures();

        var image = SwiftImage.Named("tour-welcome");

        Assert.Equal(new PixelSize(660, 412), image.Bitmap!.PixelSize);
        Assert.False(Decoded2x("tour-welcome"));
        Assert.Equal(new PixelSize(1320, 824), image.Bitmap2x!.PixelSize);
        Assert.True(Decoded2x("tour-welcome"));
        Assert.Same(image.Bitmap2x, SwiftImage.Named("tour-welcome").Bitmap2x); // one decode, shared by every page view
        SwiftImage.ReleaseTourPictures();
    });

    [Fact]
    [Trait("Decision", "OD-0027")]
    public void Drawing_at_a_scaling_below_one_and_a_half_never_decodes_the_2x_file() => headless.OnUiThread(() =>
    {
        SwiftImage.ReleaseTourPictures();
        var image = SwiftImage.Named("tour-themes");
        image.Resizable();
        var window = new Window { Width = 660, Height = 412, Content = new Border { Background = Brushes.White, Child = image } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.RenderScaling < 1.5);

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.False(Decoded2x("tour-themes"));
        window.Close();
        SwiftImage.ReleaseTourPictures();
    });

    [Fact]
    [Trait("Decision", "OD-0027")]
    public void Releasing_the_tour_pictures_empties_the_table_and_the_next_tour_decodes_again() => headless.OnUiThread(() =>
    {
        SwiftImage.ReleaseTourPictures();
        var first = SwiftImage.Named("tour-hydra").Bitmap;
        _ = SwiftImage.Named("tour-pairs");
        Assert.Equal(2, Table().Count);

        SwiftImage.ReleaseTourPictures();

        Assert.Empty(Table());
        var again = SwiftImage.Named("tour-hydra");
        Assert.False(again.IsEmpty);
        Assert.NotSame(first, again.Bitmap);
        Assert.Equal(new PixelSize(660, 412), again.Bitmap!.PixelSize);
        SwiftImage.ReleaseTourPictures();
    });
}

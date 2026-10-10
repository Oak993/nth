using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Common;
using Marrow.UI.Port.Nth;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Nth;

// nth motion pack, site group D, the Marrow.UI sites: step 13 (the large image preview's blurred backdrop and glass puck), step 6
// (the video thumbnail's plate) and step 1 (the tool image preview's spinner). The expected pixels are derived from the review page
// (nth-motion-review.html step 13 build notes and .pv CSS): the skeleton colour plate, the blurred thumbnail zoomed 1.15, the light
// band at 35 % strength every 2.2 s, the puck rgba(18,20,26,.62) with a 1 px white 18 % rim.
[Collection("Headless")]
public sealed class NthSitesDTests(HeadlessSession headless)
{
    private const int W = 200, H = 150;

    private static NthFrame Frame(object? state, bool calm = false, bool dark = false, double started = 0) =>
        new(new Size(W, H), dark, calm, TimeProvider.System, started, state);

    private static SKBitmap Draw(PreviewBackdropPainter painter, PreviewBackdropState state, double now, bool calm = false, bool dark = false)
    {
        var bitmap = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bitmap);
        painter.Draw(canvas, Frame(state, calm, dark), now);
        return bitmap;
    }

    private static DecodedBitmap Solid(byte b, byte g, byte r, int size = 40)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++) { pixels[i * 4] = b; pixels[i * 4 + 1] = g; pixels[i * 4 + 2] = r; pixels[i * 4 + 3] = 255; }
        return new DecodedBitmap(size, size, size * 4, pixels);
    }

    // a rest moment of the band: the 2.2 s cycle travels for 1.54 s and rests after (NthMotion.SkeletonLight); Travel is mid-travel
    private const double Rest = 2.0, Travel = .8;

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Without_a_thumbnail_the_backdrop_is_the_skeleton_plate_and_the_light_crosses_it_at_35_percent()
    {
        using var painter = new PreviewBackdropPainter();
        var plain = NthPalette.Light.Skeleton;
        using var rest = Draw(painter, new PreviewBackdropState(null, null), Rest);
        Assert.Equal(plain, rest.GetPixel(100, 75)); // .pv background var(--sk)
        Assert.Equal(plain, rest.GetPixel(2, 2));
        using var moving = Draw(painter, new PreviewBackdropState(null, null), Travel);
        var brightest = 0;
        for (var x = 0; x < W; x++) for (var y = 0; y < H; y += 10) brightest = Math.Max(brightest, moving.GetPixel(x, y).Red);
        // white at most 35 % over the plate: 0xc6 + (0xff - 0xc6) * .35 = 218
        Assert.InRange(brightest, plain.Red + 5, 219);
        Assert.Equal(0, painter.BlursMade);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_light_runs_by_the_clock_and_a_calm_backdrop_has_none()
    {
        using var painter = new PreviewBackdropPainter();
        var state = new PreviewBackdropState(null, null);
        Assert.True(painter.Moving(Frame(state), 1));
        Assert.False(painter.Moving(Frame(state, calm: true), 1));
        using var calm = Draw(painter, state, Travel, calm: true);
        Assert.Equal(NthPalette.Light.Skeleton, calm.GetPixel(60, 60)); // calm: no light
        // dark theme: the dark skeleton plate
        using var dark = Draw(painter, state, Rest, dark: true);
        Assert.Equal(NthPalette.Dark.Skeleton, dark.GetPixel(100, 75));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void With_a_thumbnail_the_backdrop_shows_it_blurred_once_and_zoomed()
    {
        using var painter = new PreviewBackdropPainter();
        // left half red, right half blue: the blur softens the seam, the zoom 1.15 keeps the picture filling the frame
        var size = 40;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                if (x < size / 2) pixels[i + 2] = 255; else pixels[i] = 255;
                pixels[i + 3] = 255;
            }
        var thumbnail = new DecodedBitmap(size, size, size * 4, pixels);
        var state = new PreviewBackdropState(thumbnail, null);
        using var first = Draw(painter, state, Rest);
        Assert.Equal(1, painter.BlursMade);
        var left = first.GetPixel(10, 75); var right = first.GetPixel(W - 10, 75); var seam = first.GetPixel(W / 2, 75);
        Assert.True(left.Red > 200 && left.Blue < 40, $"left {left}");
        Assert.True(right.Blue > 200 && right.Red < 40, $"right {right}");
        Assert.InRange(seam.Red, 60, 200); // sigma 12 smears the seam: both colours meet here, neither pure
        Assert.InRange(seam.Blue, 60, 200);
        // the corners are covered too (the zoom leaves no plate showing at the edges)
        Assert.True(first.GetPixel(0, 0).Red > 150);
        // later frames reuse the blur; a new size makes another one
        using (Draw(painter, state, 1.0)) { }
        using (Draw(painter, state, 1.3)) { }
        Assert.Equal(1, painter.BlursMade);
        using var bitmap = new SKBitmap(W / 2, H / 2);
        using var canvas = new SKCanvas(bitmap);
        painter.Draw(canvas, new NthFrame(new Size(W / 2, H / 2), false, false, TimeProvider.System, 0, state), Rest);
        Assert.Equal(2, painter.BlursMade);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Arrival_takes_the_light_away_in_0_2_seconds_and_leaves_the_plate()
    {
        using var painter = new PreviewBackdropPainter();
        var arrived = new PreviewBackdropState(null, 0.5);
        Assert.True(painter.Moving(Frame(arrived), 0.6));
        Assert.False(painter.Moving(Frame(arrived), 0.8)); // faded: nothing moves any more
        using var after = Draw(painter, arrived, Travel); // 0.3 s after arrival, where the band would have crossed
        Assert.Equal(NthPalette.Light.Skeleton, after.GetPixel(100, 75));
        for (var x = 0; x < W; x += 7) Assert.Equal(NthPalette.Light.Skeleton, after.GetPixel(x, 40));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_control_hosts_the_painter_and_arrives_once() => headless.OnUiThread(() =>
    {
        var backdrop = new NthPreviewBackdrop(Solid(10, 20, 30));
        Assert.True(backdrop.HasThumbnail);
        Assert.False(backdrop.HasArrived);
        backdrop.Arrive();
        Assert.True(backdrop.HasArrived);
        Assert.False(new NthPreviewBackdrop().HasThumbnail);
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_puck_is_44_points_of_glass_holding_the_white_spinner_or_the_ring() => headless.OnUiThread(() =>
    {
        var puck = new PreviewPuck();
        Assert.Equal(44, puck.Width); Assert.Equal(44, puck.Height);
        Assert.Equal(new CornerRadius(22), puck.CornerRadius);
        Assert.Equal(new Thickness(1), puck.BorderThickness);
        var fill = Assert.IsAssignableFrom<ISolidColorBrush>(puck.Background).Color;
        Assert.Equal(new Color(158, 18, 20, 26), fill); // rgba(18,20,26,.62): .62 of 255 is 158
        var rim = Assert.IsAssignableFrom<ISolidColorBrush>(puck.BorderBrush).Color;
        Assert.Equal(new Color(46, 255, 255, 255), rim); // 1 px white 18 %: .18 of 255 is 46
        // unknown size: the step 1 spinner at 20 px in white
        var spinner = Assert.IsType<NthSpinner>(puck.Mark);
        Assert.Equal(20, spinner.Width); Assert.Equal(20, spinner.Height); Assert.Equal(NthSpinnerInk.Puck, spinner.Ink);
        Assert.False(puck.IsRing);
        // known progress: the step 4 ring at 24 px in the puck's white
        puck.Show(.4);
        var ring = Assert.IsType<NthRing>(puck.Mark);
        Assert.Equal(24, ring.Width); Assert.Equal(24, ring.Height); Assert.True(ring.Puck); Assert.Equal(.4, ring.Value);
        Assert.True(puck.IsRing);
        puck.Show(null);
        Assert.IsType<NthSpinner>(puck.Mark);
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_puck_leaves_with_opacity_and_a_scale_of_0_9_over_0_24_seconds() => headless.OnUiThread(() =>
    {
        var puck = new PreviewPuck();
        var window = new Window { Width = 300, Height = 300, Content = puck };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(puck.HasArrived);
        puck.Arrive();
        Assert.True(puck.HasArrived);
        Assert.Contains(puck.Transitions!, t => t is Avalonia.Animation.DoubleTransition { Duration: var d } && d == TimeSpan.FromSeconds(.24));
        // the transition carries it to opacity 0 and scale 0.9
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (puck.Opacity > 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The puck never left.");
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        Assert.NotNull(puck.RenderTransform);
        Assert.Equal(.9, puck.RenderTransform!.Value.M11, 3);
        puck.Arrive(); // once only
        window.Close();
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_picture_that_does_not_load_keeps_the_backdrop_and_the_puck() => headless.OnUiThread(() =>
    {
        var attachment = new Attachment { Name = "gone.png", Path = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid().ToString("N") + ".png"), MimeType = "image/png" };
        var preview = new AttachmentLargePreview(attachment);
        var window = new Window { Width = 600, Height = 600, Content = preview };
        window.Show();
        var until = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < until) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); }
        Assert.False(preview.Puck.HasArrived); // PR-COMPOSER-074 failure case: the loader stays
        Assert.False(preview.Backdrop.HasArrived);
        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(preview).OfType<NthPreviewBackdrop>());
        window.Close();
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_video_thumbnail_waits_on_the_step_6_plate_under_its_badge() => headless.OnUiThread(() =>
    {
        var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4"), MimeType = "video/mp4" };
        var thumbnail = new AttachmentVideoThumbnail(attachment);
        Assert.Equal(NthSkeletonKind.Plate, thumbnail.Plate.Kind);
        var panel = Assert.IsType<Panel>(thumbnail.Child);
        Assert.Same(thumbnail.Plate, panel.Children[0]); // the plate first, the badge over it
        Assert.IsType<AttachmentPlayBadge>(panel.Children[1]);
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_large_preview_blurs_the_thumbnail_the_strip_already_decoded() => headless.OnUiThread(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        var attachment = new Attachment { Name = "photo.png", Path = path, MimeType = "image/png" };
        Assert.False(new AttachmentLargePreview(attachment).Backdrop.HasThumbnail); // nothing was drawn for this file yet
        ThumbnailMemory.Store(Solid(1, 2, 3, 56), path, 56);
        Assert.True(new AttachmentLargePreview(attachment).Backdrop.HasThumbnail); // the 56 point thumbnail of the chat strip
        var other = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        ThumbnailMemory.Store(Solid(1, 2, 3, 48), other, 48);
        Assert.True(new AttachmentLargePreview(new Attachment { Name = "o.png", Path = other, MimeType = "image/png" }).Backdrop.HasThumbnail); // the composer's 48
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_tool_image_preview_waits_on_the_16_point_spinner() => headless.OnUiThread(() =>
    {
        var preview = new ToolImagePreview(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".png"));
        var window = new Window { Width = 400, Height = 300, Content = preview };
        window.Show();
        var spinner = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(preview).OfType<NthSpinner>());
        Assert.Equal(16, spinner.Width); Assert.Equal(16, spinner.Height);
        Assert.Equal(NthSpinnerInk.Mark, spinner.Ink);
        window.Close();
    });
}

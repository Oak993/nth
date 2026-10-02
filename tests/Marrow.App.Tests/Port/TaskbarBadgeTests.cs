using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests.Port;

// ED-219 (PORT-ONLY): the Dock badge of AppModel.swift:1270-1273 (`NSApp.dockTile.badgeLabel = unread > 0 ? String(unread) : nil`,
// unread = threads with hasUnread that are neither archived nor in a panel) becomes the main window's taskbar overlay icon: the
// count in white on a filled red circle (NSColor.systemRed, light value 255,59,48), at the system small-icon size, the decimal
// count however large (String(unread), no "99+"), nil clearing it, the count as text for the accessible description. The COM
// call is behind the ITaskbarOverlay seam; these tests bind a recorder and never touch the real taskbar.
public sealed class TaskbarBadgeTests
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273
    [Trait("Decision", "ED-219")]
    public void The_overlay_is_the_count_of_unread_threads_that_are_not_archived_or_in_a_panel()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        using var rig = new AppModelNotifyRig(project =>
        [
            Unread(project),
            Unread(project),
            Unread(project),
            Unread(project) with { IsArchived = true },
            Unread(project) with { IsInPanel = true },
            AppModelSamples.Thread(project),
        ]);

        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());

        var icon = Assert.IsType<TaskbarOverlayIcon>(overlay.Current);
        Assert.Equal(3, icon.Count);
        Assert.Equal("3", icon.Description);
        Assert.Equal(overlay.SmallIconSize, icon.Size);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1197, :1230, :1270-1273
    [Trait("Decision", "ED-219")]
    public void The_overlay_follows_the_count_up_when_chats_finish_and_down_when_they_are_read()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        using var rig = new AppModelNotifyRig(project => [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var first = rig.Threads[0].Id;
        var second = rig.Threads[1].Id;
        rig.Configure(chime: false, notify: false);

        rig.Finish(first, TurnStatus.Completed);
        Assert.Equal(1, overlay.Current!.Count);
        rig.Finish(second, TurnStatus.Completed);
        Assert.Equal(2, overlay.Current!.Count);
        Assert.Equal("2", overlay.Current.Description);
        rig.Harness.Main.OnMain(() => rig.Model.MarkRead(first));
        Assert.Equal(1, overlay.Current!.Count);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1197, :1230, :1270-1273 (nil label)
    [Trait("Decision", "ED-219")]
    public void Reading_the_last_unread_thread_clears_the_overlay()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        using var rig = new AppModelNotifyRig(project => [AppModelSamples.Thread(project)]);
        rig.Configure(chime: false, notify: false);
        rig.Finish(rig.Threads[0].Id, TurnStatus.Completed);
        Assert.NotNull(overlay.Current);

        rig.Harness.Main.OnMain(() => rig.Model.MarkRead(rig.Threads[0].Id));

        Assert.Null(overlay.Current);
        Assert.Equal(1, overlay.Calls.Count(call => call is null));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273 (nil when none)
    [Trait("Decision", "ED-219")]
    public void With_nothing_unread_the_model_asks_for_the_overlay_to_be_cleared_and_never_draws_a_zero()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        using var rig = new AppModelNotifyRig(project => [AppModelSamples.Thread(project), Unread(project) with { IsArchived = true }]);

        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());

        var call = Assert.Single(overlay.Calls);
        Assert.Null(call);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273 (String(unread), no cap)
    [Trait("Decision", "ED-219")]
    public void More_than_ninety_nine_unread_threads_show_the_whole_decimal_count()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        using var rig = new AppModelNotifyRig(project => Enumerable.Range(0, 120).Select(_ => Unread(project)).ToList());

        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());

        var icon = Assert.IsType<TaskbarOverlayIcon>(overlay.Current);
        Assert.Equal(120, icon.Count);
        Assert.Equal("120", icon.Description);
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1273
    [Trait("Decision", "ED-219")]
    [InlineData(1L, "1")]
    [InlineData(9L, "9")]
    [InlineData(10L, "10")]
    [InlineData(42L, "42")]
    [InlineData(99L, "99")]
    [InlineData(100L, "100")]
    [InlineData(150L, "150")]
    [InlineData(1000L, "1000")]
    [InlineData(12345L, "12345")]
    public void The_accessible_description_is_the_count_as_decimal_text_with_no_plus_sign(long count, string text)
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(count);

        var icon = Assert.IsType<TaskbarOverlayIcon>(overlay.Current);
        Assert.Equal(text, icon.Description);
        Assert.Equal(count, icon.Count);
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273
    [Trait("Decision", "ED-219")]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void The_icon_is_a_red_disc_with_a_white_count_drawn_at_the_system_small_icon_size(int size)
    {
        var overlay = new RecordingTaskbarOverlay { SmallIconSize = size };
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(5);

        var icon = Assert.IsType<TaskbarOverlayIcon>(overlay.Current);
        Assert.Equal(size, icon.Size);
        Assert.Equal(size * size * 4, icon.Bgra.Length);
        // A filled circle: the corners are outside it, the rim and the disc away from the text are systemRed light (255,59,48).
        Assert.Equal((byte)0, BadgePixels.At(icon, 0, 0).A);
        Assert.Equal((byte)0, BadgePixels.At(icon, size - 1, 0).A);
        Assert.Equal((byte)0, BadgePixels.At(icon, 0, size - 1).A);
        Assert.Equal((byte)0, BadgePixels.At(icon, size - 1, size - 1).A);
        Assert.True(BadgePixels.IsRed(icon, size / 2, 1), "top of the disc");
        Assert.True(BadgePixels.IsRed(icon, size / 2, size - 2), "bottom of the disc");
        Assert.True(BadgePixels.IsRed(icon, 1, size / 2), "left of the disc");
        Assert.True(BadgePixels.IsRed(icon, size - 2, size / 2), "right of the disc");
        // The count in white, centred.
        var white = BadgePixels.WhitePixels(icon);
        Assert.True(white.Count >= size / 2, "white text pixels: " + white.Count);
        Assert.InRange(white.Average(p => p.X + 0.5), size / 2.0 - 1.5, size / 2.0 + 1.5);
        Assert.InRange(white.Average(p => p.Y + 0.5), size / 2.0 - 1.5, size / 2.0 + 1.5);
        // Only red, white and their blend are drawn: every opaque pixel has full red.
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var (a, r, _, _) = BadgePixels.At(icon, x, y);
                if (a == 255) Assert.Equal((byte)255, r);
            }
        if (size >= 24) Assert.True(BadgePixels.HasPureWhite(icon), "the text is white where a stroke covers a pixel whole");
        // Straight alpha: a rim pixel the disc only partly covers keeps systemRed's own channels (within unpremultiplying's
        // rounding) at a lower alpha, never darkened toward black.
        var rimPixels = 0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var (a, r, g, b) = BadgePixels.At(icon, x, y);
                var distance = Math.Sqrt(Math.Pow(x + 0.5 - size / 2.0, 2) + Math.Pow(y + 0.5 - size / 2.0, 2));
                if (a < 64 || a == 255 || distance < size * 0.4) continue; // below a quarter, 8-bit unpremultiplying is too coarse to compare
                rimPixels++;
                Assert.InRange(r, (byte)253, (byte)255);
                Assert.InRange(g, (byte)56, (byte)62);
                Assert.InRange(b, (byte)45, (byte)51);
            }
        Assert.True(rimPixels > 0, "the disc's edge is antialiased");
    }

    [Fact]
    [Trait("Decision", "ED-219")] // owner, 2026-10-02: "Use Inter for the badge digits" (OD-0009's face); weight semibold
    public void The_count_is_set_in_Inter_semibold()
    {
        var face = TaskbarBadge.Typeface;
        Assert.Equal("Inter", face.FamilyName);
        Assert.Equal(600, face.FontWeight);
        Assert.Equal(SkiaSharp.SKFontStyleSlant.Upright, face.FontSlant);
        // Every digit has its own glyph in the face (none falls back to another font's).
        using var font = new SkiaSharp.SKFont(face);
        Assert.All("0123456789", digit => Assert.NotEqual((ushort)0, font.GetGlyph(digit)));
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273
    [Trait("Decision", "ED-219")]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(40)]
    public void The_icon_is_rendered_at_whatever_size_the_system_gives_for_the_window_dpi(int size)
    {
        var overlay = new RecordingTaskbarOverlay { SmallIconSize = size };
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(7);

        var icon = Assert.IsType<TaskbarOverlayIcon>(overlay.Current);
        Assert.Equal(size, icon.Size);
        Assert.Equal(size * size * 4, icon.Bgra.Length);
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1273 (no "99+": the text shrinks instead)
    [Trait("Decision", "ED-219")]
    [InlineData(42L)]
    [InlineData(100L)]
    [InlineData(150L)]
    [InlineData(1000L)]
    [InlineData(99999L)]
    public void A_long_count_is_shrunk_to_fit_inside_the_disc(long count)
    {
        var overlay = new RecordingTaskbarOverlay { SmallIconSize = 32 };
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        TaskbarBadge.Show(5);
        var single = overlay.Current!;
        TaskbarBadge.Show(count);
        var icon = overlay.Current!;
        var size = icon.Size;

        var white = BadgePixels.WhitePixels(icon);
        Assert.NotEmpty(white);
        // Every pixel of the text lies inside the disc, and the text is shorter than a single digit's.
        foreach (var (x, y) in white)
        {
            var distance = Math.Sqrt(Math.Pow(x + 0.5 - size / 2.0, 2) + Math.Pow(y + 0.5 - size / 2.0, 2));
            Assert.True(distance <= size / 2.0, $"({x},{y}) is outside the disc");
        }
        Assert.True(BadgePixels.TextHeight(icon) < BadgePixels.TextHeight(single),
            $"{count}: height {BadgePixels.TextHeight(icon)} against {BadgePixels.TextHeight(single)}");
        Assert.True(BadgePixels.TextWidth(icon) > BadgePixels.TextWidth(single));
        Assert.True(BadgePixels.TextWidth(icon) <= size - 2, "the text keeps clear of the disc's rim");
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1273 String(unread)
    [Trait("Decision", "ED-219")]
    public void Each_digit_and_its_place_changes_the_pixels()
    {
        var overlay = new RecordingTaskbarOverlay { SmallIconSize = 32 };
        using var scope = TaskbarBadge.Overlay.Override(overlay);
        var rendered = new Dictionary<long, byte[]>();
        foreach (var count in new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 21, 22, 88, 100, 101, 110 })
        {
            TaskbarBadge.Show(count);
            rendered[count] = overlay.Current!.Bgra;
        }

        var counts = rendered.Keys.ToList();
        for (var i = 0; i < counts.Count; i++)
            for (var j = i + 1; j < counts.Count; j++)
                Assert.False(rendered[counts[i]].SequenceEqual(rendered[counts[j]]), $"{counts[i]} and {counts[j]} draw the same pixels");
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1273 String(unread): digits in reading order
    [Trait("Decision", "ED-219")]
    [InlineData(18L, true)]
    [InlineData(81L, false)]
    public void The_digits_run_left_to_right_in_reading_order(long count, bool rightIsHeavier)
    {
        var overlay = new RecordingTaskbarOverlay { SmallIconSize = 32 };
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(count);

        // A one is a single stem and an eight is two closed loops, so the side an eight is on holds more white.
        var white = BadgePixels.WhitePixels(overlay.Current!);
        var left = white.Count(p => p.X < 16);
        var right = white.Count(p => p.X >= 16);
        Assert.Equal(rightIsHeavier, right > left);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1273
    [Trait("Decision", "ED-219")]
    public void The_same_count_always_draws_the_same_pixels()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(37);
        TaskbarBadge.Show(37);

        Assert.Equal(2, overlay.Calls.Count);
        Assert.True(overlay.Calls[0]!.Bgra.SequenceEqual(overlay.Calls[1]!.Bgra));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273
    [Trait("Decision", "ED-219")]
    public void Zero_clears_the_overlay_and_a_later_count_brings_it_back()
    {
        var overlay = new RecordingTaskbarOverlay();
        using var scope = TaskbarBadge.Overlay.Override(overlay);

        TaskbarBadge.Show(4);
        Assert.NotNull(overlay.Current);
        TaskbarBadge.Show(0);
        Assert.Null(overlay.Current);
        TaskbarBadge.Show(2);

        Assert.Equal(2, overlay.Current!.Count);
        Assert.Equal(3, overlay.Calls.Count);
    }

    [Fact]
    [Trait("Decision", "ED-219")]
    public void Unless_the_app_installs_the_Windows_overlay_the_seam_never_reaches_the_taskbar()
    {
        Assert.IsNotType<WindowsTaskbarOverlay>(TaskbarBadge.Overlay.Current);
        TaskbarBadge.Show(3);
        TaskbarBadge.Show(0);
    }

    private static ChatThread Unread(Project project) => AppModelSamples.Thread(project) with { HasUnread = true };
}

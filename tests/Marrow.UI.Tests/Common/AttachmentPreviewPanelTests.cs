using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/AttachmentPreviewPanel.swift (B2 1fbf81e), the members named by this
// part's records: AttachmentPreviewCoordinator (toggle :64-70, show :78-109, anchorTarget :117-147, startMonitors
// :160-187, handleMouseDown :192-201, handleKeyDown :203-207, contentSize :223-235, textSample :237-242),
// AttachmentPreviewSlot :258-295 (lazy panel construction). Expected values are transcribed from the Swift and the
// records, never from the C# under test: the tap-point hairline rect at :122-127 and :140-142, the 8x8 minimum
// strip size at :119, the four-step anchor fallback order, the three dismissal triggers, and the three content-size
// formulas at :226/:230/:234.
// Port shape read (ED-095, R-VIEW-9): NSPopover is an Avalonia Popup, shown and dismissed through the
// Presentation seam and recorded here by PopoverRecorder (Fixtures/PopoverRecorder.cs), because headless cannot
// open a themeless Popup. NSApp.currentEvent is Marrow.UI.Port.AppCurrentEvent, an app-wide observer of the
// PointerPressed/PointerReleased/KeyDown routed events' static Raised streams; since each test below opens its own
// Window, a stale event from an earlier test's (different, disposed) window can never satisfy this file's
// `ReferenceEquals(last.Root, ...)` checks, so no cross-test reset is needed except where a test relies on
// AppCurrentEvent.CurrentWindow resolving to ITS OWN window (WindowRectAnchor branch), where a priming key press
// is sent first. NSWindow's didResignKey/willMove/didMove notifications, which all only call close(), are
// simulated by invoking WindowBase's internal HandleDeactivated/HandlePositionChanged (headless has no real
// platform activation or move), because Window's own Deactivated/PositionChanged events cannot otherwise be
// raised from outside the platform backend.
[Collection("Headless")]
public sealed class AttachmentPreviewPanelTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-071")] // UI/Common/AttachmentPreviewPanel.swift:160-201 application-wide monitors, pointer passes through, Escape consumed
    public void Review_other_application_window_dismisses_the_panel(bool escape) => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder(); using var seam = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip); var other = Show(new Border { Width = 100, Height = 100 });
        var panel = new AttachmentPreviewCoordinator(); panel.SetAnchor(strip); panel.Toggle(Sample()); Dispatcher.UIThread.RunJobs();
        Assert.True(Assert.Single(recorder.Calls).Presented);
        if (escape)
        {
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            other.RaiseEvent(key); Assert.True(key.Handled);
        }
        else
        {
            var presses = 0;
            other.AddHandler(InputElement.PointerPressedEvent, (_, e) => { Assert.False(e.Handled); presses++; });
            Click(other, new Point(10, 10)); Assert.Equal(1, presses);
        }
        Assert.False(recorder.Calls[^1].Presented);
        panel.Close(); window.Close(); other.Close(); Dispatcher.UIThread.RunJobs();
    });
    private static Window Show(Control content, double width = 400, double height = 300)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Attachment Sample(string name = "a.txt", string path = "Z:\\does-not-exist\\a.txt", string mimeType = "text/plain") =>
        new() { Name = name, Path = path, MimeType = mimeType };

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    /// Fires WindowBase's own Deactivated event, standing in for AppKit's didResignKeyNotification (:174-177):
    /// headless has no real window activation to drive it from outside.
    private static void SimulateDeactivate(Window window) =>
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null);

    /// Fires WindowBase's own PositionChanged event, standing in for willMove/didMoveNotification (:178-185).
    private static void SimulateMove(Window window) =>
        typeof(WindowBase).GetMethod("HandlePositionChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [new PixelPoint(1, 1)]);

    // ---- PR-COMPOSER-069: one shared panel per strip, toggle opens/swaps/closes ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-069")] // AttachmentPreviewPanel.swift:64-70 toggle: first tap opens
    public void First_toggle_shows_the_popup_and_records_the_attachment_as_current() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample();

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented); // :106-107 popover.show
        Assert.Same(coordinator.PopupForTests, popup);
        Assert.Equal(attachment.Id, coordinator.CurrentIDForTests); // :88
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-069")] // AttachmentPreviewPanel.swift:64-66, :90 currentID == id && isShown
    public void Second_toggle_of_the_same_attachment_closes_the_popup() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample();

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();
        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, recorder.Calls.Count);
        Assert.True(recorder.Calls[0].Presented);
        Assert.False(recorder.Calls[1].Presented); // :66 close()
        Assert.Null(coordinator.CurrentIDForTests); // :74
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-069")] // AttachmentPreviewPanel.swift:67-68, :98-107 a different id swaps and re-shows
    public void Toggling_a_different_attachment_while_shown_swaps_content_and_shows_again() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var first = Sample("a.txt", "Z:\\a.txt");
        var second = Sample("b.txt", "Z:\\b.txt");

        coordinator.Toggle(first);
        Dispatcher.UIThread.RunJobs();
        coordinator.Toggle(second);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, recorder.Calls.Count);
        Assert.True(recorder.Calls[0].Presented);
        Assert.True(recorder.Calls[1].Presented); // re-shown, not closed first
        Assert.Equal(second.Id, coordinator.CurrentIDForTests);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-069")] // AttachmentPreviewPanel.swift:265-272 the coordinator is built once, lazily
    public void Slots_panel_is_the_same_coordinator_on_every_access() => headless.OnUiThread(() =>
    {
        var slot = new AttachmentPreviewSlot();
        var first = slot.Panel;
        var second = slot.Panel;
        Assert.Same(first, second); // :266-267 if let coordinator { return coordinator }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-069")] // AttachmentPreviewPanel.swift:286-294: a slot that never opened does nothing
    public void A_slot_that_never_opened_a_panel_closes_and_retires_without_effect()
    {
        // UNTESTABLE (black box): that a never-shown row's slot never even constructs a Popup cannot be observed
        // without reflecting into the private field, since Close()/Retire() are themselves no-ops either way; the
        // externally visible half of the claim -- these calls never present anything and never throw -- is what
        // this test proves.
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var slot = new AttachmentPreviewSlot();
        slot.Close();
        slot.Retire(new HashSet<Guid>());
        Assert.Empty(recorder.Calls);
    }

    // ---- PR-COMPOSER-070: anchor fallback order ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:117-129 the tap point inside the strip, clamped
    public void Anchor_prefers_the_tap_point_inside_the_strip_when_it_is_at_least_8_by_8() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 120, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        Click(window, new Point(80, 20)); // strip fills the window at (0,0), so this is strip-local too

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Same(strip, coordinator.PopupForTests.PlacementTarget);
        Assert.Equal(new Rect(79, 0, 2, 40), coordinator.PopupForTests.PlacementRect); // :123, :127
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:123 clamped 2 points in from each strip edge
    public void Anchor_tap_point_clamps_2_points_in_from_the_strip_edges() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 120, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        Click(window, new Point(0, 5)); // past the left edge

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Rect(1, 0, 2, 40), coordinator.PopupForTests.PlacementRect); // x clamped to 2, rect.x = 2-1
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:119 a strip smaller than 8x8 is not used as tap anchor
    public void A_strip_smaller_than_8_by_8_falls_back_past_the_tap_point() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 4, Height = 4 };
        var window = Show(strip, width: 200, height: 200);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        Click(window, new Point(2, 2));

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Same(strip, coordinator.PopupForTests.PlacementTarget); // :133-134 falls to the strip's own bounds
        Assert.Equal(new Rect(strip.Bounds.Size), coordinator.PopupForTests.PlacementRect);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:130-131 the thumbnail's own bounds
    public void Anchor_falls_back_to_the_tapped_thumbnails_own_bounds_when_no_click_matches() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 120, Height = 40 };
        var thumbnail = new Border { Width = 28, Height = 28 };
        var root = new Panel { Children = { strip, thumbnail } };
        var window = Show(root);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        // No click: keyboard/VoiceOver activation carries no click (:60-61).

        coordinator.Toggle(Sample(), over: thumbnail);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(thumbnail, coordinator.PopupForTests.PlacementTarget);
        Assert.Equal(new Rect(thumbnail.Bounds.Size), coordinator.PopupForTests.PlacementRect); // :131
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:133-134 the strip's own bounds, last view fallback
    public void Anchor_falls_back_to_the_strips_own_bounds_with_no_thumbnail_and_no_click() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 120, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Same(strip, coordinator.PopupForTests.PlacementTarget);
        Assert.Equal(new Rect(strip.Bounds.Size), coordinator.PopupForTests.PlacementRect); // :134
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:136-144 the window-rect anchor, no view of its own
    public void Anchor_uses_the_window_rect_when_there_is_no_view_anchor_at_all() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var placeholder = new Border();
        var window = Show(placeholder);
        // Primes AppCurrentEvent.CurrentWindow to resolve to this test's own window (:316 NSApp.currentEvent?.window).
        window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
        var coordinator = new AttachmentPreviewCoordinator();
        var windowRect = new Rect(10, 20, 30, 15);
        coordinator.SetAnchor(windowRect);

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Same(window, coordinator.PopupForTests.PlacementTarget); // :318-321 (view: content, ...)
        Assert.Equal(windowRect, coordinator.PopupForTests.PlacementRect); // no matching click: the plain rect (:144)
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-070")] // AttachmentPreviewPanel.swift:139-142 the window-rect anchor, tap x preferred
    public void Anchor_window_rect_prefers_the_tap_x_when_a_click_matches() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var placeholder = new Border();
        var window = Show(placeholder, width: 200, height: 200);
        var coordinator = new AttachmentPreviewCoordinator();
        var windowRect = new Rect(10, 20, 30, 15);
        coordinator.SetAnchor(windowRect);
        Click(window, new Point(25, 27)); // inside windowRect, x clamped between 12 and 38

        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Rect(24, 20, 2, 15), coordinator.PopupForTests.PlacementRect); // x=25, rect.x = 25-1
        window.Close();
    });

    // ---- PR-COMPOSER-071: dismissal ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-071")] // AttachmentPreviewPanel.swift:192-199 any click off the strip closes
    public void A_click_outside_the_strip_closes_the_panel_and_still_reaches_its_target() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var root = new Panel { Children = { strip } };
        var window = Show(root, width: 200, height: 200);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();
        Assert.True(recorder.Calls[0].Presented);
        // A default (Bubble, not handledEventsToo) handler on the window sees a click exactly when it was never
        // marked handled: this stands in for ":193-198 the click still reaches whatever it hit", which headless's
        // hit-testing (always resolving to its own ContentPresenter, never descending into a leaf) cannot itself
        // exercise for an arbitrary descendant target.
        var windowSawUnhandledClick = false;
        window.AddHandler(InputElement.PointerPressedEvent, (_, _) => windowSawUnhandledClick = true);

        Click(window, new Point(150, 150)); // outside the strip, which sits at the window's origin

        Assert.Equal(2, recorder.Calls.Count);
        Assert.False(recorder.Calls[1].Presented); // :199 close()
        Assert.True(windowSawUnhandledClick); // :199-201 return event: never marked handled, so bubble still sees it
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-071")] // AttachmentPreviewPanel.swift:195-196 a click anywhere on the strip passes through
    public void A_click_anywhere_on_the_strip_itself_does_not_close_the_panel() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        Click(window, new Point(90, 5)); // still on the strip, away from the earlier tap point

        Assert.Single(recorder.Calls); // no second (close) call
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-071")] // AttachmentPreviewPanel.swift:203-207 Escape closes and swallows the key
    public void Escape_closes_the_panel_and_the_key_never_reaches_the_composer() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var composerSawKey = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) composerSawKey = true;
        }); // default bubble handler: only reached while the key is not yet Handled
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, recorder.Calls.Count);
        Assert.False(recorder.Calls[1].Presented); // :205 close()
        Assert.False(composerSawKey); // :206 return nil: swallowed before the bubble phase
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-071")] // AttachmentPreviewPanel.swift:174-177 didResignKeyNotification closes
    public void The_window_losing_key_closes_the_panel() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        SimulateDeactivate(window);

        Assert.Equal(2, recorder.Calls.Count);
        Assert.False(recorder.Calls[1].Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-071")] // AttachmentPreviewPanel.swift:178-185 willMove/didMoveNotification closes
    public void The_window_moving_closes_the_panel() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        coordinator.Toggle(Sample());
        Dispatcher.UIThread.RunJobs();

        SimulateMove(window);

        Assert.Equal(2, recorder.Calls.Count);
        Assert.False(recorder.Calls[1].Presented);
        window.Close();
    });

    // ---- PR-COMPOSER-072: fixed size, decided once before the panel shows ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:226 imageBounds.width+24, ceil(imageSize.height)+60
    public void An_image_attachment_sizes_the_popup_from_imageBounds_plus_chrome() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        // An unreadable header falls back to a 4:3 aspect that exactly fills imageBounds (AttachmentPreview.swift:247),
        // scale = min(480/4, 360/3) = 120, so the displayed size is imageBounds itself (480, 360).
        var attachment = Sample("photo.png", "Z:\\does-not-exist\\photo.png", "image/png");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(AttachmentLargePreview.ImageBounds.Width + 24, frame.Width);
        Assert.Equal(System.Math.Ceiling(AttachmentLargePreview.ImageBounds.Height) + 60, frame.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:82, :226; failure case: no cached video size opens at imageBounds
    public void A_video_attachment_with_no_cached_size_sizes_the_popup_from_the_full_bounds() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample("clip.mp4", "Z:\\does-not-exist\\clip.mp4", "video/mp4");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(AttachmentLargePreview.ImageBounds.Width + 24, frame.Width); // :284 videoSizes[path] ?? imageBounds
        Assert.Equal(AttachmentLargePreview.ImageBounds.Height + 60, frame.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:230, :237-242 a readable text file sizes by character count
    public void A_readable_text_attachment_sizes_the_popup_from_its_character_count() => headless.OnUiThread(() =>
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "notes.txt");
        var text = new string('a', 2_000); // count/4 = 500, clamped to 320
        File.WriteAllText(path, text);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample("notes.txt", path, "text/plain");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(464, frame.Width); // :230
        Assert.Equal(320 + 60, frame.Height); // min(320, max(120, 500)) + 60
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:230 the height floor at 120 for a short text file
    public void A_short_text_attachment_floors_the_popup_height_at_120_plus_chrome() => headless.OnUiThread(() =>
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "short.txt");
        File.WriteAllText(path, "hi"); // count/4 = 0, floored to 120
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample("short.txt", path, "text/plain");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(120 + 60, frame.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:239 a text file 500,000 bytes or larger is not sampled
    public void A_text_file_at_the_500000_byte_ceiling_falls_back_to_the_plain_box() => headless.OnUiThread(() =>
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "big.txt");
        File.WriteAllBytes(path, new byte[500_000]);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample("big.txt", path, "text/plain");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(344, frame.Width); // :234
        Assert.Equal(100, frame.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-072")] // AttachmentPreviewPanel.swift:234 neither an image, a video, nor readable text
    public void A_non_image_attachment_with_no_readable_file_sizes_the_plain_344_by_100_box() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var strip = new Border { Width = 100, Height = 40 };
        var window = Show(strip);
        var coordinator = new AttachmentPreviewCoordinator();
        coordinator.SetAnchor(strip);
        var attachment = Sample("app.bin", "Z:\\does-not-exist\\app.bin", "application/octet-stream");

        coordinator.Toggle(attachment);
        Dispatcher.UIThread.RunJobs();

        var frame = Assert.IsType<Border>(coordinator.PopupForTests.Child);
        Assert.Equal(344, frame.Width);
        Assert.Equal(100, frame.Height);
        window.Close();
    });
}

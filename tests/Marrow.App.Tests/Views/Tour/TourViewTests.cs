using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourView.swift (B2 1fbf81e), :1-277.

[Collection("Headless")]
public sealed class TourViewTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window ShowInFrame(Control content)
    {
        var window = new Avalonia.Controls.Window
        {
            RequestedThemeVariant = ThemeVariant.Light,
            SizeToContent = SizeToContent.WidthAndHeight, // otherwise Stretch fills the window's own default size
            Content = content,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TourPage[] SamplePages() =>
    [
        new("tour-welcome", "Welcome to Droppy Code", "One native macOS window for every coding agent you use."), // :9-10 TourPages.swift
        new("tour-hydra", "Meet Hydra", "One chat, many heads."),
        new("tour-pairs", "Pair a strong lead with quick heads", "Put a Claude lead over Gemini or OpenCode heads."),
        new("tour-slider", "Effort, on a slider", "Drag from low to max on any model."),
        new("tour-threads", "Threads, when you want them", "Hide the sidebar and the chat has the whole window."),
        new("tour-recipes", "Recipes for a team", "Hydra's cookbook holds ready-made pairs."),
        new("tour-panels", "Panels that float", "Changes, the terminal and the team's panel dock to any corner."),
        new("tour-themes", "Make it yours", "Twenty-six themes, from Catppuccin to Kanagawa."),
    ];

    // PR-UPDATE-099: Continue advances one page; Done finishes; no wrap-around; Return is the default action.
    [Fact]
    [Trait("Req", "PR-UPDATE-099")] // App/Views/Tour/TourView.swift:204-246
    public void Continue_advances_pages_1_to_7_and_the_last_page_reads_Done_and_finishes()
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        using var clockScope = SwiftClock.Provider.Override(time);
        using var frameSource = FrameClock.Source.Override(frames);
        void Settle()
        {
            time.Advance(System.TimeSpan.FromSeconds(0.25));
            frames.Advance(System.TimeSpan.FromSeconds(0.25));
        }
        headless.OnUiThread(() =>
        {
        var finished = 0;
        var view = new TourView(SamplePages(), onFinish: () => finished++);
        ShowInFrame(view);

        for (var page = 0; page < 7; page++)
        {
            var button = ButtonFor(view);
            Assert.Equal("Continue", TextOf(button)); // pages 1-7 (index 0-6): not yet the last of 8 pages
            Assert.True(button.IsDefault); // :211 .keyboardShortcut(.defaultAction)
            view.Advance();
            Settle();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(page + 1, view.CurrentIndex);
        }

        Assert.Equal("Done", TextOf(ButtonFor(view)));
        Assert.Equal(0, finished);
        view.Advance(); // on the last page
        Assert.Equal(1, finished);
        Assert.Equal(7, view.CurrentIndex); // no wrap-around: still the last page
        });
    }

    // PR-UPDATE-100 / UI-UPDATE-040: the back control is present, clickable and a no-op on page 1.
    [Fact]
    [Trait("Req", "PR-UPDATE-100")] // App/Views/Tour/TourView.swift:185-188,248-252
    public void GoBack_on_page_one_is_a_harmless_no_op_and_the_control_stays_hit_testable() => headless.OnUiThread(() =>
    {
        var view = new TourView(SamplePages());
        ShowInFrame(view);
        var back = view.GetVisualDescendants().OfType<Button>().First(b => SymbolOf(b) == "chevron.left");
        var backHost = (Control)back.GetVisualParent()!; // BuildIconButton's chromeGlassCircle() wrapper carries the opacity

        Assert.Equal(0.0, backHost.Opacity); // :188 .opacity(currentIndex > 0 ? 1 : 0)
        Assert.True(back.IsHitTestVisible); // opacity 0, not disabled, not removed
        Assert.True(back.IsEnabled);

        view.GoBack();
        Assert.Equal(0, view.CurrentIndex); // :249-251 newIndex == currentIndex -> early return
    });

    // PR-UPDATE-101: paging crossfades over exactly 0.25s ease-in-out.
    [Fact]
    [Trait("Req", "PR-UPDATE-101")] // App/Views/Tour/TourView.swift:96,120-122,179-180
    public void Paging_crossfades_the_artwork_and_the_bottom_panel_over_a_quarter_second()
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        using var clockScope = SwiftClock.Provider.Override(time);
        using var frameSource = FrameClock.Source.Override(frames);
        headless.OnUiThread(() =>
        {
            var view = new TourView(SamplePages());
            var artworkHost = view.ArtworkHost;
            var bottomHost = view.BottomHost;
            ShowInFrame(view);

            Assert.Single(artworkHost.Children);
            Assert.Single(bottomHost.Children);

            view.Advance();
            Dispatcher.UIThread.RunJobs();
            // Mid-crossfade: both the leaving and the arriving instance are present together.
            Assert.Equal(2, artworkHost.Children.Count);
            Assert.Equal(2, bottomHost.Children.Count);

            time.Advance(System.TimeSpan.FromSeconds(0.25));
            frames.Advance(System.TimeSpan.FromSeconds(0.25));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(artworkHost.Children); // the leaving instance has been removed
            Assert.Single(bottomHost.Children);
            Assert.Equal(1, view.CurrentIndex);
        });
    }

    // PR-UPDATE-102 / UI-UPDATE-054: a missing named asset degrades to the purple-to-black gradient with a photo glyph.
    [Fact]
    [Trait("Req", "PR-UPDATE-102")] // App/Views/Tour/TourView.swift:254-276
    [Trait("Req", "UI-UPDATE-054")]
    public void A_missing_tour_image_shows_the_gradient_and_photo_placeholder() => headless.OnUiThread(() =>
    {
        // RR-22 (ED-NEW-testing-lead-1 (12)): the eight tour pictures now ship, so the safety net is proved on a page whose
        // image name is in no asset catalogue; the assertions are those of the record, unchanged.
        var view = new TourView([new TourPage("tour-not-in-the-catalogue", "Welcome to Droppy Code", "One native macOS window for every coding agent you use.")]);
        ShowInFrame(view);

        Assert.DoesNotContain(view.GetVisualDescendants().OfType<SwiftImage>(), i => i.AssetName == "tour-not-in-the-catalogue" && i.Bitmap is not null);
        var glyph = view.GetVisualDescendants().OfType<SwiftImage>().Single(i => i.SymbolName == "photo"); // :270
        var gradientHost = glyph.GetVisualAncestors().OfType<Border>().First(b => b.Background is LinearGradientBrush);
        var brush = Assert.IsType<LinearGradientBrush>(gradientHost.Background);
        Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint); // :266 .top
        Assert.Equal(new RelativePoint(0, 1, RelativeUnit.Relative), brush.EndPoint); // :267 .bottom
        Assert.Equal(Color.FromRgb(41, 31, 102), brush.GradientStops[0].Color); // :263 (0.16, 0.12, 0.40)
        Assert.Equal(Color.FromRgb(13, 13, 26), brush.GradientStops[1].Color); // :264 (0.05, 0.05, 0.10)
    });

    // RR-22 (OD-0027): every page of Tour.Pages names a picture retaken from Windows Marrow, shipped at 660 x 412 and 1320 x 824,
    // so the tour shows the app, never the placeholder.
    [Fact]
    [Trait("Decision", "OD-0027")]
    public void Every_tour_page_shows_its_Windows_picture_at_1x_and_2x() => headless.OnUiThread(() =>
    {
        foreach (var page in Marrow.App.Views.Tour.Tour.Pages)
        {
            var image = SwiftImage.Named(page.ImageName);
            Assert.False(image.IsEmpty, page.ImageName);
            Assert.Equal(new PixelSize(660, 412), image.Bitmap!.PixelSize);
            Assert.Equal(new PixelSize(1320, 824), image.Bitmap2x!.PixelSize);
        }
        var view = new TourView(Marrow.App.Views.Tour.Tour.Pages);
        ShowInFrame(view);
        Assert.Contains(view.GetVisualDescendants().OfType<SwiftImage>(), i => i.AssetName == Marrow.App.Views.Tour.Tour.Pages[0].ImageName && i.Bitmap is not null);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<SwiftImage>(), i => i.SymbolName == "photo");
    });

    // PR-UPDATE-103 / UI-UPDATE-045: an empty page list shows a 220pt empty state with no controls.
    [Fact]
    [Trait("Req", "PR-UPDATE-103")] // App/Views/Tour/TourView.swift:100-114
    [Trait("Req", "UI-UPDATE-045")]
    public void An_empty_page_list_shows_the_220pt_empty_state_with_no_controls() => headless.OnUiThread(() =>
    {
        var view = new TourView([]);
        ShowInFrame(view);

        Assert.Equal(TourView.EmptyStateHeight, view.Bounds.Height);
        Assert.Equal(220, TourView.EmptyStateHeight);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No tour pages"); // :105
        var glyph = view.GetVisualDescendants().OfType<SwiftImage>().Single();
        Assert.Equal("rectangle.stack", glyph.SymbolName); // :102
        Assert.Empty(view.GetVisualDescendants().OfType<Button>()); // no primary button, no icon controls
    });

    // R-TEST-6: an expectation resting on open oracle item O-64 cannot also carry UI-UPDATE-045's Req trait, so the
    // title's weight is pinned here, apart from the structural facts above.
    [Fact]
    [Trait("Decision", "ED-186")]
    [Trait("Oracle", "O-64")]
    public void The_empty_state_titles_plain_headline_stays_bold() => headless.OnUiThread(() =>
    {
        var view = new TourView([]);
        ShowInFrame(view);

        var text = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "No tour pages"); // :106
        Assert.Equal(FontWeight.Bold, TextElement.GetFontWeight(text)); // plain .headline is the system style (ED-186); O-64 unresolved
    });

    // PR-UPDATE-104: only the initial index is clamped into range.
    [Fact]
    [Trait("Req", "PR-UPDATE-104")] // App/Views/Tour/TourView.swift:55,229-232
    public void The_initial_page_index_is_clamped_into_range()
    {
        Assert.Equal(0, TourView.Clamped(-1, 8));
        Assert.Equal(7, TourView.Clamped(100, 8));
        Assert.Equal(0, TourView.Clamped(0, 0));
        Assert.Equal(3, TourView.Clamped(3, 8));

        headless.OnUiThread(() =>
        {
            var below = new TourView(SamplePages(), initialPageIndex: -5);
            Assert.Equal(0, below.CurrentIndex);
            var above = new TourView(SamplePages(), initialPageIndex: 99);
            Assert.Equal(7, above.CurrentIndex);
        });
    }

    // PR-UPDATE-105: two pages with identical text are still distinct (their minted ids differ).
    [Fact]
    [Trait("Req", "PR-UPDATE-105")] // App/Views/Tour/TourView.swift:9-21
    public void Two_pages_with_identical_text_are_still_distinct()
    {
        var a = new TourPage("tour-welcome", "Welcome", "Same text");
        var b = new TourPage("tour-welcome", "Welcome", "Same text");
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a, b); // default structural equality includes Id
    }

    // PR-UPDATE-106 / UI-UPDATE-039: the indicator collapses to one accessibility element labelled "Page N of M".
    [Fact]
    [Trait("Req", "PR-UPDATE-106")] // App/Views/Tour/TourView.swift:296-297
    public void The_indicator_announces_its_page_position_as_one_element() => headless.OnUiThread(() =>
    {
        var indicator = new TourPageIndicator(totalPages: 8, currentIndex: 2);
        Assert.Equal("Page 3 of 8", Avalonia.Automation.AutomationProperties.GetName(indicator));

        var empty = new TourPageIndicator(totalPages: 0, currentIndex: 0);
        Assert.Equal("Page 1 of 1", Avalonia.Automation.AutomationProperties.GetName(empty)); // max(totalPages, 1)
    });

    // UI-UPDATE-037: the artwork band is 660 wide, 413 tall (660/1.6 rounded), clipped.
    [Fact]
    [Trait("Req", "UI-UPDATE-037")] // App/Views/Tour/TourView.swift:58-60,116-122
    public void The_artwork_band_is_660_by_413_and_clipped()
    {
        Assert.Equal(413.0, SwiftNumber.Rounded(660.0 / TourView.ImageAspectRatio));
        headless.OnUiThread(() =>
        {
            var view = new TourView(SamplePages());
            ShowInFrame(view);
            var artworkHost = view.ArtworkHost;
            var framed = Assert.IsType<Border>(artworkHost.Children[0]);
            Assert.Equal(660, framed.Width);
            Assert.Equal(413, framed.Height);
            Assert.True(framed.ClipToBounds); // :120 .clipped()
        });
    }

    // UI-UPDATE-038: the gradient seat is 96pt tall, bottom-anchored and takes no hits.
    [Fact]
    [Trait("Req", "UI-UPDATE-038")] // App/Views/Tour/TourView.swift:124-137
    public void The_gradient_seat_is_96_points_tall_bottom_anchored_and_ignores_hits() => headless.OnUiThread(() =>
    {
        var view = new TourView(SamplePages());
        ShowInFrame(view);
        var seat = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Background is LinearGradientBrush g && g.GradientStops.Count == 3);
        Assert.Equal(96, seat.Height);
        Assert.Equal(VerticalAlignment.Bottom, seat.VerticalAlignment);
        Assert.False(seat.IsHitTestVisible);
        var brush = (LinearGradientBrush)seat.Background!;
        Assert.Equal(0, brush.GradientStops[0].Offset);
        Assert.Equal(0.55, brush.GradientStops[1].Offset);
        Assert.Equal(1.0, brush.GradientStops[2].Offset);
        Assert.Equal(Colors.Transparent, brush.GradientStops[0].Color);
    });

    // UI-UPDATE-039: the current capsule is 24x8 and bright, the others 8x8 and dim.
    [Fact]
    [Trait("Req", "UI-UPDATE-039")] // App/Views/Tour/TourView.swift:279-298
    public void The_current_capsule_is_wider_and_brighter_than_the_others() => headless.OnUiThread(() =>
    {
        var indicator = new TourPageIndicator(totalPages: 3, currentIndex: 1);
        ShowInFrame(indicator);
        var dots = indicator.GetVisualDescendants().OfType<Border>().ToArray();
        Assert.Equal(3, dots.Length);
        for (var i = 0; i < dots.Length; i++)
        {
            Assert.Equal(8, dots[i].Height);
            Assert.Equal(i == 1 ? 24.0 : 8.0, dots[i].Width);
            var color = Assert.IsType<SolidColorBrush>(dots[i].Background).Color;
            var expectedAlpha = i == 1 ? 0.95 : 0.32;
            Assert.Equal((byte)System.Math.Round(expectedAlpha * 255), color.A);
            Assert.Equal(255, color.R);
            Assert.Equal(255, color.G);
            Assert.Equal(255, color.B);
        }
    });

    // UI-UPDATE-040: the back control is a 32x32 chevron.left icon button in a glass circle.
    [Fact]
    [Trait("Req", "UI-UPDATE-040")] // App/Views/Tour/TourView.swift:183-202,214-223
    public void The_back_control_is_a_32_by_32_chevron_left_icon_button() => headless.OnUiThread(() =>
    {
        var view = new TourView(SamplePages());
        ShowInFrame(view);
        var back = view.GetVisualDescendants().OfType<Button>().First(b => SymbolOf(b) == "chevron.left");
        var frame = back.GetVisualDescendants().OfType<Border>().First(b => b.Child is SwiftImage);
        Assert.Equal(32, frame.Width);
        Assert.Equal(32, frame.Height);
    });

    // UI-UPDATE-041: the close control is a checkmark, always visible, and closes the tour.
    [Fact]
    [Trait("Req", "UI-UPDATE-041")] // App/Views/Tour/TourView.swift:192-198,214-223
    public void The_close_control_is_a_checkmark_always_visible_and_calls_onClose() => headless.OnUiThread(() =>
    {
        var closed = 0;
        var view = new TourView(SamplePages(), onClose: () => closed++);
        ShowInFrame(view);
        var close = view.GetVisualDescendants().OfType<Button>().First(b => SymbolOf(b) == "checkmark");
        Assert.Equal(1.0, close.Opacity); // always visible, on every page
        close.Command?.Execute(null);
        var _ = close.Command; // no command path used; invoke via click event instead
        RaiseClick(close);
        Assert.Equal(1, closed); // :193-194 onClose() is called, never onFinish
    });

    // UI-UPDATE-042 / UI-UPDATE-043: the title is 28pt bold, the description is .body (13pt, ED-059), both centred.
    [Fact]
    [Trait("Req", "UI-UPDATE-042")] // App/Views/Tour/TourView.swift:156-160
    [Trait("Req", "UI-UPDATE-043")] // App/Views/Tour/TourView.swift:162-167
    public void The_title_is_28_point_bold_and_the_description_is_body_both_centred() => headless.OnUiThread(() =>
    {
        var pages = SamplePages();
        var view = new TourView(pages);
        ShowInFrame(view);
        var title = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == pages[0].Title);
        Assert.Equal(28, TextElement.GetFontSize(title));
        Assert.Equal(FontWeight.Bold, TextElement.GetFontWeight(title));
        Assert.Equal(TextAlignment.Center, title.TextAlignment);

        var description = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == pages[0].Description);
        Assert.Equal(13, TextElement.GetFontSize(description)); // ED-059 .body table
        Assert.Equal(TextAlignment.Center, description.TextAlignment);
    });

    // UI-UPDATE-044: the primary button is 220x42, labelled Continue/Done, and carries the default action.
    [Fact]
    [Trait("Req", "UI-UPDATE-044")] // App/Views/Tour/TourView.swift:204-212
    public void The_primary_button_is_220_by_42_and_switches_label_on_the_last_page() => headless.OnUiThread(() =>
    {
        var view = new TourView(SamplePages(), continueButtonTitle: "Continue", finishButtonTitle: "Done");
        ShowInFrame(view);
        var button = ButtonFor(view);
        var frame = button.GetVisualDescendants().OfType<Border>().First(b => b.Child is TextBlock);
        Assert.Equal(220, frame.Width);
        Assert.Equal(42, frame.Height);
        Assert.Equal("Continue", TextOf(button));
    });

    // UI-UPDATE-036: the card's own shape, glass background and hairline border (TourView.swift's part of this
    // record; the window-level key/main capability and drag-by-background belong to TourWindowController.swift,
    // a separate unit, and are not testable here).
    [Fact]
    [Trait("Req", "UI-UPDATE-036")] // App/Views/Tour/TourView.swift:79-95
    public void The_card_fills_the_windows_rectangle_with_glass_and_a_hairline_border() => headless.OnUiThread(() =>
    {
        var view = new TourView(SamplePages());
        ShowInFrame(view);

        // :63, :80-95 shape = RoundedRectangle(cornerRadius: 20, style: .continuous) is the Mac window's own shape (a clear window the size of the card). The port's window is a
        // rectangle of the system's Acrylic that DWM rounds by 8 px at most, so (OD-0075, the owner's intent of 2026-10-05, ED-338) the scrim, the glass and the
        // clip are that rectangle and the card reads as glass out to the OS edge; TourWindowCornerTests proves the corner pixels.
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius == new CornerRadius(20));

        var glass = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => SwiftGlass.GetEffect(b) is { Shape.Kind: GlassShapeKind.RoundedRectangle }); // :82 .glassEffect(.regular, in: shape)
        var effect = SwiftGlass.GetEffect(glass)!;
        Assert.Equal(0, effect.Shape.CornerRadius);
        Assert.Equal(default, glass.CornerRadius);

        var stroke = Assert.IsType<Border>(view.Children[2]); // :88-90 strokeBorder(..., lineWidth: 1), the front layer
        Assert.Equal(new Thickness(1), stroke.BorderThickness);
        Assert.False(stroke.IsHitTestVisible);
    });

    private static Button ButtonFor(TourView view) =>
        view.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<Border>().Any(f => f.Width == 220 && f.Height == 42));

    private static string? TextOf(Button button) => button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;

    private static string? SymbolOf(Button button) => button.GetVisualDescendants().OfType<SwiftImage>().FirstOrDefault()?.SymbolName;

    private static void RaiseClick(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-099")] // App/Views/Tour/TourView.swift:24,234-246 the pages array is a value: a page appended to the caller's list later is not the view's
    public void Pages_appended_to_the_callers_list_after_construction_do_not_change_the_view() => headless.OnUiThread(() =>
    {
        var pages = new System.Collections.Generic.List<TourPage> { new("a", "One", "First") };
        var finished = 0;
        var view = new TourView(pages, onFinish: () => finished++);
        pages.Add(new("b", "Two", "Second"));
        Assert.True(view.IsLastPage);
        view.Advance();
        Assert.Equal(1, finished);
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-103")] // App/Views/Tour/TourView.swift:64-72,225-245 advancing an empty tour only moves the index: the body still shows the empty state
    public void Advancing_an_empty_tour_neither_throws_nor_calls_back() => headless.OnUiThread(() =>
    {
        var called = 0;
        var view = new TourView([], onFinish: () => called++, onClose: () => called++);
        view.Advance();
        Assert.Equal(0, called);
        Assert.Equal(1, view.CurrentIndex);
    });
}

using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using TourPages = global::Marrow.App.Views.Tour.Tour;

namespace Marrow.App.Tests.Views.Tour;

[Collection("Headless")]
public sealed class TourPagesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-UPDATE-088")] // App/Views/Tour/TourPages.swift:4-13
    [Trait("Req", "UI-UPDATE-046")] // App/Views/Tour/TourPages.swift:5
    [Trait("Req", "UI-UPDATE-047")] // App/Views/Tour/TourPages.swift:6
    [Trait("Req", "UI-UPDATE-048")] // App/Views/Tour/TourPages.swift:7
    [Trait("Req", "UI-UPDATE-049")] // App/Views/Tour/TourPages.swift:8
    [Trait("Req", "UI-UPDATE-050")] // App/Views/Tour/TourPages.swift:9
    [Trait("Req", "UI-UPDATE-051")] // App/Views/Tour/TourPages.swift:10
    [Trait("Req", "UI-UPDATE-052")] // App/Views/Tour/TourPages.swift:11
    [Trait("Req", "UI-UPDATE-053")] // App/Views/Tour/TourPages.swift:12
    public void Pages_have_the_required_ordered_titles_descriptions_and_artwork()
    {
        var expected = new[]
        {
            ("tour-welcome", "Welcome to nth", "One native Windows window for every coding agent you use. Claude, Codex, Gemini, Copilot and more, each in its own thread, on the same glass."),
            ("tour-hydra", "Meet Hydra", "One chat, many heads. The lead writes the briefs, sends helper agents out in parallel, each in a copy of your project, and their work lands back in your checkout."),
            ("tour-pairs", "Pair a strong lead with quick heads", "Put a Claude lead over Gemini or OpenCode heads. Pick the pair from the model picker, cap how many heads run at once, and give each side its own effort."),
            ("tour-slider", "Effort, on a slider", "Drag from low to max on any model. In a pair the track blends the lead's colour into the heads', fast mode streaks toward the knob, and the title opens the model picker."),
            ("tour-threads", "Threads, when you want them", "Hide the sidebar and the chat has the whole window. The Threads button brings the list back as a floating panel: the same search, the same rows, and it goes away as soon as you pick one."),
            ("tour-recipes", "Recipes for a team", "Hydra's cookbook holds ready-made pairs: a lead, its heads and the effort each side gets, from the providers you have signed in. Pick one in Settings and every new chat starts on it."),
            ("tour-panels", "Panels that float", "Changes, the terminal and the team's panel dock to any corner and can pop out on their own. The conversation makes room, and everything stays in one window."),
            ("tour-themes", "Make it yours", "Twenty-six themes, from Catppuccin to Kanagawa, in real glass. Built in C#, so it scrolls smoothly and starts in a blink."),
        };

        Assert.Equal(8, TourPages.Pages.Count);
        Assert.Equal(expected, TourPages.Pages.Select(page => (page.ImageName, page.Title, page.Description)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-088")] // App/Views/Tour/TourPages.swift:4-13
    public void Advancing_past_the_last_page_does_not_wrap_to_the_first()
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        using var clock = SwiftClock.Provider.Override(time);
        using var frameSource = FrameClock.Source.Override(frames);

        headless.OnUiThread(() =>
        {
            var view = new TourView(TourPages.Pages);
            for (var index = 0; index < 7; index++)
            {
                view.Advance();
                time.Advance(TimeSpan.FromSeconds(0.25));
                frames.Advance(TimeSpan.FromSeconds(0.25));
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(view.IsLastPage);
            Assert.Equal(7, view.CurrentIndex);

            view.Advance();

            Assert.True(view.IsLastPage);
            Assert.Equal(7, view.CurrentIndex);
            Assert.Equal(7, TourView.Clamped(8, 8));
            Assert.Equal(7, TourView.Clamped(7, 8));
            Assert.Equal(0, TourView.Clamped(-1, 8));
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-089")] // App/Views/Tour/TourPages.swift:15-24
    public void Themes_page_derives_its_count_word_from_the_theme_catalogue()
    {
        var count = SwiftEnum<AppTheme>.AllCases.Count;
        Assert.Equal(26, count);

        var spelled = SwiftFormatters.SpellOut(count, "en_US")
            ?? count.ToString(CultureInfo.InvariantCulture);
        var countWord = SwiftString.Uppercased(spelled.Substring(0, 1)) + spelled.Substring(1);

        Assert.Equal("Twenty-six", countWord);
        Assert.Equal(
            countWord + " themes, from Catppuccin to Kanagawa, in real glass. Built in C#, so it scrolls smoothly and starts in a blink.",
            TourPages.Pages[7].Description);
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-053")] // App/Views/Tour/TourPages.swift:12
    public void Themes_page_renders_the_computed_count_and_port_description()
    {
        Assert.Equal(
            "Twenty-six themes, from Catppuccin to Kanagawa, in real glass. Built in C#, so it scrolls smoothly and starts in a blink.",
            TourPages.Pages[7].Description);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-093")] // App/Views/Tour/TourPages.swift:26-28
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-002: the tour's close control writes hasSeenTour
    [Trait("Req", "UI-LAUNCH-002")] // App/Views/Tour/TourPages.swift:26-28
    public void Clicking_the_checkmark_marks_the_tour_seen_and_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var model = h.MakeModel();

            h.Main.OnMain(() =>
            {
                TourPages.Present(model);
                var window = TourWindowController.Shared.Present(TourPages.Pages);
                var view = Assert.IsType<TourView>(window.Content);
                var checkmark = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.GetVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "checkmark"));
                checkmark.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.IsVisible);
            });

            Assert.True(h.Main.OnMain(() => model.Settings.HasSeenTour));
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-093")] // App/Views/Tour/TourPages.swift:26-28; App/Views/Tour/TourWindowController.swift:103-106,139-148
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-002: a native window close only clears the controller; OD-0039 follows the Swift over the record's "closed without finishing writes it"
    [Trait("Req", "UI-LAUNCH-002")] // App/Views/Tour/TourPages.swift:26-28; App/Views/Tour/TourWindowController.swift:103-107; owner_decisions.md OD-0039
    public void Closing_the_window_without_the_checkmark_does_not_mark_the_tour_seen()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                TourPages.Present(model);
                TourWindowController.Shared.Close();
                Assert.False(model.Settings.HasSeenTour);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-093")] // App/Views/Tour/TourPages.swift:26-28
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-002: Done on the last page writes hasSeenTour
    [Trait("Req", "UI-LAUNCH-002")] // App/Views/Tour/TourPages.swift:26-28
    public void Finishing_on_the_last_page_marks_the_tour_seen_and_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var frames = new ManualFrameSource();
            using var frameSource = FrameClock.Source.Override(frames);
            var model = h.MakeModel();

            h.Main.OnMain(() => TourPages.Present(model));

            var window = h.Main.OnMain(() => TourWindowController.Shared.Present(TourPages.Pages));
            var view = Assert.IsType<TourView>(window.Content);

            for (var index = 0; index < 7; index++)
            {
                h.Main.OnMain(view.Advance);
                h.Main.Advance(TimeSpan.FromSeconds(0.25));
                frames.Advance(TimeSpan.FromSeconds(0.25));
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(h.Main.OnMain(() => view.IsLastPage));
            h.Main.OnMain(view.Advance);
            Assert.True(h.Main.OnMain(() => model.Settings.HasSeenTour));

            Assert.False(window.IsVisible);
        });
    }
}

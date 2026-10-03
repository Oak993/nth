using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// The chat title's text change (UI-CHAT-076, PR-CHAT-176), round 4. SOURCE FACT: ChatView.swift:1147-1151,1203-1205 the title Text takes its new
// words and colour with no animation of its own (the row's body re-evaluates; the column's `.transition(.identity)` is what the record's
// "never crossfades on a thread switch" speaks of). OWNER-RULED DEVIATION (OD-0076, "content that swaps crossfades ... wherever the source
// snaps"; the lead's decision for PR-CHAT-176 / UI-CHAT-076): when the title's words change on a drawn title, the old words fade out in place
// while the new ones fade in, on one ease-out 0.2 s; Reduce Motion cuts. The title's width still jumps (ED-NEW-parity-chatview (13)).
[Collection("Headless")]
public sealed class ChatViewParityRound4TitleTests(HeadlessSession headless)
{
    private static readonly Animation Swap = Animation.EaseOut(duration: 0.2);

    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private static void Step(AppModelHarness h, ManualFrameSource frames, AvaloniaWindow window, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.Zero); Drain(h, window);
    }

    private void WithChat(Action<AppModelHarness, AppModel, ChatView, ThreadRuntime, AvaloniaWindow, ManualFrameSource> check, bool reduceMotion = false, string title = "") => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var h = new AppModelHarness();
        h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var clock = FrameClock.Source.Override(frames);
        h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        AvaloniaWindow window = null!;
        ChatView view = null!;
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
            if (title.Length != 0) model.UpdateThread(runtime.ThreadID, thread => thread with { Title = title });
            view = new ChatView(runtime);
            window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
            AppEnvironment.SetAppModel(window, model);
            AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
            ChatHeadlessFixtures.Track(window);
            window.Show();
        });
        try
        {
            Drain(h, window);
            check(h, model, view, runtime, window, frames);
        }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    private static ChatChromeTitle TitleOf(ChatView view) => Assert.Single(LogicalQuery.SelfAndDescendants<ChatChromeTitle>(view));

    private static TextBlock[] Words(ChatView view) => LogicalQuery.SelfAndDescendants<TextBlock>(TitleOf(view)).ToArray();

    private static TextBlock Words(ChatView view, string text) => Assert.Single(Words(view), words => words.Text == text);

    private static void Rename(AppModelHarness h, AppModel model, ThreadRuntime runtime, AvaloniaWindow window, string title)
    {
        h.Main.OnMain(() => model.UpdateThread(runtime.ThreadID, thread => thread with { Title = title }));
        Drain(h, window);
    }

    private static Avalonia.Media.Color Colour(TextBlock words) => Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(words.Foreground).Color;

    private static Avalonia.Media.Color Expected(TextBlock words, bool empty) =>
        (empty ? Chrome.SecondaryText : Chrome.PrimaryText).Resolve(words.ActualThemeVariant);

    // UI-CHAT-076 / PR-CHAT-176 under OD-0076: a rename (the same update path a thread switch and an auto-title take) crossfades the words. Frame by
    // frame on the 0.2 s ease-out: the new words weigh the curve's progress and the old ones what is left of it (the pair always adds to
    // one, so no frame shows the title thinner than a plain Text), the old words keep their place and width and take no room, and the
    // new ones are alone, opaque, when it ends. The first frame is not a cut: the old words are still the opaque ones.
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_rename_crossfades_the_old_words_into_the_new_ones_in_place_on_an_ease_out_of_0_2_seconds()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            Rename(h, model, runtime, window, "Before the change");
            Step(h, frames, window, 1);
            var title = TitleOf(view);
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(title));
            var before = Words(view, "Before the change");
            Assert.Equal(1, before.Opacity);
            var left = before.TranslatePoint(default, view)!.Value;
            var width = before.Bounds.Width;
            Rename(h, model, runtime, window, "After");
            Assert.Same(button, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(title)));
            Assert.Equal(2, Words(view).Length); // both on screen: not cut
            Assert.Same(before, Words(view, "Before the change"));
            Assert.Equal(1, before.Opacity, 0.001);
            Assert.Equal(0, Words(view, "After").Opacity, 0.001);
            var elapsed = 0.0;
            foreach (var at in new[] { 0.03, 0.06, 0.1, 0.15 })
            {
                Step(h, frames, window, at - elapsed); elapsed = at;
                var arriving = Words(view, "After");
                var leaving = Words(view, "Before the change");
                var weight = Swap.Progress(at);
                Assert.Equal(weight, arriving.Opacity, 0.03);
                Assert.Equal(1 - weight, leaving.Opacity, 0.03);
                Assert.Equal(left, leaving.TranslatePoint(default, view)!.Value); // the old words keep their place...
                Assert.Equal(width, leaving.Bounds.Width, 0.5); // ...and their width,
                Assert.Equal(arriving.DesiredSize.Width, button.Bounds.Width, 1); // and the title's button is the new words' width alone.
            }
            Step(h, frames, window, 0.3);
            var settled = Assert.Single(Words(view));
            Assert.Equal("After", settled.Text);
            Assert.Equal(1, settled.Opacity);
            Assert.Equal(left, settled.TranslatePoint(default, view)!.Value);
        });
    }

    // OD-0076: "the system's reduce-motion setting stills them". With Reduce Motion the words are replaced at once, in place, one Text.
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void With_Reduce_Motion_a_rename_cuts_to_the_new_words()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            Rename(h, model, runtime, window, "Before the change");
            Rename(h, model, runtime, window, "After");
            var only = Assert.Single(Words(view));
            Assert.Equal("After", only.Text);
            Assert.Equal(1, only.Opacity);
            Step(h, frames, window, 0.05);
            Assert.Equal("After", Assert.Single(Words(view)).Text);
            // The cut takes the colour of the new title too: secondary for the empty one, primary again for a titled one.
            Rename(h, model, runtime, window, "");
            var empty = Assert.Single(Words(view));
            Assert.Equal("New thread", empty.Text);
            Assert.Equal(Expected(empty, empty: true), Colour(empty));
            Rename(h, model, runtime, window, "Back");
            var titled = Assert.Single(Words(view));
            Assert.Equal("Back", titled.Text);
            Assert.Equal(Expected(titled, empty: false), Colour(titled));
        }, reduceMotion: true);
    }

    // The update path that sets the colour in practice (the column stays mounted: a thread switch, a rename and an auto-title all arrive as
    // UpdateTitle, never as a new view): titled, then empty ("New thread", secondary), then titled again. After each change settles, the words and
    // the colour are the ones of the new title; while it runs the arriving words wear the NEW title's colour and the leaving ones keep the OLD
    // one (the Mac's Text has one colour per body; the crossfade shows the two bodies).
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_mounted_title_driven_from_titled_to_empty_and_back_shows_the_words_and_the_colour_of_each_title_on_the_update_path()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            var primary = Expected(Words(view).First(), empty: false);
            var secondary = Expected(Words(view).First(), empty: true);
            Assert.NotEqual(primary, secondary);
            Rename(h, model, runtime, window, "Fix the build");
            Step(h, frames, window, 1);
            var settled = Assert.Single(Words(view));
            Assert.Equal("Fix the build", settled.Text);
            Assert.Equal(primary, Colour(settled));
            foreach (var (title, shown, empty) in new[] { ("", "New thread", true), ("Fix the build", "Fix the build", false), ("", "New thread", true) })
            {
                var leaving = Assert.Single(Words(view));
                var leavingColour = Colour(leaving);
                Rename(h, model, runtime, window, title);
                Step(h, frames, window, 0.05);
                var arriving = Words(view, shown);
                Assert.Equal(Expected(arriving, empty), Colour(arriving)); // the arriving words wear the new title's colour
                Assert.Equal(leavingColour, Colour(Assert.Single(Words(view), words => !ReferenceEquals(words, arriving)))); // the leaving ones keep theirs
                Step(h, frames, window, 1);
                var only = Assert.Single(Words(view));
                Assert.Equal(shown, only.Text);
                Assert.Equal(Expected(only, empty), Colour(only));
                Assert.Equal(1, only.Opacity);
            }
        }, title: "Fix the build");
    }

    // OD-0076: no change by a cut, also when the change comes while the last one is still running: the words on their way in leave from the
    // opacity they have reached (never from full), the new ones arrive from nothing, and the words that had already left are not brought back.
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_rename_while_a_crossfade_runs_takes_the_arriving_words_out_from_where_they_have_got_to()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            Rename(h, model, runtime, window, "First title");
            Step(h, frames, window, 1);
            Rename(h, model, runtime, window, "Second title");
            Step(h, frames, window, 0.05);
            var reached = Words(view, "Second title").Opacity;
            Assert.InRange(reached, 0.05, 0.95);
            Rename(h, model, runtime, window, "Third title");
            Assert.Equal(reached, Words(view, "Second title").Opacity, 0.02); // no jump to full
            Assert.Equal(0, Words(view, "Third title").Opacity, 0.02);
            Step(h, frames, window, 0.05);
            Assert.True(Words(view, "Second title").Opacity < reached, "the leaving words go on fading");
            Assert.True(Words(view, "Third title").Opacity > 0.05);
            Step(h, frames, window, 1);
            var only = Assert.Single(Words(view));
            Assert.Equal("Third title", only.Text);
            Assert.Equal(1, only.Opacity);
        });
    }

    // A refresh that leaves the title as it is (the row's inputs are re-evaluated by every model change) swaps nothing.
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_refresh_that_leaves_the_title_unchanged_swaps_nothing()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            Rename(h, model, runtime, window, "Same title");
            Step(h, frames, window, 1);
            var words = Assert.Single(Words(view));
            Rename(h, model, runtime, window, "Same title");
            h.Main.OnMain(() => model.Settings.ShowsUsagePanel = !model.Settings.ShowsUsagePanel); // another model change: the inputs are re-read
            Drain(h, window);
            Assert.Same(words, Assert.Single(Words(view)));
            Assert.Equal(1, words.Opacity);
        });
    }

    // Nothing drawn yet, nothing to fade from: a title that has no laid-out words takes its new ones in place (the update of a row not yet shown).
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211, 1147-1151; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_title_that_has_not_been_drawn_takes_its_new_words_in_place()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("project")).Url))!.Id);
                var title = new ChatChromeTitle(model, runtime, "Before");
                var update = typeof(ChatChromeTitle).GetMethod("UpdateTitle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                update.Invoke(title, ["After"]);
                var only = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(title));
                Assert.Equal("After", only.Text);
                Assert.Equal(1, only.Opacity);
            });
        });
    }

    // The leaving words keep the width they were laid out at, so a title that was trimmed with an ellipsis fades out trimmed, not
    // regrown to its full length as it leaves (a long title in a row too narrow for it).
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1207; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1207; OD-0076
    public void The_leaving_words_of_a_trimmed_title_fade_out_at_the_width_they_were_trimmed_to()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            var long_ = string.Concat(Enumerable.Repeat("a long title that cannot fit the row ", 20));
            Rename(h, model, runtime, window, long_);
            Step(h, frames, window, 1);
            var before = Assert.Single(Words(view));
            var trimmed = before.Bounds.Width;
            Assert.InRange(trimmed, 300, 1400); // 740 characters at this size are far wider than the window: it is trimmed to the row
            Rename(h, model, runtime, window, "Short");
            Step(h, frames, window, 0.05);
            var leaving = Assert.Single(Words(view), words => words.Text == long_);
            Assert.Equal(trimmed, leaving.Bounds.Width, 0.5);
        });
    }

    // A title that is literally "New thread" and an empty one read the same words in different colours: the change between them is a
    // change of the words on screen (primary to secondary) and crossfades like any other.
    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205; OD-0076
    public void A_title_that_is_literally_New_thread_crossfades_to_the_empty_title_in_the_other_colour()
    {
        WithChat((h, model, view, runtime, window, frames) =>
        {
            Rename(h, model, runtime, window, "New thread");
            Step(h, frames, window, 1);
            var literal = Assert.Single(Words(view));
            Assert.Equal(Expected(literal, empty: false), Colour(literal));
            Rename(h, model, runtime, window, "");
            Step(h, frames, window, 0.05);
            Assert.Equal(2, Words(view).Length);
            var arriving = Assert.Single(Words(view), words => !ReferenceEquals(words, literal));
            Assert.Equal(Expected(arriving, empty: true), Colour(arriving));
            Assert.Equal(Expected(literal, empty: false), Colour(literal));
            Step(h, frames, window, 1);
            Assert.Same(arriving, Assert.Single(Words(view)));
        });
    }
}

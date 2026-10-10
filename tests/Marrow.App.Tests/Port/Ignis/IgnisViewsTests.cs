using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Views.Composer;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis U4 (OD-0134, ED-NEW-ignis-views; work/ignis/PLAN.md unit U4): the Kindle capsule, its popover, the timeline's ping filter,
// the event lines and the first-time notice, against a fake controller (the engine, providers and runtime are other units').
// A view's observation reactions land on the next main turn, so each step is its own runtime.OnMain.
[Collection("Headless")]
public sealed class IgnisViewsTests(HeadlessSession headless)
{
    private static readonly Guid Chat = KindleFakeController.Chat;

    private static KindleFakeController Controller(TestRuntime runtime) => new(new IgnisSettings(runtime.Defaults));

    private static IgnisChatStatus Warm(TestRuntime runtime, TimeSpan left, IgnisMode mode = IgnisMode.WarmAndTidy) =>
        IgnisChatStatus.Unknown(Chat, mode, true) with
        {
            Provider = ProviderKind.Claude, Cache = IgnisCacheState.Warm, ExpiresAt = runtime.Time.GetUtcNow() + left, Ttl = TimeSpan.FromHours(1),
            ContextUsed = 244_000, ContextWindow = 1_000_000, TidyAt = 400_000, SavedToday = 3.18m, CanKeepWarm = true, CanTidy = true,
        };

    private static AvaloniaWindow Show(Control content)
    {
        var window = new AvaloniaWindow { Content = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom }, Width = 600, Height = 900 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static (KindleCapsule Capsule, KindlePopover Popover, AvaloniaWindow Window) Open(TestRuntime runtime, KindleFakeController controller) => runtime.OnMain(() =>
    {
        var capsule = new KindleCapsule(controller, Chat);
        var popover = new KindlePopover(capsule);
        var window = Show(new StackPanel { Children = { capsule, popover } });
        return (capsule, popover, window);
    });

    // ---- capsule or ring

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void The_composer_hosts_the_Kindle_for_a_supported_provider_and_keeps_the_ContextMeter_otherwise_or_unbound() => headless.OnUiThread(() =>
    {
        using var h = ComposerViewTests.RuntimeHarness();
        // unbound: the ContextMeter, as before
        var plain = h.Main.OnMain(() => new ComposerControls(h.Runtime, h.Thread, false, false, false, null, () => { }));
        Assert.IsType<ContextMeter>(((StackPanel)plain.Child!).Children[^2]);
        var controller = h.Main.OnMain(() => new KindleFakeController(new IgnisSettings(new InMemorySettingsStore())));
        using (IgnisViews.Override(controller))
        {
            Assert.True(IgnisViews.Supports(h.Thread.Provider), "the harness chat's provider is one Ignis supports");
            var bound = h.Main.OnMain(() => new ComposerControls(h.Runtime, h.Thread, false, false, false, null, () => { }));
            var capsule = Assert.IsType<KindleCapsule>(((StackPanel)bound.Child!).Children[^2]);
            Assert.Equal(h.Runtime.ThreadID, capsule.ThreadID);
            // a provider Ignis has no route for keeps the ring
            Assert.Null(h.Main.OnMain(() => IgnisViews.ComposerOffer(Chat, ProviderKind.Cursor)));
            Assert.IsType<KindleCapsule>(h.Main.OnMain(() => IgnisViews.ComposerOffer(Chat, ProviderKind.Claude)));
            // compact composers carry neither
            var compact = h.Main.OnMain(() => new ComposerControls(h.Runtime, h.Thread, true, false, false, null, () => { }));
            Assert.DoesNotContain(((StackPanel)compact.Child!).Children, c => c is KindleCapsule or ContextMeter);
        }
        Assert.Null(h.Main.OnMain(() => IgnisViews.ComposerOffer(Chat, ProviderKind.Claude)));
    });

    // ---- countdown, Cold, Off

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void The_capsule_counts_down_in_whole_minutes_then_reads_Cold_and_Off() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var controller = Controller(runtime);
        controller.Current = Warm(runtime, TimeSpan.FromMinutes(41.5));
        var capsule = runtime.OnMain(() => new KindleCapsule(controller, Chat));
        var window = runtime.OnMain(() => Show(capsule));
        try
        {
            runtime.OnMain(() =>
            {
                Assert.Equal("42m", capsule.Text);
                Assert.Equal(KindleCapsule.CapsuleHeight, capsule.Button.Bounds.Height, 1);
                Assert.True(capsule.Bounds.Width >= KindleCapsule.MinCapsuleWidth - .5);
                Assert.True(capsule.Glyph.Breathing, "a warm core breathes");
                Assert.InRange(capsule.Glyph.Heat, .3, 1);
                Assert.Equal(IgnisWords.CapsuleAccessibleName, AutomationProperties.GetName(capsule.Button));
            });
            void At(TimeSpan advance, string expected)
            {
                runtime.Time.Advance(advance);
                runtime.OnMain(capsule.Refresh);
                runtime.OnMain(() => Assert.Equal(expected, capsule.Text));
            }
            At(TimeSpan.FromMinutes(1), "41m");
            At(TimeSpan.FromMinutes(39.6), "1m");
            // the time ran out before the controller said so: Cold, the ember out
            At(TimeSpan.FromMinutes(1), "Cold");
            runtime.OnMain(() => Assert.False(capsule.Glyph.Breathing));
            runtime.OnMain(() => controller.Set(controller.Current with { Cache = IgnisCacheState.Cold, ExpiresAt = null }));
            runtime.OnMain(() => Assert.Equal("Cold", capsule.Text));
            runtime.OnMain(() => controller.Set(controller.Current with { Mode = IgnisMode.Off, Cache = IgnisCacheState.Off }));
            runtime.OnMain(() => Assert.Equal("Off", capsule.Text));
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void Only_changed_digits_roll_and_Reduce_Motion_swaps_at_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var controller = Controller(runtime);
        controller.Current = Warm(runtime, TimeSpan.FromMinutes(42));
        var capsule = runtime.OnMain(() => new KindleCapsule(controller, Chat));
        var window = runtime.OnMain(() => Show(capsule));
        try
        {
            runtime.Time.Advance(TimeSpan.FromMinutes(1));
            runtime.OnMain(capsule.Refresh);
            runtime.OnMain(() =>
            {
                Assert.Equal("41m", capsule.Text);
                Assert.True(capsule.Countdown.IsRolling || KindleMotion.Reduce(capsule), "the changed digit rolls");
                EnvironmentValues.SetAccessibilityReduceMotion(capsule, true);
            });
            runtime.Time.Advance(TimeSpan.FromMinutes(1));
            runtime.OnMain(capsule.Refresh);
            runtime.OnMain(() =>
            {
                Assert.Equal("40m", capsule.Text);
                Assert.False(capsule.Countdown.IsRolling);
                Assert.False(capsule.Glyph.IsAmbientRunning, "nothing loops under Reduce Motion");
            });
        }
        finally { runtime.OnMain(window.Close); }
    });

    // ---- the popover: upward, Escape

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void A_press_opens_the_popover_upward_and_Escape_closes_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        var controller = Controller(runtime);
        controller.Current = Warm(runtime, TimeSpan.FromMinutes(42));
        var capsule = runtime.OnMain(() => new KindleCapsule(controller, Chat));
        var window = runtime.OnMain(() => Show(capsule));
        try
        {
            runtime.OnMain(() =>
            {
                Assert.Equal(PlacementMode.TopEdgeAlignedRight, capsule.Popover.Placement);
                Click(capsule.Button);
            });
            runtime.OnMain(() =>
            {
                Assert.True(capsule.IsPresented);
                Assert.Equal((capsule.Popover, true), recorder.Calls[^1]);
                var content = LogicalQuery.SelfAndDescendants<KindlePopover>(capsule.Popover.Child!).Single();
                Assert.False(content.DetailsOpen, "Details starts collapsed");
                Assert.Equal(0, content.DetailsReveal);
                Assert.True(PopoverHost.ForwardKey(capsule.Popover, window, Key.Escape, KeyModifiers.None));
            });
            runtime.OnMain(() =>
            {
                Assert.False(capsule.IsPresented);
                Assert.Equal((capsule.Popover, false), recorder.Calls[^1]);
            });
        }
        finally { runtime.OnMain(() => { window.Close(); WindowTransients.Hide(capsule.Popover); }); }
    });

    // ---- buttons by mode

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void Keep_warm_Tidy_up_and_Auto_are_enabled_by_the_mode_and_the_controllers_word() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        var controller = Controller(runtime);
        controller.Current = Warm(runtime, TimeSpan.FromMinutes(42));
        var (capsule, popover, window) = Open(runtime, controller);
        try
        {
            runtime.OnMain(() =>
            {
                Assert.True(popover.KeepWarmButton.IsEnabled);
                Assert.True(popover.TidyButton.IsEnabled);
                Assert.True(popover.AutoControl.IsEnabled);
                Click(popover.KeepWarmButton);
                Click(popover.TidyButton);
                Assert.Equal(["warm", "tidy"], controller.Calls);
                Assert.Equal("Today Ignis saved $3.18. It keeps the quick memory warm while you work and tidies up the chat before it gets costly, always between tasks.", popover.SummaryText);
            });
            void Then(Func<IgnisChatStatus, IgnisChatStatus> change, Action check)
            {
                runtime.OnMain(() => controller.Set(change(controller.Current)));
                runtime.OnMain(check);
            }
            Then(s => s with { Mode = IgnisMode.KeepWarm, Next = IgnisNext.KeepWarm }, () =>
            {
                Assert.True(popover.KeepWarmButton.IsEnabled);
                Assert.False(popover.TidyButton.IsEnabled, "Keep warm never tidies up");
            });
            Then(s => s with { Mode = IgnisMode.Off, Cache = IgnisCacheState.Off, Next = IgnisNext.Off }, () =>
            {
                Assert.False(popover.KeepWarmButton.IsEnabled);
                Assert.False(popover.TidyButton.IsEnabled);
                Assert.False(popover.AutoControl.IsEnabled, "Auto means nothing while Ignis is off");
                Assert.Equal("Today Ignis saved $3.18. Ignis is off in this chat, so it isn’t saving anything right now.", popover.SummaryText);
            });
            // the controller's own word: not between tasks, a tidy-up running
            Then(s => s with { Mode = IgnisMode.WarmAndTidy, Cache = IgnisCacheState.Warm, CanKeepWarm = false, CanTidy = true, Tidying = true, Next = IgnisNext.WarmAndTidy }, () =>
            {
                Assert.False(popover.KeepWarmButton.IsEnabled);
                Assert.False(popover.TidyButton.IsEnabled);
                Assert.Equal(IgnisWords.TidyingUp, AutomationProperties.GetName(popover.TidyButton));
                Assert.Equal("Today Ignis saved $3.18. It’s tidying up the chat now. Your recent messages stay word for word.", popover.SummaryText);
                Assert.True(capsule.Glyph.Spinning, "the ring spins while it tidies");
            });
            Then(s => s with { Tidying = false, JustTidied = true, CanKeepWarm = true }, () =>
            {
                Assert.Equal(IgnisWords.TidiedUp, AutomationProperties.GetName(popover.TidyButton));
                Assert.False(capsule.Glyph.Spinning);
                // the mode control writes through the controller
                popover.ModeControl.Select(IgnisSettings.RawValue(IgnisMode.KeepWarm));
                Assert.Contains("mode KeepWarm", controller.Calls);
            });
        }
        finally { runtime.OnMain(window.Close); }
    });

    // ---- the usage line

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void The_quiet_line_is_the_plan_share_or_the_API_key_sentence_and_Open_usage_closes_the_popover() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        var opened = 0;
        var before = KindlePopover.OpenUsage;
        KindlePopover.OpenUsage = _ => opened++;
        try
        {
            var controller = Controller(runtime);
            var thursday = new DateTimeOffset(2026, 9, 24, 18, 30, 0, TimeSpan.Zero);
            controller.Current = Warm(runtime, TimeSpan.FromMinutes(42)) with { WeekShare = .09, WeeklyLimitAt = thursday };
            var (capsule, popover, window) = Open(runtime, controller);
            try
            {
                runtime.OnMain(() =>
                {
                    var when = IgnisWords.DayPart(thursday.ToLocalTime());
                    Assert.Equal("This chat used 9% of your week. At this pace you’ll reach the weekly limit " + when + ".", popover.AccountText);
                    controller.Set(controller.Current with { Account = IgnisAccountKind.ApiKey, ChatCost = 4.12m, ChatCostWithoutIgnis = 7.30m });
                });
                runtime.OnMain(() =>
                {
                    Assert.Equal("API key: you pay per token. This chat: $4.12.", popover.AccountText);
                    capsule.IsPresented = true;
                    Click(popover.OpenUsageButton);
                    Assert.Equal(1, opened);
                    Assert.False(capsule.IsPresented, "Open usage closes the popover");
                });
            }
            finally { runtime.OnMain(window.Close); }
        }
        finally { KindlePopover.OpenUsage = before; }
    });

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void Open_usage_shows_the_usage_panel_setting() => headless.OnUiThread(() =>
    {
        using var h = ComposerViewTests.RuntimeHarness();
        h.Main.OnMain(() =>
        {
            var host = new Border();
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            var anchor = new Border();
            host.Child = anchor;
            h.App.Settings.ShowsUsagePanel = false;
            KindlePopover.OpenUsage(anchor);
            Assert.True(h.App.Settings.ShowsUsagePanel);
        });
    });

    // ---- (i) explanations follow the setting; Details

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void The_explanations_follow_Show_i_explanations_and_Details_opens_on_Savings() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        var controller = Controller(runtime);
        controller.Current = Warm(runtime, TimeSpan.FromMinutes(42));
        var (_, popover, window) = Open(runtime, controller);
        try
        {
            runtime.OnMain(() =>
            {
                Assert.NotEmpty(popover.Infos);
                Assert.All(popover.Infos, info => Assert.True(info.IsVisible));
                Assert.Contains(popover.Infos, info => AutomationProperties.GetName(info) == "What is quick memory?");
                controller.Settings.Update(p => p with { ShowExplanations = false }); // the setting alone hides them
            });
            runtime.OnMain(() =>
            {
                Assert.All(popover.Infos, info => Assert.False(info.IsVisible));
                Click(popover.DetailsButton);
            });
            runtime.OnMain(() =>
            {
                Assert.True(popover.DetailsOpen);
                Assert.Equal(KindlePopover.Section.Savings, popover.DetailsSection);
                popover.DetailsSection = KindlePopover.Section.History;
            });
            runtime.OnMain(() => Assert.Equal(KindlePopover.Section.History, popover.DetailsSection));
        }
        finally { runtime.OnMain(window.Close); }
    });

    // ---- ping turns hidden, Show pings reveals them

    private static TimelineEntry Entry(string id, string message) =>
        new(new TimelineItem(null, new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = message }), id));

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void Ping_turns_are_hidden_unless_pings_are_shown_and_nothing_is_hidden_unbound() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var controller = Controller(runtime);
        TimelineEntry user = null!, ping = null!, line = null!;
        IReadOnlyList<TimelineEntry> entries = null!;
        runtime.OnMain(() =>
        {
            user = Entry("a", "Finish the export flow");
            ping = Entry(IgnisEvent.NewPingItemID(), IgnisWords.PingText);
            line = Entry(IgnisEvent.TimelinePrefix + "e1", "Ignis kept the quick memory warm");
            entries = [user, ping, line];
            Assert.Same(entries, IgnisViews.Visible(entries)); // unbound: everything, the same list
        });
        using (IgnisViews.Override(controller))
        {
            var revision = 0;
            runtime.OnMain(() =>
            {
                Assert.Equal([user, line], IgnisViews.Visible(entries));
                revision = IgnisViewState.Shared.PingsRevision;
                controller.Settings.Update(p => p with { ShowPings = true });
            });
            runtime.OnMain(() =>
            {
                Assert.True(IgnisViewState.Shared.PingsRevision > revision, "the timeline's pass runs again");
                Assert.Same(entries, IgnisViews.Visible(entries));
                IReadOnlyList<TimelineEntry> noPings = [user, line];
                controller.Settings.Update(p => p with { ShowPings = false });
                Assert.Same(noPings, IgnisViews.Visible(noPings)); // nothing left out: no copy for the block cache
                Assert.Equal([user, line], IgnisViews.Visible(entries));
            });
        }
    });

    // ---- event lines and the first-time notice

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "OD-0134")]
    public void An_event_notice_draws_as_a_quiet_line_and_the_first_time_notice_shows_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var controller = Controller(runtime);
        var now = runtime.Time.GetUtcNow();
        controller.FakeLedger.Record(new IgnisEvent("e1", Chat, IgnisEventKind.KeptWarm, now.AddMinutes(-3), 0.02m));
        controller.FakeLedger.Record(new IgnisEvent("e2", Chat, IgnisEventKind.TidiedUp, now.AddMinutes(-2), 0.42m, IgnisAmountKind.Saved));
        controller.FakeLedger.Record(new IgnisEvent("e3", Chat, IgnisEventKind.KeptWarm, now.AddMinutes(-1), 0.02m));
        using (IgnisViews.Override(controller))
        {
            IgnisEventLine first = null!;
            AvaloniaWindow window = null!;
            runtime.OnMain(() =>
            {
                Assert.Null(IgnisViews.EventLineFor(Entry("n", "Context compacted.").Item)); // an ordinary notice stays a NoticeRow

                first = Assert.IsType<IgnisEventLine>(IgnisViews.EventLineFor(Entry(IgnisEvent.TimelinePrefix + "e1", "x").Item));
                Assert.Equal("Ignis kept the quick memory warm", first.Words);
                Assert.Equal("$0.02", first.Amount);
                Assert.NotNull(first.Notice);
                var tidy = Assert.IsType<IgnisEventLine>(IgnisViews.EventLineFor(Entry(IgnisEvent.TimelinePrefix + "e2", "x").Item));
                Assert.Equal("Ignis tidied up the chat", tidy.Words);
                Assert.Equal("saved $0.42", tidy.Amount);
                Assert.Null(tidy.Notice);
                var second = Assert.IsType<IgnisEventLine>(IgnisViews.EventLineFor(Entry(IgnisEvent.TimelinePrefix + "e3", "x").Item));
                Assert.Null(second.Notice); // once: one line holds it

                // the row itself: NoticeRow hands an event item to the line
                var row = new Marrow.App.Views.Chat.NoticeRow(Entry(IgnisEvent.TimelinePrefix + "e2", "x"));
                Assert.IsType<IgnisEventLine>(row.Child);
                window = Show(first);
            });
            try
            {
                runtime.OnMain(() =>
                {
                    var notice = first.Notice!;
                    Assert.Equal(IgnisWords.ShowPings, AutomationProperties.GetName(notice.ShowPingsButton));
                    Click(notice.ShowPingsButton);
                    Assert.True(controller.Settings.Value.ShowPings);
                    Assert.Equal(IgnisWords.HidePings, AutomationProperties.GetName(notice.ShowPingsButton));
                    Click(notice.GotItButton);
                    Assert.True(controller.Settings.Value.NoticeSeen);
                });
                runtime.OnMain(() => Assert.Null(Assert.IsType<IgnisEventLine>(IgnisViews.EventLineFor(Entry(IgnisEvent.TimelinePrefix + "e1", "x").Item)).Notice));
            }
            finally { runtime.OnMain(window.Close); }
        }
        runtime.OnMain(() =>
        {
            // a Codex chat names Codex; an API provider's chat has no hidden pings and no notice
            IgnisViewState.Shared.NoticeHolder = null;
            var codex = new KindleFakeController(new IgnisSettings(new InMemorySettingsStore())); // NoticeSeen is still false here
            codex.Current = codex.Current with { Provider = ProviderKind.Codex };
            codex.FakeLedger.Record(new IgnisEvent("c1", Chat, IgnisEventKind.KeptWarm, now));
            var codexLine = IgnisEventLine.For(codex, Entry(IgnisEvent.TimelinePrefix + "c1", "x").Item)!;
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(codexLine.Notice!), t => t.Text == IgnisWords.NoticeBody("Codex"));
            IgnisViewState.Shared.NoticeHolder = null;
            var api = new KindleFakeController(new IgnisSettings(new InMemorySettingsStore()));
            api.Current = api.Current with { Provider = ProviderKind.Deepseek };
            api.FakeLedger.Record(new IgnisEvent("d1", Chat, IgnisEventKind.KeptWarm, now));
            Assert.Null(IgnisEventLine.For(api, Entry(IgnisEvent.TimelinePrefix + "d1", "x").Item)!.Notice);
            IgnisViewState.Shared.NoticeHolder = null;
        });
    });
}

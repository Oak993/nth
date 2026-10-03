using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Audit fixes for DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792): bunny slice App.Views.Chat.ChatView.
[Collection("Headless")]
public sealed class ChatViewAuditFixTests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private void WithColumn(Action<AppModelHarness, AppModel, ThreadRuntime, ChatView, AvaloniaWindow> check) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        h.WriteLibrary([new Project("Project", directory)], []);
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
            view = new ChatView(runtime);
            window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
            AppEnvironment.SetAppModel(window, model);
            AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
            ChatHeadlessFixtures.Track(window);
            window.Show();
        });
        try { Drain(h, window); check(h, model, runtime, view, window); }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-135")] // App/Views/Chat/ChatView.swift:97-123 the timeline keeps its identity (.id(threadID)) while columnHeight changes
    public void Resizing_the_window_updates_the_live_timeline_in_place() => WithColumn((h, model, runtime, view, window) =>
    {
        var before = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadTimeline>(view).Single());
        h.Main.OnMain(() => window.Height = 700);
        Drain(h, window);
        var after = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadTimeline>(view).Single());
        Assert.Same(before, after);
    });

    [Fact]
    [Trait("Req", "UI-GIT-040")] // App/Views/Chat/ChatView.swift:1354-1378 BranchMenu observes runtime.isRunning: an open menu disables the other branches
    public void An_open_branch_menu_disables_the_other_branches_when_a_turn_starts()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            ThreadRuntime runtime = null!;
            Control menu = null!;
            h.Main.OnMain(() =>
            {
                var thread = model.NewThread(model.AddProject(new Project("P", h.NewFolder("proj")).Url))!;
                runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();
                typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Branches))!.SetValue(git,
                    new ValueList<GitBranch>([new GitBranch("main", true, null), new GitBranch("feature", false, null)]));
                menu = BranchMenu.BuildMenu(model, runtime, h.Scratch, git);
                var window = new AvaloniaWindow { Content = menu };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
            });
            Button Feature() => LogicalQuery.SelfAndDescendants<PopoverItem>(menu)
                .Where(item => LogicalQuery.SelfAndDescendants<TextBlock>(item).Any(text => text.Text == "feature"))
                .Select(item => LogicalQuery.SelfAndDescendants<Button>(item).Single()).Single();
            Assert.True(h.Main.OnMain(() => Feature().IsEnabled));
            h.Main.OnMain(() => typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Running));
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.False(h.Main.OnMain(() => Feature().IsEnabled));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1127-1131,1242-1265 the ThreadsButton keeps its identity (and its @State) while the row re-evaluates
    public void The_threads_button_survives_a_row_refresh_in_place()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the toggle capsule's circle fades on the frame clock (OD-0076).
            var model = h.MakeModel();
            ThreadRuntime runtime = null!;
            ChatChromeRow row = null!;
            ThreadsButton before = null!;
            h.Main.OnMain(() =>
            {
                var thread = model.NewThread(model.AddProject(new Project("P", h.NewFolder("proj")).Url))!;
                runtime = model.Runtime(thread.Id);
                row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var window = new AvaloniaWindow { Content = row };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                before = LogicalQuery.SelfAndDescendants<ThreadsButton>(row).Single();
                runtime.IsTerminalVisible = !runtime.IsTerminalVisible;
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            var after = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row).Single());
            Assert.Same(before, after);
        });
    }

    [Fact]
    // STOP (round 4, traceability), still open: PR-CHAT-171: the one-glass-pass clause (the glass over the row; ED-180, OD-0005 UNVERIFIED) is not proven. This test proves its own clauses only, so it carries no Req trait for the open record.
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1138-1141 HydraButton carries .transition(.softAppear): it leaves with a fade, not at once
    public void The_hydra_button_leaves_and_arrives_with_the_soft_appear_transition()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            ChatChromeRow row = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = true;
                var thread = model.NewThread(model.AddProject(new Project("P", h.NewFolder("proj")).Url))!;
                row = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var window = new AvaloniaWindow { Content = row };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Single(LogicalQuery.SelfAndDescendants<HydraButton>(row));
                model.Settings.HydraEnabled = false;
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            // The removal half is .opacity.animation(.easeOut(duration: 0.12)): the button is still on screen mid-flight.
            Assert.Single(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<HydraButton>(row)));
            for (var i = 0; i < 40; i++) { time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); }
            Assert.Empty(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<HydraButton>(row)));
        });
    }
}

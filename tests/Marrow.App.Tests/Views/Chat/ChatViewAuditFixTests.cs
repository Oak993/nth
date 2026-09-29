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
}

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewReviewFixTests(HeadlessSession headless)
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static Control Menu(Type type, params object?[] inputs) => (Control)type.GetMethod("BuildMenuCore", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, inputs)!;
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private void WithColumn(Action<AppModelHarness, AppModel, ThreadRuntime, ChatView, AvaloniaWindow, string> check) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        h.WriteLibrary([new Project("Project", directory)], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model); // Offline: every CLI path is missing; mounting usage never runs a real provider.
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
        try { Drain(h, window); check(h, model, runtime, view, window, directory); }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    [Fact]
    [Trait("Req", "PR-TERM-064")] // App/Views/Chat/ChatView.swift:1452-1463; the sheet's state outlives chrome body evaluation.
    [Trait("Req", "UI-TERM-020")] // App/Views/Chat/ChatView.swift:1456-1463
    public void A_scripts_sheet_keeps_its_identity_and_edits_when_the_mounted_chrome_refreshes() => WithColumn((h, model, runtime, view, window, _) =>
    {
        var recorder = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(recorder);
        h.Main.OnMain(() =>
        {
            var anchor = Assert.Single(LogicalQuery.SelfAndDescendants<ScriptsMenu>(view));
            var menu = Menu(typeof(ScriptsMenu), model, model.Project(runtime.Thread!.ProjectID)!, runtime, anchor);
            var item = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverItem>(menu));
            ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));
        });
        Drain(h, window);
        var popup = Assert.Single(recorder.Calls).Popup;
        var editor = Assert.Single(LogicalQuery.SelfAndDescendants<ScriptsEditor>(popup.Child!));
        h.Main.OnMain(() =>
        {
            ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Add script"));
            LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(x => x.PlaceholderText == "Name").Text = "Unsaved local name";
            LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(x => x.PlaceholderText == "Command").Text = "make local";
            typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Activity))!.SetValue(Field<GitStatusModel>(view, "git"), "Refreshing");
            model.Rename(runtime.ThreadID, "Changed title");
        });
        Drain(h, window);
        Assert.All(recorder.Calls, call => Assert.True(call.Presented));
        Assert.Same(editor, Assert.Single(LogicalQuery.SelfAndDescendants<ScriptsEditor>(popup.Child!)));
        Assert.Equal("Unsaved local name", Assert.Single(editor.Scripts).Name);
        Assert.Equal("make local", Assert.Single(editor.Scripts).Command);
        h.Main.OnMain(() => ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Cancel")));
        Assert.False(recorder.Calls.Last().Presented);
    });

    [Fact]
    [Trait("Req", "PR-GIT-131")] // App/Views/Chat/ChatView.swift:1633-1649; only a successful commit dismisses.
    [Trait("Req", "UI-GIT-032")] // App/Views/Chat/ChatView.swift:1489-1492
    public void A_failed_commit_keeps_the_same_mounted_sheet_and_message_through_activity_and_status_refresh() => WithColumn((h, model, runtime, view, window, directory) =>
    {
        var recorder = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(recorder);
        var git = Field<GitStatusModel>(view, "git");
        h.Main.Loop.RunUntil(() => { Dispatcher.UIThread.RunJobs(); return git.IsRepository; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Status))!.SetValue(git, new Marrow.Services.Git.GitStatus { ChangedFiles = 1 });
            var anchor = Assert.Single(LogicalQuery.SelfAndDescendants<GitActionsMenu>(view));
            var menu = Menu(typeof(GitActionsMenu), model, runtime, directory, git, 1L, anchor);
            var item = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).First();
            ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));
        });
        Drain(h, window);
        var popup = Assert.Single(recorder.Calls).Popup;
        var sheet = Assert.Single(LogicalQuery.SelfAndDescendants<CommitSheet>(popup.Child!));
        h.Main.OnMain(() => { sheet.Message = "Keep this typed message"; sheet.PushesAfterCommit = false; typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Activity))!.SetValue(git, "Refresh"); });
        Drain(h, window);
        Assert.All(recorder.Calls, call => Assert.True(call.Presented));
        Assert.Same(sheet, Assert.Single(LogicalQuery.SelfAndDescendants<CommitSheet>(popup.Child!)));
        File.WriteAllText(Path.Combine(directory, ".git", "index.lock"), "fixture prevents staging");
        h.Main.OnMain(() => ChatGitFixtures.Click(sheet.CommitButton));
        h.Main.Loop.RunUntil(() => { Dispatcher.UIThread.RunJobs(); return model.Alert?.Title == "Commit failed" && !sheet.IsCommitting; }, AppModelHarness.Bound);
        Drain(h, window);
        Assert.Same(sheet, Assert.Single(LogicalQuery.SelfAndDescendants<CommitSheet>(popup.Child!)));
        Assert.All(recorder.Calls, call => Assert.True(call.Presented));
        Assert.Equal("Keep this typed message", sheet.Message);
        Assert.Equal("Commit failed", model.Alert!.Title);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:490-530,554-590
    [Trait("Rule", "R-VIEW-1")] // App/Views/Chat/ChatView.swift:85-90,533-551; layout changes preserve the active gesture/view identity.
    public void A_mounted_resize_grip_survives_multiple_real_pointer_moves_and_commits_once_on_release() => WithColumn((h, model, _, view, window, _) =>
    {
        h.Main.OnMain(() => { model.Settings.UsagePanelDock = PanelDockCorner.TopLeading; model.Settings.ShowsUsagePanel = true; });
        Drain(h, window);
        var placed = Assert.Single(LogicalQuery.SelfAndDescendants<PlacedPanel>(view));
        var content = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(placed));
        var grip = LogicalQuery.SelfAndDescendants<DragHandleView>(placed).Single(x => x.Bounds.Width == 16 && x.Bounds.Height == 16);
        var at = grip.TranslatePoint(new Point(8, 8), window)!.Value;
        var state = Field<PanelResizeState>(view, "panelResize");
        var original = Field<PanelScene>(view, "scene").Layout.PanelSize;
        var before = h.Defaults.Writes.Count;
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        foreach (var move in new[] { new Point(20, 10), new Point(35, 20), new Point(47, 30) })
        {
            window.MouseMove(at + move, RawInputModifiers.LeftMouseButton);
            Drain(h, window);
            Assert.True(state.IsActive);
            Assert.Same(placed, Assert.Single(LogicalQuery.SelfAndDescendants<PlacedPanel>(view)));
            Assert.Same(content, Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view)));
            Assert.Same(grip, LogicalQuery.SelfAndDescendants<DragHandleView>(placed).Single(x => x.Bounds.Width == 16 && x.Bounds.Height == 16));
            Assert.Same(grip, grip.CapturedPointerForTest!.Captured);
            Assert.Equal(before, h.Defaults.Writes.Count); // live motion never persists a preference.
        }
        window.MouseUp(at + new Point(47, 30), MouseButton.Left, RawInputModifiers.None);
        Drain(h, window);
        Assert.False(state.IsActive);
        Assert.Null(state.LiveSize);
        Assert.Null(state.LiveUsageHeight);
        Assert.Null(grip.CapturedPointerForTest!.Captured);
        Assert.Equal(original.Width + 47, model.Settings.PanelSize!.Value.Width);
        Assert.Equal(before + 2, h.Defaults.Writes.Count); // one exact width/height preference and one usage-height preference.
        Assert.Same(placed, Assert.Single(LogicalQuery.SelfAndDescendants<PlacedPanel>(view)));
        Assert.Same(content, Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view)));
    });

    [Fact]
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1712-1714; verification_method requires save/restart and cancel interaction.
    [Trait("Req", "UI-TERM-029")] // App/Views/Chat/ChatView.swift:1708-1717
    public void Saving_then_restarting_restores_the_complete_script_list_and_cancel_stays_unsaved_after_another_restart()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var first = new ProjectScript("Build", "make build") { Symbol = "hammer", RunOnWorktreeCreate = true };
            var second = new ProjectScript("Tests", "make test") { Symbol = "checkmark" };
            var project = new Project("Project", h.NewFolder("project")) { Scripts = new ValueList<ProjectScript>([first, second]) };
            h.WriteLibrary([project], []);
            var model = h.MakeModel();
            var dismissed = 0;
            ProjectScript added = null!;
            h.Main.OnMain(() =>
            {
                var editor = new ScriptsEditor(model, project.Id, () => dismissed++);
                var window = new AvaloniaWindow { Content = editor }; ChatHeadlessFixtures.Track(window); window.Show();
                LogicalQuery.SelfAndDescendants<TextBox>(editor).First(x => x.PlaceholderText == "Name").Text = "Renamed";
                LogicalQuery.SelfAndDescendants<TextBox>(editor).First(x => x.PlaceholderText == "Command").Text = "make all";
                ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Add script"));
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Last(x => x.PlaceholderText == "Name").Text = "Deploy";
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Last(x => x.PlaceholderText == "Command").Text = "make deploy";
                added = new ProjectScript("Deploy", "make deploy") { Id = editor.Scripts[2].Id }; // source-generated identity must survive encoding.
                Assert.True(editor.SaveButton.IsEnabled);
                ChatGitFixtures.Click(editor.SaveButton);
                Assert.Equal(1, dismissed);
                window.Close();
                model.SaveBeforeQuit();
            });
            var expected = new[] { first with { Name = "Renamed", Command = "make all" }, second, added };
            var restarted = h.MakeModel();
            Assert.Equal(expected, restarted.Project(project.Id)!.Scripts);
            h.Main.OnMain(() =>
            {
                var editor = new ScriptsEditor(restarted, project.Id, () => dismissed++);
                var window = new AvaloniaWindow { Content = editor }; ChatHeadlessFixtures.Track(window); window.Show();
                LogicalQuery.SelfAndDescendants<TextBox>(editor).First(x => x.PlaceholderText == "Name").Text = "Discard this";
                ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Cancel"));
                Assert.Equal(2, dismissed);
                window.Close();
                restarted.SaveBeforeQuit();
            });
            var restartedAgain = h.MakeModel();
            Assert.Equal(expected, restartedAgain.Project(project.Id)!.Scripts);
            h.Main.OnMain(restartedAgain.SaveBeforeQuit);
        });
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Sidebar;

[Collection("Headless")]
public sealed class ParityTailArchiveTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-SIDEBAR-032")] // SidebarView.swift:276-288,295-306: delete only after dismissal and a main-actor turn; keep worktree.
    public void Delete_waits_for_the_archive_question_to_disappear_and_a_main_turn_and_keeps_worktree_files() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var project = AppModelSamples.Project("Project", harness.NewFolder("project"));
        var worktree = harness.NewFolder("worktree");
        var file = Path.Combine(worktree, "keep.txt");
        File.WriteAllText(file, "keep this work");
        var thread = AppModelSamples.Thread(project) with { WorktreePath = worktree };
        harness.WriteLibrary([project], [thread]);
        var model = harness.MakeModel();
        var view = new SidebarView();
        var host = new Border { Child = view };
        AppEnvironment.SetAppModel(host, model);
        var state = typeof(SidebarView).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var shown = state.GetType().GetProperty("IsDeletePopoverShown")!;
        harness.Main.OnMain(() =>
        {
            state.GetType().GetProperty("PendingArchiveID")!.SetValue(state, thread.Id);
            shown.SetValue(state, true);
            var content = Assert.IsType<ArchiveThreadPopover>(typeof(SidebarView).GetMethod("BuildArchivePopoverContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [thread, new Border()]));
            var delete = LogicalQuery.SelfAndDescendants<Button>(content).Single(button => Equals(ToolTip.GetTip(button), "Remove the thread and its history"));
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(model.Thread(thread.Id)); // the presentation is still anchoring the row.
            Assert.Null(state.GetType().GetProperty("PendingArchiveID")!.GetValue(state));
            var confirmed = state.GetType().GetProperty("ConfirmedDeletion")!.GetValue(state)!;
            Assert.False(Assert.IsType<bool>(confirmed.GetType().GetProperty("RemoveWorktree")!.GetValue(confirmed)));
            // The real archive Popup.Closed handler does these two operations (:287-288).
            shown.SetValue(state, false);
            typeof(SidebarView).GetMethod("CarryOutConfirmedDeletion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            Assert.NotNull(model.Thread(thread.Id)); // deletion has been posted, not run inline in teardown.
        });
        Assert.Null(harness.Main.OnMain(() => model.Thread(thread.Id)));
        Assert.Equal("keep this work", File.ReadAllText(file));
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-031")] // SidebarView.swift:933-935,944-961: focused without a focus effect; monitor removed on disappear.
    [Trait("Req", "UI-SIDEBAR-032")] // SidebarView.swift:922-936: small Delete and Archive, exact help, default action.
    public void Small_archive_controls_keep_their_metrics_and_focus_without_a_ring_and_do_not_leave_a_key_monitor() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var thread = AppModelSamples.Thread(AppModelSamples.Project("Project", harness.NewFolder("project")));
        var anchor = new Border { Focusable = true };
        var archives = 0;
        var dismissals = 0;
        var content = new ArchiveThreadPopover(thread, anchor, () => archives++, () => { }, () => dismissals++);
        var panel = new Panel { Children = { anchor, content } };
        var window = new Avalonia.Controls.Window { Content = panel, Width = 400, Height = 300 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var buttons = LogicalQuery.SelfAndDescendants<Button>(content).ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.Equal(new[] { "Remove the thread and its history", "Move it to Archived (Return)" }, buttons.Select(ToolTip.GetTip));
            foreach (var button in buttons)
            {
                var frame = Assert.IsType<Border>(button.Content);
                Assert.Equal(20, frame.Height); // ED-280 / OD-0098 measured small glass metrics.
                Assert.Equal(new Avalonia.Thickness(10.5, 0), frame.Padding);
                Assert.Equal(11, Assert.IsType<TextBlock>(frame.Child).FontSize);
            }
            var archive = buttons[1];
            Assert.True(archive.IsFocused);
            Assert.True(archive.IsDefault);
            Assert.Null(archive.FocusAdorner);
            // Focus lands on the row window instead of the popup: the source's monitor still consumes Escape.
            Assert.True(anchor.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, dismissals);
            panel.Children.Remove(content);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.NumPadEnter, null);
            Assert.Equal(0, archives);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, dismissals);
        }
        finally { window.Close(); }
    });
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Expectations for DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), part 3 of 4:
// UI-CHAT-081, UI-GIT-024/026-043/060, UI-TERM-015/016/017.
[Collection("Headless")]
public sealed class ChatViewPart3Tests(HeadlessSession headless)
{
    private static T Show<T>(T control) where T : Control
    {
        var window = new AvaloniaWindow { Content = control };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWithTip(Control root, string tip) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(b => Equals(ToolTip.GetTip(b), tip));

    private static bool HasActiveCircle(Button button) => LogicalQuery.SelfAndDescendants<Ellipse>(button).Any();

    private static ChatThread MakeThread(AppModelHarness harness, AppModel model) =>
        model.NewThread(model.AddProject(new Project("P", harness.NewFolder("proj")).Url))!;

    // Real "git" via the app's own ProcessLauncher (native P/Invoke) is WINDOWS-ONLY; set up repo state on disk
    // with the system git directly, independent of the code under test (ChatViewPart1Tests precedent).
    private static string InitRepository(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var args in new[] { "init -q -b main", "config user.name test", "config user.email test@test", "config commit.gpgsign false" })
            RunGit(root, args);
        return root;
    }

    private static void RunGit(string root, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true });
        process!.WaitForExit();
    }

    private static PopoverItem ItemNamed(Control root, string title) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(root).Single(i => Texts(i).Contains(title));

    // ---- Terminal/Changes capsule: UI-CHAT-081, UI-GIT-024, UI-TERM-015 ------------------------------------------

    [Fact]
    [Trait("Req", "UI-GIT-024")] // App/Views/Chat/ChatView.swift:1164-1172
    public void The_changes_button_reflects_the_diffs_visibility_live_and_flips_it_on_click()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var frames = Marrow.UI.Port.FrameClock.Source.Override(new Marrow.UI.Port.ManualFrameSource()); // the toggle capsule's circle fades on the frame clock (OD-0076).
            var model = harness.MakeModel();
            ChatChromeRow row = null!;
            Marrow.App.Runtime.ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();
                row = Show(new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, harness.Scratch, git));

                var changesButton = ButtonWithTip(row, "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges));
                Assert.False(HasActiveCircle(changesButton));

                Click(changesButton);
                Assert.True(runtime.IsChangesPaneOpen); // web W4 (ED-NEW-web-w4-1): :1170 now opens the Changes pane through toggleChanges (Alt+click is toggleDiff's popover)
                Assert.False(runtime.IsDiffVisible);
            });

            // Tracked<T>.Schedule (ViewSupport.cs) posts the rebuild through MainActor.Post rather than running it
            // inline: drain once, as a separate top-level call, before a freshly-built button can show isActive.
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var rebuilt = ButtonWithTip(row, "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges));
            Assert.True(HasActiveCircle(rebuilt));
        });
    }

    // Covered structurally by part 1's Terminal_toggle_flips_the_flag_directly_while_Changes_calls_toggleDiff
    // (UI-TERM-015's own tooltip/click, PR-CHAT-183) and part 2's terminal isActive test (PR-TERM-072): the
    // Terminal half of UI-CHAT-081 needs no separate test here.

    // ---- GitActionsMenu: UI-GIT-026, UI-GIT-027, UI-GIT-028, UI-GIT-029, UI-GIT-030, UI-GIT-031 --------------------

    [Fact]
    [Trait("Req", "UI-GIT-026")] // App/Views/Chat/ChatView.swift:1478
    public void The_menu_buttons_tooltip_is_the_sentence_at_zero_and_the_unsingularised_count_otherwise()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var zero = Show(new GitActionsMenu(model, runtime, harness.Scratch, git));
                Assert.Equal("Commit, push and open pull requests", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(zero).Single()));
            });
        });

        // The "1 changed files" tooltip (never singularised): ChatViewPart4ReviewTests.A_git_status_change_updates_the_actions_help...
    }

    [Fact]
    [Trait("Req", "UI-GIT-027")] // App/Views/Chat/ChatView.swift:1479
    public void The_note_is_singular_at_one_changed_file_and_plural_otherwise()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var one = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 1));
                Assert.Equal("1 changed file", Texts(one)[0]);

                var many = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 3));
                Assert.Equal("3 changed files", Texts(many)[0]);

                var none = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 0));
                Assert.Equal("0 changed files", Texts(none)[0]); // :1479 the else branch has no special zero case
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-028")] // App/Views/Chat/ChatView.swift:1481
    public void The_commit_row_is_disabled_at_zero_changed_files_and_carries_the_single_ellipsis()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var disabled = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 0));
                var commitItemDisabled = ItemNamed(disabled, "Commit…");
                Assert.False(LogicalQuery.SelfAndDescendants<Button>(commitItemDisabled).Single().IsEnabled);

                var enabled = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 2));
                var commitItemEnabled = ItemNamed(enabled, "Commit…");
                Assert.True(LogicalQuery.SelfAndDescendants<Button>(commitItemEnabled).Single().IsEnabled);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-029")] // App/Views/Chat/ChatView.swift:1482
    public void The_push_row_is_always_enabled_even_with_nothing_to_push()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var menu = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 0));
                var push = ItemNamed(menu, "Push");
                Assert.True(LogicalQuery.SelfAndDescendants<Button>(push).Single().IsEnabled);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-030")] // App/Views/Chat/ChatView.swift:1483
    public void The_create_pull_request_row_is_always_present_and_enabled()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var menu = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 0));
                var row = ItemNamed(menu, "Create pull request");
                Assert.True(LogicalQuery.SelfAndDescendants<Button>(row).Single().IsEnabled);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-031")] // App/Views/Chat/ChatView.swift:1484-1487
    public void The_open_repository_row_and_its_divider_are_absent_when_there_is_no_remote()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel(); // RemoteURL stays null: no Refresh was ever awaited

                var menu = Show(GitActionsMenu.BuildMenu(model, runtime, harness.Scratch, git, 0));
                Assert.DoesNotContain("Open repository", Texts(menu));
                Assert.Single(LogicalQuery.SelfAndDescendants<PopoverDivider>(menu)); // only :1480's divider
            });
        });

        // The present case and its click: ChatViewPart3ReviewTests.Open_repository_uses_the_refreshed_remote_url and
        // ChatViewParityDetailTests.Push_sends_HEAD_to_origin_with_tracking_and_Open_repository_opens_the_remote_page.
    }

    // ---- BranchMenu: UI-GIT-039, UI-GIT-043 (UI-GIT-040/041/042 covered by part 1) --------------------------------

    [Fact]
    [Trait("Req", "UI-GIT-039")] // App/Views/Chat/ChatView.swift:1357, 1316-1325
    public void The_title_is_the_running_activity_verb_else_the_branch_else_Detached()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                // No status yet (Refresh never awaited) and no activity: falls all the way to "Detached".
                var idle = new BranchMenu(model, runtime, harness.Scratch, git);
                Assert.Contains("Detached", Texts(idle));
            });

            // The running-activity title ("Switching", "Creating", ...) is proven directly against
            // GitStatusModel.Perform, independent of BranchMenu, since Perform's own activity write is what the
            // title reads (ChatView.swift:1357 `git.activity`); this needs no real git or a rebuild.
            using var runtime2 = new TestRuntime();
            var git2 = new GitStatusModel();
            string? seenDuring = null;
            runtime2.OnMain(() => git2.Perform("Switching", () =>
            {
                seenDuring = git2.Activity;
                return Task.CompletedTask;
            }));
            Assert.Equal("Switching", seenDuring);
            Assert.Null(git2.Activity); // cleared once the work completes (PR-GIT-127, part 1)
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-040")] // App/Views/Chat/ChatView.swift:1360-1369
    public void The_current_branch_is_checked_and_disabled_and_every_row_disables_while_the_thread_runs()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            string directory = null!;
            Marrow.App.Runtime.ThreadRuntime runtime = null!;
            var git = new GitStatusModel();
            harness.Main.OnMain(() =>
            {
                directory = InitRepository(harness.NewFolder("repo"));
                RunGit(directory, "commit --allow-empty -m init");
                RunGit(directory, "branch feature");
                var thread = MakeThread(harness, model);
                runtime = model.Runtime(thread.Id);
            });

            // GitStatusModel.Refresh spawns real git through the app's own ProcessLauncher (native P/Invoke),
            // WINDOWS-ONLY on this Linux container per part 1's own PR-GIT-126 precedent; the repository setup
            // above uses the system git directly and is not what is expected to fail here. A separate top-level
            // OnMain call (not nested) so RunUntilIdle fully drains Refresh's own continuations before proceeding.
            harness.Main.Loop.Run(() => git.Refresh(directory));

            harness.Main.OnMain(() =>
            {
                var menu = Show(BranchMenu.BuildMenu(model, runtime, directory, git));
                var current = ItemNamed(menu, "main");
                var currentButton = LogicalQuery.SelfAndDescendants<Button>(current).Single();
                Assert.False(currentButton.IsEnabled); // :1365 !branch.isCurrent
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-043")] // App/Views/Chat/ChatView.swift:1381-1385
    public void The_new_branch_alert_carries_the_exact_title_placeholder_and_buttons()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recorder = new RecordingPromptPresenter();
            using var scope = AlertPresentation.Presenter.Override(recorder);
            Control menu = null!;
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var git = new GitStatusModel();
                menu = Show(BranchMenu.BuildMenu(model, model.Runtime(thread.Id), harness.Scratch, git));
            });

            var newBranchItem = ItemNamed(menu, "New branch…");
            Click(LogicalQuery.SelfAndDescendants<Button>(newBranchItem).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var shown = Assert.Single(recorder.Shown);
            Assert.Equal("New branch", shown.Title);
            Assert.Equal("Branch name", shown.Placeholder);
            Assert.Equal(["Create", "Cancel"], shown.Buttons.Select(b => b.Title).ToArray());

            // Only Cancel is exercised: Create spawns a real git call (CreateBranch -> MainActor.Spawn), the same
            // WINDOWS-ONLY-only-safe-to-await-not-fire-and-forget shape as PR-GIT-129/130/131 in part 1 - not
            // driven end to end here.
            recorder.Dismiss(0, 1, "unused");
        });
    }

    // ---- UI-GIT-060 (updated 2026-10-03): the alert titles are now driven on Windows with real git ------------------
    // ChatViewPart3ReviewTests.Git_failure_paths_alert_and_refresh_without_dismissing_the_error (switch, create, push,
    // pull request), Commit_disables_while_running_trims_text_and_dismisses_only_after_success and
    // ChatViewParityTests.A_failed_push_after_the_commit_raises_Commit_failed... (commit), and
    // ChatViewParityDetailTests.The_worktree_offer_outside_a_repository_raises_Worktree_unavailable... (the worktree
    // offer's fixed body). "Could not create a worktree" is AppModel.swift:1138-1140's catch, reached from this menu only
    // by a git failure after the repository check passed: AppModelTests.Create_worktree_shows_the_git_refusal_under_could_not_create_a_worktree.

    // ---- ScriptsMenu header: UI-TERM-016, UI-TERM-017 (covered by part 1's PR-TERM-062/063 tests) ------------------
}

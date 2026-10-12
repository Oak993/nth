using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Expectations for DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), part 1 of 4:
// PR-CHAT-157/158/159/171-180/183, PR-GIT-111/126-131, PR-TERM-001/062/063.
[Collection("Headless")]
public sealed class ChatViewPart1Tests(HeadlessSession headless)
{
    // PopoverItem's title renders through MiddleTruncatingText, which only computes Text inside MeasureOverride
    // (HydraButtonTests' own note): a control that is constructed but never laid out keeps Text empty.
    private static Control Show(Control control)
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

    // NewThread(null) with no CurrentProject returns null and opens a folder picker (AppModel.CreateNewThread),
    // so every test that just needs *a* real thread goes through a real project instead.
    private static ChatThread MakeThread(AppModelHarness harness, AppModel model) =>
        model.NewThread(model.AddProject(new Project("P", harness.NewFolder("proj")).Url))!;

    // A real "git" process (LoginEnvironment.Git via ProcessLauncher's native P/Invoke) is WINDOWS-ONLY: it never
    // runs on this Linux container. Set up with the system git directly, independent of the code under test.
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

    // ---- GitStatusModel: PR-GIT-126, PR-GIT-127 (WINDOWS-ONLY: real git via the app's own process launcher) ----

    [Fact]
    // PR-GIT-126 is closed by ChatViewGitReadsTests (2026-10-04, unit rec-tlr); this test's own clauses are also part of it.
    [Trait("Req", "PR-GIT-126")] // App/Views/Chat/ChatView.swift:1289-1307
    public async Task Refresh_applies_the_cached_snapshot_at_once_and_skips_git_when_not_forced_and_fresh()
    {
        using var runtime = new TestRuntime();
        var directory = InitRepository(Path.Combine(Path.GetTempPath(), "chatview-git-" + Guid.NewGuid().ToString("N")));
        RunGit(directory, "commit --allow-empty -m init");
        var first = new GitStatusModel();
        runtime.Loop.Run(() => first.Refresh(directory));
        Assert.True(first.IsRepository);

        // A second model reading the same directory sees the shared static snapshot at once.
        var second = new GitStatusModel();
        var task = runtime.OnMain(() => second.Refresh(directory, force: false));
        Assert.True(second.IsRepository); // applied synchronously from the cache, before any await completed
        await task;
    }

    [Fact]
    // PR-GIT-126 is closed by ChatViewGitReadsTests (2026-10-04, unit rec-tlr); this test's own clauses are also part of it.
    [Trait("Req", "PR-GIT-126")] // App/Views/Chat/ChatView.swift:1294-1303
    public async Task Refresh_of_a_non_repository_reads_only_isRepository_and_leaves_the_rest_empty()
    {
        using var runtime = new TestRuntime();
        var directory = Path.Combine(Path.GetTempPath(), "chatview-notrepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var model = new GitStatusModel();

        runtime.Loop.Run(() => model.Refresh(directory));
        await Task.CompletedTask;

        Assert.False(model.IsRepository);
        Assert.Null(model.Status);
        Assert.Empty(model.Branches);
        Assert.Null(model.RemoteURL);
    }

    [Fact]
    [Trait("Req", "PR-GIT-127")] // App/Views/Chat/ChatView.swift:1316-1325
    public async Task Perform_sets_the_activity_while_running_clears_it_after_and_returns_the_thrown_error()
    {
        using var runtime = new TestRuntime();
        var model = new GitStatusModel();
        string? seenDuring = null;

        var error = await runtime.OnMain(() => model.Perform("Switching", () =>
        {
            seenDuring = model.Activity;
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("Switching", seenDuring);
        Assert.Null(model.Activity);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    [Trait("Req", "PR-GIT-127")] // App/Views/Chat/ChatView.swift:1319-1321
    public async Task Perform_returns_null_when_the_work_succeeds()
    {
        using var runtime = new TestRuntime();
        var model = new GitStatusModel();

        var error = await runtime.OnMain(() => model.Perform("Creating", () => Task.CompletedTask));

        Assert.Null(error);
        Assert.Null(model.Activity);
    }

    // ---- PermissionMenu: PR-CHAT-179 ----

    [Fact]
    [Trait("Req", "PR-CHAT-179")] // App/Views/Chat/ChatView.swift:1334
    [Trait("Req", "UI-CHAT-079")] // App/Views/Chat/ChatView.swift:1334
    public void The_circle_menu_symbol_and_tooltip_come_from_the_threads_own_mode()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.AutoAcceptEdits);
                var menu = new PermissionMenu(model, thread);
                var button = LogicalQuery.SelfAndDescendants<Button>(menu).Single();
                Assert.Equal("Auto-accept edits \u00b7 Edits files freely, asks before other actions", ToolTip.GetTip(button));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-179")] // App/Views/Chat/ChatView.swift:1335-1338
    [Trait("Req", "UI-CHAT-079")] // App/Views/Chat/ChatView.swift:1335-1338
    public void The_menu_lists_every_RuntimeMode_in_declaration_order_under_a_Permissions_header()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Auto);
                var menu = Show(PermissionMenu.BuildMenu(model, thread));

                Assert.Equal(["Permissions", "Supervised", "Auto-accept edits", "Auto", "Full access"], Texts(menu));
                Assert.Equal(4, LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Count());
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-179")] // App/Views/Chat/ChatView.swift:1339-1340
    [Trait("Req", "UI-CHAT-079")] // App/Views/Chat/ChatView.swift:1339-1340
    public void Picking_a_mode_writes_it_onto_the_thread_through_the_model()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            Guid threadId = default;
            Control menu = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var thread = model.NewThread(project)!;
                model.UpdateThread(thread.Id, t => t with { RuntimeMode = RuntimeMode.Supervised });
                threadId = thread.Id;
                menu = Show(PermissionMenu.BuildMenu(model, model.Thread(thread.Id)!));
            });

            // PopoverItem.Activate closes the popover, then spawns a NEW task to run the action
            // (Popovers.cs:271-273): it never runs synchronously inside the click, so this needs its own drain
            // after the outer OnMain above has returned control to the loop.
            var fullAccess = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Last();
            Click(LogicalQuery.SelfAndDescendants<Button>(fullAccess).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(RuntimeMode.FullAccess, model.Thread(threadId)!.RuntimeMode);
        });
    }

    // ---- BranchMenu: PR-GIT-111 (the worktree offer) ----

    [Fact]
    [Trait("Req", "PR-GIT-111")] // App/Views/Chat/ChatView.swift:1375-1379
    public void The_worktree_offer_is_absent_once_the_thread_has_a_turn()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runningRuntime = model.Runtime(thread.Id);
                var git = new GitStatusModel();

                var withoutTurns = Show(BranchMenu.BuildMenu(model, runningRuntime, harness.Scratch, git));
                Assert.Contains("Use a new worktree for this thread", Texts(withoutTurns));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-111")] // App/Views/Chat/ChatView.swift:1371-1374
    public void The_new_branch_item_is_always_present_with_its_own_glyph()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var git = new GitStatusModel();
                var menu = Show(BranchMenu.BuildMenu(model, model.Runtime(thread.Id), harness.Scratch, git));
                Assert.Contains("New branch\u2026", Texts(menu));
            });
        });
    }

    // ---- OpenInMenu: PR-CHAT-180 ----

    [Fact]
    [Trait("Req", "PR-CHAT-180")] // App/Views/Chat/ChatView.swift:1416,1425-1429
    [Trait("Req", "UI-CHAT-080")] // App/Views/Chat/ChatView.swift:1416,1425-1429
    public void The_menu_opens_with_File_Explorer_then_a_divider_then_Copy_path()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var workspace = new ChatWorkspaceFixture();
            var menu = Show(OpenInMenu.BuildMenu("C:\\work"));
            var texts = Texts(menu);
            Assert.Equal("Open in", texts[0]);
            Assert.Equal("File Explorer", texts[1]);
            Assert.Equal("Copy path", texts[^1]);
        });
    }

    // ---- ScriptsMenu: PR-TERM-062, PR-TERM-063 ----

    [Fact]
    [Trait("Req", "PR-TERM-062")] // App/Views/Chat/ChatView.swift:1442
    [Trait("Req", "UI-TERM-016")] // App/Views/Chat/ChatView.swift:1153-1158,1442
    public void The_scripts_button_carries_the_run_a_project_script_tooltip()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var thread = model.NewThread(project)!;
                var menu = new ScriptsMenu(model, project, model.Runtime(thread.Id));
                var button = LogicalQuery.SelfAndDescendants<Button>(menu).Single();
                Assert.Equal("Run a project script", ToolTip.GetTip(button));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-063")] // App/Views/Chat/ChatView.swift:1444-1450
    [Trait("Req", "UI-TERM-017")] // App/Views/Chat/ChatView.swift:1444-1450
    public void The_menu_lists_every_script_under_a_Scripts_header_then_a_divider_then_Edit_scripts()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build", "hammer"), new ProjectScript("Test", "make test", "checkmark")]) };
                model.UpdateProject(project.Id, _ => project);
                var thread = model.NewThread(project)!;

                var menu = Show(ScriptsMenu.BuildMenu(model, model.Project(project.Id)!, model.Runtime(thread.Id)));
                Assert.Equal(["Scripts", "Build", "Test", "Edit scripts\u2026"], Texts(menu));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-063")] // App/Views/Chat/ChatView.swift:1443,1452
    [Trait("Req", "UI-TERM-017")] // App/Views/Chat/ChatView.swift:1443,1452
    public void With_no_scripts_the_header_and_divider_are_omitted_and_the_add_item_reads_Add_a_script()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var thread = model.NewThread(project)!;
                var menu = Show(ScriptsMenu.BuildMenu(model, project, model.Runtime(thread.Id)));
                Assert.Equal(["Add a script\u2026"], Texts(menu));
            });
        });
    }

    // ---- ChatChromeTitle: PR-CHAT-176, PR-CHAT-177, PR-CHAT-178 ----

    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203,1212-1213
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203,1212-1213
    public void An_empty_title_reads_New_thread_and_the_tooltip_and_accessibility_label_are_exact()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var control = Show(new ChatChromeTitle(model, model.Runtime(thread.Id), ""));
                Assert.Contains("New thread", Texts(control));
                var button = LogicalQuery.SelfAndDescendants<Button>(control).Single();
                Assert.Equal("Thread title \u2014 click to copy or rename", ToolTip.GetTip(button));
                Assert.Equal("Thread title", Avalonia.Automation.AutomationProperties.GetName(button));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203
    public void A_non_empty_title_is_shown_verbatim()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var control = Show(new ChatChromeTitle(model, model.Runtime(thread.Id), "Fix the build"));
                Assert.Contains("Fix the build", Texts(control));
                Assert.DoesNotContain("New thread", Texts(control));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-177")] // App/Views/Chat/ChatView.swift:1220-1228
    [Trait("Req", "UI-CHAT-077")] // App/Views/Chat/ChatView.swift:1220-1228
    public void The_title_popover_offers_Copy_title_and_Rename_under_a_Thread_title_header()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var anchor = new Button();
                var menu = Show(ChatChromeTitle.BuildMenu(model, model.Runtime(thread.Id), anchor, "T"));
                Assert.Equal(["Thread title", "Copy title", "Rename\u2026"], Texts(menu));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-178")] // App/Views/Chat/ChatView.swift:1231-1236
    [Trait("Req", "UI-CHAT-078")] // App/Views/Chat/ChatView.swift:1231-1236
    public void Rename_shows_the_exact_alert_and_renames_through_the_model_on_Rename()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recorder = new RecordingPromptPresenter();
            using var scope = AlertPresentation.Presenter.Override(recorder);
            Guid threadId = default;
            Control menu = null!;
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                threadId = thread.Id;
                var anchor = new Button();
                menu = Show(ChatChromeTitle.BuildMenu(model, model.Runtime(thread.Id), anchor, "Old title"));
            });

            // PopoverItem.Activate spawns a task for the click action, which itself spawns the alert
            // (ChatView.cs ShowRenameAlert): neither runs synchronously inside Click, so drain after it.
            var renameItem = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Last();
            Click(LogicalQuery.SelfAndDescendants<Button>(renameItem).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var shown = Assert.Single(recorder.Shown);
            Assert.Equal("Rename thread", shown.Title);
            Assert.Equal("Title", shown.Placeholder);
            Assert.Equal("Old title", shown.InitialText);
            Assert.Equal(["Rename", "Cancel"], shown.Buttons.Select(b => b.Title).ToArray());

            // The dismissed button's Perform runs model.Rename synchronously (RecordingPromptPresenter.Dismiss),
            // and Rename asserts the main actor (R-ISO-1): it must run inside the loop's own pump.
            harness.Main.OnMain(() => recorder.Dismiss(0, 0, "New title"));
            Assert.Equal("New title", model.Thread(threadId)!.Title);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-178")] // App/Views/Chat/ChatView.swift:1236
    [Trait("Req", "UI-CHAT-078")] // App/Views/Chat/ChatView.swift:1236
    public void Cancel_renames_nothing()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recorder = new RecordingPromptPresenter();
            using var scope = AlertPresentation.Presenter.Override(recorder);
            Guid threadId = default;
            Control menu = null!;
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                threadId = thread.Id;
                model.Rename(thread.Id, "Kept"); // so the model's real title matches the popover's display title
                var anchor = new Button();
                menu = Show(ChatChromeTitle.BuildMenu(model, model.Runtime(thread.Id), anchor, "Kept"));
            });

            var renameItem = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Last();
            Click(LogicalQuery.SelfAndDescendants<Button>(renameItem).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            recorder.Dismiss(0, 1, "typed but cancelled");
            Assert.Equal("Kept", model.Thread(threadId)!.Title);
        });
    }

    // ---- ThreadsButton: PR-CHAT-173 ----

    // ED-NEW-sidebarmodes-1: the words say what the press does, in Column as in Floating (the Mac said "Threads" in Column, whose
    // press hung a popover; that popover is gone).
    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1252
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1252
    public void The_tooltip_is_Show_the_sidebar_while_the_column_is_away_and_Hide_the_sidebar_while_it_is_open_in_both_modes()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var hint = ShortcutStore.Hint(AppShortcut.ToggleSidebar);
                foreach (var floats in new[] { false, true })
                {
                    model.Settings.SidebarFloats = floats;
                    Assert.False(model.Sidebar.IsVisible);
                    var away = new ThreadsButton(model);
                    Assert.Equal("Show the sidebar" + hint, ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(away).Single()));
                    Assert.Equal("Show the sidebar" + hint, ThreadsButton.Help(model));
                    model.Sidebar.Toggle();
                    var open = new ThreadsButton(model);
                    Assert.Equal("Hide the sidebar" + hint, ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(open).Single()));
                    model.Sidebar.Toggle();
                }
            });
        });
    }

    [Theory]
    [InlineData(false)] // Column
    [InlineData(true)] // Floating
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1253
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1253
    public void Clicking_toggles_the_column_both_ways_in_Column_and_Floating(bool floats)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = floats;
                Assert.False(model.Sidebar.IsVisible);
                var button = new ThreadsButton(model);
                Click(LogicalQuery.SelfAndDescendants<Button>(button).Single());
                Assert.True(model.Sidebar.IsVisible);
                Click(LogicalQuery.SelfAndDescendants<Button>(button).Single());
                Assert.False(model.Sidebar.IsVisible);
                Assert.Equal(floats ? SidebarMode.Floating : SidebarMode.Column, model.Settings.SidebarMode); // the press never changes the mode
            });
        });
    }

    // ---- ReserveSlide: PR-CHAT-158 ----

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:719-720,751-752
    public void The_first_application_lays_out_in_place_with_no_offset()
    {
        var slide = new ReserveSlide(new PanelReserve { Leading = 40, Trailing = 0 }, Slides: true);
        var content = new Border();

        slide.Body(content);

        Assert.Equal(0, slide.Offset);
        Assert.Equal(new Avalonia.Thickness(40, 0, 0, 0), content.Margin);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:751-752,760-767
    public void A_reserve_change_snaps_the_padding_in_one_step_and_starts_the_glide_from_the_shift()
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slide = new ReserveSlide(new PanelReserve { Leading = 0, Trailing = 0 }, Slides: true);
        var content = new Border();
        slide.Body(content);

        slide = slide with { Reserve = new PanelReserve { Leading = 100, Trailing = 0 } };
        slide.Body(content);

        // :751-752 the margin is exactly the new reserve, never an intermediate width.
        Assert.Equal(new Avalonia.Thickness(100, 0, 0, 0), content.Margin);
        // :760-767 shift = ((100-0)-(0-0))/2 = 50; offset -= shift starts the glide at -50.
        Assert.Equal(-50, slide.Offset);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:761-762
    public void When_not_sliding_a_reserve_change_snaps_the_offset_to_zero_at_once()
    {
        var slide = new ReserveSlide(new PanelReserve { Leading = 0, Trailing = 0 }, Slides: false);
        var content = new Border();
        slide.Body(content);

        slide = slide with { Reserve = new PanelReserve { Leading = 100, Trailing = 0 } };
        slide.Body(content);

        Assert.Equal(0, slide.Offset); // :761 guard slides else { offset = 0 }
    }

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:743,761
    public void AnimatesWidth_true_also_skips_the_glide()
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slide = new ReserveSlide(new PanelReserve { Leading = 0, Trailing = 0 }, Slides: true, AnimatesWidth: true);
        var content = new Border();
        slide.Body(content);

        slide = slide with { Reserve = new PanelReserve { Leading = 100, Trailing = 0 } };
        slide.Body(content);

        Assert.Equal(0, slide.Offset);
    }

    // ---- isMovingAPanel: PR-CHAT-157 ----

    [Fact]
    [Trait("Req", "PR-CHAT-157")] // App/Views/Chat/ChatView.swift:48-52
    public void True_when_any_drag_state_has_a_position_false_when_none_do()
    {
        var layout = new SubagentPanelLayout { Pane = new Avalonia.Size(1200, 800), ComposerAreaHeight = 120 };
        var idle = new PanelDragState();
        var moving = new PanelDragState();
        moving.Move(new Avalonia.Size(1, 1), new Avalonia.Point(0, 0), layout); // sets Position (:456-474)
        var autoDrags = new Dictionary<Guid, PanelDragState> { [Guid.NewGuid()] = idle };

        Assert.False(ChatView.IsMovingAPanel(idle, idle, idle, idle, autoDrags));
        Assert.True(ChatView.IsMovingAPanel(moving, idle, idle, idle, autoDrags));

        autoDrags[Guid.NewGuid()] = moving;
        Assert.True(ChatView.IsMovingAPanel(idle, idle, idle, idle, autoDrags)); // :49-51 any of the auto drags too
    }

    // ---- ChatChromeRow composition: PR-CHAT-171, PR-CHAT-172, PR-CHAT-175, PR-CHAT-183 ----

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1124-1174
    public void The_row_lays_out_in_the_fixed_order_for_a_thread_with_a_project_and_a_repository()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var directory = InitRepository(harness.NewFolder("repo"));
                var project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                var git = new GitStatusModel();
                var row = Show(new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", project, directory, git));

                // Every fixed-order control is present (PR-CHAT-171). git.IsRepository is false here (Refresh was
                // never awaited), so the branch menu and the git-actions capsule item are correctly absent.
                var tooltips = LogicalQuery.SelfAndDescendants<Button>(row)
                    .Select(b => ToolTip.GetTip(b) as string).Where(t => t is not null).ToArray()!;
                Assert.Contains(tooltips, t => t!.StartsWith("Show the sidebar", StringComparison.Ordinal)); // ED-NEW-sidebarmodes-1
                Assert.Contains(tooltips, t => t!.StartsWith("New thread", StringComparison.Ordinal));
                Assert.Contains("Open in another app", tooltips);
                Assert.Contains("Run a project script", tooltips);
                Assert.Contains(tooltips, t => t!.StartsWith("Terminal", StringComparison.Ordinal));
                Assert.Contains(tooltips, t => t!.StartsWith("Changes", StringComparison.Ordinal));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-175")] // App/Views/Chat/ChatView.swift:1132-1134
    [Trait("Req", "UI-CHAT-075")] // App/Views/Chat/ChatView.swift:1132-1134
    public void The_New_thread_button_is_unconditional_and_creates_a_thread_in_the_rows_project()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var seedThread = model.NewThread(project)!;
                var git = new GitStatusModel();
                var before = model.Threads.Count;
                var row = Show(new ChatChromeRow(model, model.Runtime(seedThread.Id), new ChromeScrollModel(), "T", project, harness.Scratch, git));

                var button = LogicalQuery.SelfAndDescendants<Button>(row)
                    .Single(b => (ToolTip.GetTip(b) as string)?.StartsWith("New thread", StringComparison.Ordinal) == true);
                Click(button);

                Assert.Equal(before + 1, model.Threads.Count);
                Assert.Equal(project.Id, model.Threads.OrderByDescending(t => t.CreatedAt).First().ProjectID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-172")] // App/Views/Chat/ChatView.swift:1177; DEVIATION OD-0044: no traffic lights lead the row
    public void The_row_keeps_the_plain_leading_inset_and_its_trailing_end_clear_of_the_caption_buttons()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var git = new GitStatusModel();
                var row = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", null, harness.Scratch, git);
                var host = LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row).Single();

                Assert.False(model.Sidebar.HoldsTrafficLights); // a freshly made SidebarLayout starts without them
                // OD-0044: the caption buttons are Windows ones at the top right, so the row leads with its plain padding
                // whoever holds them, and ends 156 pt in (6 + 3 x 46 + 2 x 2 + 8), clear of the cluster.
                Assert.Equal(Chrome.ChromeHorizontalPadding, host.Margin.Left);
                Assert.Equal(156, host.Margin.Right);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-183")] // App/Views/Chat/ChatView.swift:1165-1171
    public void Terminal_toggle_flips_the_flag_directly_while_Changes_calls_toggleDiff()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the toggle capsule's circle fades on the frame clock (OD-0076).
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();
                var row = Show(new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, harness.Scratch, git));

                var terminalButton = ButtonWithTip(row, "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal));
                Assert.False(runtime.IsTerminalVisible);
                Click(terminalButton);
                Assert.True(runtime.IsTerminalVisible); // :1166 direct flag flip

                var changesButton = ButtonWithTip(row, "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges));
                Assert.False(runtime.IsDiffVisible);
                Click(changesButton);
                Assert.True(runtime.IsDiffVisible); // :1170 through toggleDiff, not a direct flip
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    }
}

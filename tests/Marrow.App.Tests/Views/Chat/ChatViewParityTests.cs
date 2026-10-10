using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Parity closure for DroppyCode/App/Views/Chat/ChatView.swift (1.7.0): the clauses the earlier parts left unproven.
//
// STOP App.Views.Chat.ChatView #6 (2026-10-03): the clauses still open after the parity pass, per record.
// Records: PR-CHAT-159, PR-GIT-126, PR-GIT-129, PR-USAGE-071 (UI-CHAT-006 was listed here and is closed: see the last item).
// - PR-CHAT-159: the ChatView half (the veil over the pane, fed by the timeline's own ChromeScrollModel) is proven
//   (ChatViewFollowupTests); the capture at travel 0/20/44 in both themes is not done. 2-chat-use.mov shows only the dark
//   theme at full progress (CATALOGUE timeline.top-edge-scrim, frames/gap-R2-02/scrim_profile.json), and the port's veil
//   there depends on the theme's glass tint (Chrome unit, PR-WIN-196). UNRESOLVED.
// - PR-GIT-126: CLOSED 2026-10-04 (unit rec-tlr). "status, branches and the web URL are read concurrently" (:1297-1299, three async lets) is observed through ShellObserver by
//   ChatViewGitReadsTests (each read's first call is held at its entry until all three are in; a fresh non-forced refresh starts no process; a non-repository exactly one).
// - PR-GIT-129: CLOSED 2026-10-04 (unit rec-tlr). The commit instructions reaching the model call (:1625) are read from ShellObserver, which reports the standard input the prompt
//   is written to (the stand-in needs no change), and the sheet's dismissal ending its task (.task, :1605) is observed: the stand-in process is killed and no late answer is
//   written (ChatViewCommitSheetTests). Every other clause is proven (ChatViewParityDetailTests, ChatViewPart3ReviewTests, ChatViewPart4ReviewTests).
// - PR-USAGE-071: the origin arithmetic is proven (ChatViewPart2ReviewTests, ChatViewReviewFixTests); its capture of a short
//   panel in a top and a bottom corner is not done (the videos dock the floating panel top-aligned only). UNRESOLVED.
// - UI-CHAT-006 (closed by the round 1 review, 2026-10-03): the source and the reference runtime agree. The transition is on
//   the zero-height frame (:1094-1101), so the circle scales about that frame's centre, 26 points below its own, and rises by more
//   than the offset's 10; the ring's 28 of 32 px in 2-chat-use.mov C 10.285 is that scale (0.6 at the start) seen 14 px low. Proven in
//   ChatViewParityReviewTests (both directions, position against the spring's own progress) and ChatViewFollowupTests. The
//   ThreadTimeline-side clauses (the 0.38/0.78 spring, the unanimated hide on a new row, the 0.35 s glide) are ThreadTimelineChatTests' (the glide
//   against the closed form of smooth(0.35), 2026-10-04) and ThreadTimelineMountedTests'; every one of these tests is Req UI-CHAT-006 now.
[Collection("Headless")]
public sealed class ChatViewParityTests(HeadlessSession headless)
{
    private static T Show<T>(T control) where T : Control
    {
        var window = new AvaloniaWindow { Content = control };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static ThreadRuntime NewRuntime(AppModelHarness h, AppModel model, string? directory = null) =>
        model.Runtime(model.NewThread(model.AddProject(new Project("P", directory ?? h.NewFolder("project")).Url))!.Id);

    private static string? Tip(Control control) => ToolTip.GetTip(control) as string;

    private static Button ButtonWithTip(Control root, string tip) => LogicalQuery.SelfAndDescendants<Button>(root).Single(b => Tip(b) == tip);

    private static string[] Symbols(Control root) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(root).Select(image => image.SymbolName ?? "").ToArray();

    private static PopoverItem[] Rows(Control menu) => LogicalQuery.SelfAndDescendants<PopoverItem>(menu).ToArray();

    private static string Title(PopoverItem item) => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText;

    private static PopoverItem Row(Control menu, string title) => Rows(menu).Single(item => Title(item) == title);

    private static void Click(PopoverItem item) => ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));

    // The checkmark column of a row built with isChecked (Popovers.swift:276-281): opacity 1 on the checked row.
    private static double CheckOpacity(PopoverItem item) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(item).Single(image => image.SymbolName == "checkmark").Opacity;

    private static Color Resolved(SwiftColor color, Control control) => color.Resolve(control.ActualThemeVariant);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ---- PR-GIT-131: the fallbacks and the silent success ------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-131")] // App/Views/Chat/ChatView.swift:1505-1527; OD-0012 F1 ("Opened from Marrow.")
    public void An_empty_subject_and_body_fall_back_to_Update_and_Opened_from_Marrow_and_a_silent_forge_opens_nothing()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "commit", "--allow-empty", "--allow-empty-message", "-m", "");
            var bare = h.NewFolder("bare");
            ChatGitFixtures.Run(bare, "init", "--bare", "-q");
            ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://github.com/test/offline.git");
            ChatGitFixtures.Run(directory, "config", "remote.origin.pushurl", bare);
            var forge = new ChatForgeScript(directory);
            forge.ReplyAlways(stdout: ""); // :1522 `else if let url`: a CLI that prints no URL opens nothing.
            var git = new GitStatusModel();
            var opened = 0;
            var previous = InlineText.OpenUrl;
            InlineText.OpenUrl = _ => opened++;
            try
            {
                h.Main.OnMain(() => ChatGitFixtures.InvokeStatic(typeof(GitActionsMenu), "OpenPullRequest", model, git, directory));
                h.Main.Loop.Run(() => ChatGitFixtures.Until(() => forge.Calls().Count == 1 && git.Status is not null && git.Activity is null));
                h.Main.RunUntilIdle();
                Assert.Equal(["pr", "create", "--title", "Update", "--body", "Opened from nth."], Assert.Single(forge.Calls()).Arguments);
                Assert.Equal(0, opened);
                Assert.Null(model.Alert);
                // :1510 the push ran first: the branch reached the push destination before the request was opened.
                Assert.Contains("main", ChatGitFixtures.Run(bare, "branch", "--list", "main"), StringComparison.Ordinal);
            }
            finally { InlineText.OpenUrl = previous; }
        });
    }

    // ---- PR-GIT-128: the order of the git actions menu ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-128")] // App/Views/Chat/ChatView.swift:1479-1487
    public void The_git_actions_menu_is_the_note_a_divider_three_actions_then_a_divider_and_Open_repository()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://github.com/test/offline.git");
            var git = new GitStatusModel();
            h.Main.Loop.Run(() => git.Refresh(directory));
            h.Main.OnMain(() =>
            {
                var menu = (StackPanel)GitActionsMenu.BuildMenu(model, NewRuntime(h, model, directory), directory, git, 2);
                var kinds = menu.Children.Select(child => child switch
                {
                    PopoverNote => "note",
                    PopoverDivider => "divider",
                    PopoverItem item => Title(item),
                    _ => child.GetType().Name,
                }).ToArray();
                Assert.Equal(["note", "divider", "Commit…", "Push", "Create pull request", "divider", "Open repository"], kinds);
                Assert.Equal(["checkmark.circle"], Symbols(Row(menu, "Commit…")).Where(s => s != "checkmark").ToArray());
                Assert.Contains("arrow.up.circle", Symbols(Row(menu, "Push")));
                Assert.Contains("arrow.triangle.pull", Symbols(Row(menu, "Create pull request")));
                Assert.Contains("safari", Symbols(Row(menu, "Open repository")));
                Assert.Equal("2 changed files", LogicalQuery.SelfAndDescendants<TextBlock>(menu.Children[0]).Single().Text);
            });
        });
    }

    // ---- PR-GIT-130: a failed push reports under the commit's title with git's own message ---------------------------

    [Fact]
    [Trait("Req", "PR-GIT-130")] // App/Views/Chat/ChatView.swift:1633-1649
    public void A_failed_push_after_the_commit_raises_Commit_failed_with_the_git_message_and_keeps_the_sheet()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var git = new GitStatusModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            File.WriteAllText(Path.Combine(directory, "change.txt"), "payload");
            CommitSheet sheet = null!;
            Task work = null!;
            var dismissed = 0;
            h.Main.OnMain(() =>
            {
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                sheet = new CommitSheet(model, NewRuntime(h, model, directory), directory, git, () => dismissed++);
                sheet.Message = "  Subject  ";
                sheet.PushesAfterCommit = true; // no remote: the push inside the same activity fails.
                work = ChatGitFixtures.Invoke<Task>(sheet, "Commit", model, directory, git, (Action)(() => dismissed++));
            });
            h.Main.Loop.Run(() => work);
            Assert.Equal(0, dismissed);
            Assert.Equal("Commit failed", model.Alert!.Title);
            Assert.False(string.IsNullOrWhiteSpace(model.Alert.Message));
            Assert.Equal("Subject\n", ChatGitFixtures.Run(directory, "log", "-1", "--pretty=%s").Replace("\r\n", "\n", StringComparison.Ordinal)); // :1637 trimmed.
            Assert.Equal("  Subject  ", sheet.Message); // the typed message is intact.
            Assert.NotNull(git.Status); // :1643 refreshed either way.
        });
    }

    // ---- PR-GIT-111: the worktree offer ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-111")] // App/Views/Chat/ChatView.swift:1375-1379
    public void The_worktree_offer_shows_only_without_a_worktree_or_turns_and_creates_the_worktree()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var git = new GitStatusModel();
            ThreadRuntime fresh = null!;
            h.Main.OnMain(() =>
            {
                fresh = NewRuntime(h, model, directory);
                var offered = BranchMenu.BuildMenu(model, fresh, directory, git);
                Assert.Contains("Use a new worktree for this thread", Rows(offered).Select(Title));
                Assert.Contains("square.stack.3d.up", Symbols(Row(offered, "Use a new worktree for this thread")));

                var withTurn = model.Runtime(model.NewThread(model.Project(fresh.Thread!.ProjectID))!.Id);
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(withTurn, new ValueList<TurnRecord>([new TurnRecord { Index = 0 }]));
                Assert.DoesNotContain("Use a new worktree for this thread", Rows(BranchMenu.BuildMenu(model, withTurn, directory, git)).Select(Title));

                Click(Row(offered, "Use a new worktree for this thread"));
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => model.Thread(fresh.ThreadID)!.WorktreePath is not null));
            Assert.True(Directory.Exists(model.Thread(fresh.ThreadID)!.WorktreePath));
            // A thread with a worktree no longer offers one.
            h.Main.OnMain(() => Assert.DoesNotContain("Use a new worktree for this thread", Rows(BranchMenu.BuildMenu(model, fresh, directory, git)).Select(Title)));
        });
    }

    // ---- PR-GIT-127 / UI-GIT-039: the branch title follows the activity verb ----------------------------------------

    [Theory]
    [InlineData("SwitchBranch", "Switching")]
    [InlineData("CreateBranch", "Creating")]
    [InlineData("Push", "Pushing")]
    [InlineData("OpenPullRequest", "Opening pull request")]
    [Trait("Req", "PR-GIT-127")] // App/Views/Chat/ChatView.swift:1316-1325,1391,1403,1498,1509
    [Trait("Req", "UI-GIT-039")] // App/Views/Chat/ChatView.swift:1354-1359
    public void Each_git_action_shows_its_own_verb_in_the_mounted_branch_title_and_clears_it_after(string operation, string verb)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "branch", "other");
            var git = new GitStatusModel();
            h.Main.Loop.Run(() => git.Refresh(directory));
            var titles = new List<string>();
            ChatChromeRow row = null!;
            Tracked<string?>? watch = null;
            h.Main.OnMain(() =>
            {
                var runtime = NewRuntime(h, model, directory);
                row = Show(new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", runtime.Thread is null ? null : model.Project(runtime.Thread.ProjectID), directory, git));
                string Shown() => LogicalQuery.SelfAndDescendants<TextBlock>(LogicalQuery.SelfAndDescendants<BranchMenu>(row).Single()).First().Text ?? "";
                titles.Add(Shown());
                // The row's own observation rebuilds the branch menu on every activity write (:1357); read what it shows.
                watch = new Tracked<string?>(() => git.Activity, _ => MainActor.Post(() => titles.Add(Shown())));
                var branch = operation is "SwitchBranch" or "CreateBranch";
                object?[] args = branch ? [model, git, directory, operation == "SwitchBranch" ? "other" : "fresh"] : [model, git, directory];
                ChatGitFixtures.InvokeStatic(branch ? typeof(BranchMenu) : typeof(GitActionsMenu), operation, args);
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => titles.Contains(verb) && git.Activity is null));
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            watch!.Dispose();
            Assert.Equal("main", titles[0]); // :1357 the branch while idle.
            Assert.Contains(verb, titles);
            Assert.NotEqual(verb, titles[^1]); // cleared after the work, whatever the outcome (:1318 defer).
            Assert.Equal(git.Status?.Branch ?? "Detached", titles[^1]);
        });
    }

    // ---- PR-TERM-077: no confirmation even when deleting is confirmed elsewhere ----------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-077")] // App/Views/Chat/ChatView.swift:1674-1679
    [Trait("Req", "UI-TERM-025")] // App/Views/Chat/ChatView.swift:1674-1679
    public void The_trash_button_ignores_confirm_before_deleting_and_is_a_borderless_destructive_trash_glyph()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var alerts = new ChatAlertFixture();
            using var shared = AlertPresentation.Presenter.Override(alerts);
            h.Main.OnMain(() =>
            {
                model.Settings.ConfirmBeforeDeleting = true;
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with
                { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make"), new ProjectScript("Test", "make test")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = new ScriptsEditor(model, project.Id, () => { }) { IsVisible = false };
                Show(editor);
                var trash = LogicalQuery.SelfAndDescendants<Button>(editor).Where(b => b.Content is not string).ToArray();
                Assert.Equal(2, trash.Length);
                Assert.All(trash, button => Assert.Equal(["trash"], Symbols(button)));
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(trash[0]).Single();
                Assert.Equal(Resolved(Chrome.Danger, glyph), BrushColor(Avalonia.Controls.Documents.TextElement.GetForeground(glyph))); // :1674 role: .destructive
                ChatGitFixtures.Click(trash[1]);
                Assert.Equal(["Build"], editor.Scripts.Select(s => s.Name));
                Assert.Empty(alerts.Shown); // :1675 no confirmation, settings.confirmBeforeDeleting not consulted.
            });
        });
    }

    // ---- PR-TERM-078 / UI-TERM-026: the outline's exact shape and the trim set ---------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-078")] // App/Views/Chat/ChatView.swift:1684-1691,1711-1718,1726-1729
    [Trait("Req", "UI-TERM-026")] // App/Views/Chat/ChatView.swift:1681-1691,1726-1729
    [Trait("Rule", "R-STR-7")]
    public void The_incomplete_outline_is_one_point_of_danger_at_point_six_five_on_a_six_point_continuous_corner_and_newlines_count()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with
                { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = new ScriptsEditor(model, project.Id, () => { }) { IsVisible = false };
                Show(editor);
                var command = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Command");
                // :1686-1691 `.overlay { ... strokeBorder ... }`: the outline is a shape over the field (ED-275 (3a): the exact continuous
                // SwiftShapeBorder, where the stroke is seen), beside it in a Panel, not a Border around it.
                var frame = LogicalQuery.SelfAndDescendants<SwiftShapeBorder>(editor).Single(b => b.Parent is Panel p && p.Children.Contains(command));
                Assert.Equal(new Thickness(1), frame.BorderThickness); // :1689 lineWidth: 1
                Assert.Equal(new CornerRadius(6), frame.CornerRadius); // :1688 cornerRadius: 6
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(frame)); // :1688 style: .continuous
                var danger = Resolved(Chrome.Danger, frame);
                Assert.Equal(Color.FromArgb((byte)Math.Round(danger.A * 0.65), danger.R, danger.G, danger.B), BrushColor(frame.BorderBrush)); // :1689
                Assert.Equal("Every script needs a command.", Tip(editor.SaveButton));
                command.Text = "make\n"; // .whitespaces holds no newline: a command holding a line feed is complete.
                Assert.Null(frame.BorderBrush);
                Assert.True(editor.SaveButton.IsEnabled);
                Assert.Equal("Save these scripts", Tip(editor.SaveButton));
                command.Text = "  "; // Unicode Zs only: incomplete.
                Assert.NotNull(frame.BorderBrush);
                Assert.False(editor.SaveButton.IsEnabled);
                command.Text = "\n";
                Assert.True(editor.SaveButton.IsEnabled);
            });
        });
    }

    // ---- PR-TERM-079 / UI-TERM-028: Add script leads the footer --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-079")] // App/Views/Chat/ChatView.swift:1701-1708
    [Trait("Req", "UI-TERM-028")] // App/Views/Chat/ChatView.swift:1701-1707
    public void Add_script_sits_at_the_leading_edge_of_the_footer_before_the_spacer_with_its_plus_glyph()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var editor = new ScriptsEditor(model, model.AddProject(new Project("P", h.NewFolder("project")).Url).Id, () => { });
                var window = new AvaloniaWindow { Width = 700, Height = 600, Content = editor };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var add = ChatGitFixtures.Button(editor, "Add script");
                var footer = Assert.IsType<Grid>(add.Parent);
                Assert.Equal(0, Grid.GetColumn(add));
                Assert.True(footer.ColumnDefinitions[1].Width.IsStar); // :1708 Spacer() after it.
                Assert.Equal(0, add.Bounds.X);
                Assert.True(add.Bounds.Right < ChatGitFixtures.Button(editor, "Cancel").Bounds.X);
                Assert.Equal(["plus"], Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(add).OfType<SwiftImage>().Select(i => i.SymbolName)); // :1705 Label("Add script", systemImage: "plus"), drawn.
            });
        });
    }

    // ---- PR-TERM-080: Save never merges ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1712-1714
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1711-1718
    public void Save_replaces_the_scripts_wholesale_so_a_script_added_elsewhere_while_open_is_lost()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with
                { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var dismissed = 0;
                var editor = new ScriptsEditor(model, project.Id, () => dismissed++) { IsVisible = false };
                Show(editor);
                model.UpdateProject(project.Id, p => p with { Scripts = p.Scripts.Appending(new ProjectScript("Elsewhere", "echo")) });
                Assert.Equal(["Build"], editor.Scripts.Select(s => s.Name)); // PR-TERM-075: the open copy is not observed.
                ChatGitFixtures.Click(editor.SaveButton);
                Assert.Equal(1, dismissed);
                Assert.Equal(["Build"], model.Project(project.Id)!.Scripts.Select(s => s.Name));
            });
        });
    }

    // ---- PR-TERM-075: the copy is taken on appear, never observed afterwards -------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-075")] // App/Views/Chat/ChatView.swift:1657,1723
    public void The_open_editor_keeps_its_copy_while_the_project_changes_and_edits_never_touch_the_project_until_save()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            ScriptsEditor editor = null!;
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with
                { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                editor = new ScriptsEditor(model, project.Id, () => { }) { IsVisible = false };
                Show(editor);
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Command").Text = "make all";
                Assert.Equal("make", model.Project(project.Id)!.Scripts[0].Command); // edits stay on the copy.
                model.UpdateProject(project.Id, p => p with { Scripts = new ValueList<ProjectScript>([]) });
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["make all"], editor.Scripts.Select(s => s.Command));
            Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(editor), t => t.PlaceholderText == "Command");
        });
    }

    // ---- PR-TERM-064 / UI-TERM-019: the glyphs of the last row -------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-064")] // App/Views/Chat/ChatView.swift:1452-1454
    [Trait("Req", "UI-TERM-019")] // App/Views/Chat/ChatView.swift:1452-1454
    public void The_last_row_carries_plus_when_empty_and_pencil_when_scripts_exist_after_a_divider()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var empty = model.AddProject(new Project("E", h.NewFolder("empty")).Url);
                var runtime = model.Runtime(model.NewThread(empty)!.Id);
                var emptyMenu = (StackPanel)ScriptsMenu.BuildMenu(model, empty, runtime);
                Assert.Equal(["plus"], Symbols(Row(emptyMenu, "Add a script…")));
                Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverDivider>(emptyMenu));
                var filled = model.AddProject(new Project("F", h.NewFolder("filled")).Url) with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make", "hammer")]) };
                model.UpdateProject(filled.Id, _ => filled);
                var filledMenu = (StackPanel)ScriptsMenu.BuildMenu(model, model.Project(filled.Id)!, runtime);
                Assert.Equal(["pencil"], Symbols(Row(filledMenu, "Edit scripts…")));
                Assert.IsType<PopoverDivider>(filledMenu.Children[^2]); // :1450 the divider precedes the last row.
                Assert.Equal(["hammer"], Symbols(Row(filledMenu, "Build"))); // PR-TERM-063 / UI-TERM-018: the script's own symbol.
            });
        });
    }

    // ---- PR-CHAT-179 / UI-CHAT-079: the permissions menu's symbols and its check -------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-179")] // App/Views/Chat/ChatView.swift:1334-1341
    [Trait("Req", "UI-CHAT-079")] // App/Views/Chat/ChatView.swift:1329-1343
    public void The_permissions_circle_shows_the_modes_symbol_and_each_row_its_symbol_with_the_check_on_the_current_mode()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                foreach (var current in Enum.GetValues<RuntimeMode>())
                {
                    var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, current);
                    var circle = new PermissionMenu(model, thread);
                    Assert.Equal([current.Symbol], Symbols(circle).Where(s => s != "checkmark").ToArray());
                    Assert.Single(LogicalQuery.SelfAndDescendants<ChromeCircleMenu>(circle));
                    var menu = Show(PermissionMenu.BuildMenu(model, thread));
                    var rows = Rows(menu);
                    Assert.Equal(Enum.GetValues<RuntimeMode>().Select(mode => mode.Title), rows.Select(Title));
                    Assert.Equal(Enum.GetValues<RuntimeMode>().Select(mode => mode.Symbol), rows.Select(row => Symbols(row).Single(s => s != "checkmark")));
                    Assert.Equal(Enum.GetValues<RuntimeMode>().Select(mode => mode == current ? 1d : 0d), rows.Select(CheckOpacity));
                }
            });
        });
    }

    // ---- PR-CHAT-175 / UI-CHAT-075: the New thread circle ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-175")] // App/Views/Chat/ChatView.swift:1132-1134
    [Trait("Req", "UI-CHAT-075")] // App/Views/Chat/ChatView.swift:1132-1134
    public void The_New_thread_circle_is_the_square_and_pencil_glyph_with_the_exact_hint_and_passes_a_nil_project_through()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var store = ShortcutStore.Shared.Override(h.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var home = model.AddProject(new Project("Home", h.NewFolder("home")).Url);
                var other = model.AddProject(new Project("Other", h.NewFolder("other")).Url);
                var seed = model.NewThread(other)!; // the selected thread's project becomes the model's current project.
                var row = Show(new ChatChromeRow(model, model.Runtime(seed.Id), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel()));
                var button = ButtonWithTip(row, "New thread (Ctrl+N)"); // OD-0008 B7: the default chord in Windows notation.
                Assert.Equal(["square.and.pencil"], Symbols(LogicalQuery.SelfAndDescendants<ChromeCircleButton>(row).Single(c => LogicalQuery.SelfAndDescendants<Button>(c).Contains(button))));
                var before = model.Threads.Count;
                ChatGitFixtures.Click(button);
                // :1133 model.newThread(in: nil): the model's own choice for a nil project, the current one.
                Assert.Equal(before + 1, model.Threads.Count);
                Assert.Equal(other.Id, model.Threads[^1].ProjectID);
                Assert.NotEqual(home.Id, model.Threads[^1].ProjectID);
            });
        });
    }

    // ---- PR-CHAT-183 / UI-CHAT-081 / UI-GIT-024 / UI-TERM-015: the toggle capsule ---------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-183")] // App/Views/Chat/ChatView.swift:1164-1172
    [Trait("Req", "UI-CHAT-081")] // App/Views/Chat/ChatView.swift:1164-1172
    [Trait("Req", "UI-GIT-024")] // App/Views/Chat/ChatView.swift:1164-1172
    [Trait("Req", "UI-TERM-015")] // App/Views/Chat/ChatView.swift:1164-1167
    public void The_toggle_capsule_is_terminal_divider_plusminus_and_both_light_from_state_set_anywhere()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames); // the toggle capsule's circles fade on the frame clock (OD-0076).
            ThreadRuntime runtime = null!;
            ChatChromeRow row = null!;
            string terminalTip = "", changesTip = "";
            h.Main.OnMain(() =>
            {
                runtime = NewRuntime(h, model);
                row = Show(new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel()));
                terminalTip = "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal);
                changesTip = "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges);
            });
            ChromeCapsule Capsule() => LogicalQuery.SelfAndDescendants<ChromeCapsule>(row).Last(); // :1164 the last slot.
            bool Active(string tip) => LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(ButtonWithTip(row, tip)).Any();
            void Drain()
            {
                h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
                time.Advance(TimeSpan.FromMilliseconds(500)); frames.Advance(TimeSpan.FromMilliseconds(500)); // the fade ends.
                h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
            }
            h.Main.OnMain(() =>
            {
                var parts = LogicalQuery.SelfAndDescendants<StackPanel>(Capsule()).Single(panel => panel.Children.Count == 3).Children;
                Assert.Equal(3, parts.Count);
                Assert.Equal(["terminal"], Symbols(parts[0]));
                Assert.IsType<ChromeDivider>(parts[1]);
                Assert.Equal(["plusminus"], Symbols(parts[2]));
                Assert.Equal(terminalTip, Tip(LogicalQuery.SelfAndDescendants<Button>(parts[0]).Single()));
                Assert.Equal(changesTip, Tip(LogicalQuery.SelfAndDescendants<Button>(parts[2]).Single()));
                runtime.IsTerminalVisible = true; // opened elsewhere (the shortcut, the drawer).
                runtime.IsDiffVisible = true; // opened elsewhere (the changes pill).
            });
            Drain();
            Assert.True(Active(terminalTip));
            Assert.True(Active(changesTip));
            h.Main.OnMain(() => { runtime.IsTerminalVisible = false; runtime.IsDiffVisible = false; });
            Drain();
            Assert.False(Active(terminalTip));
            Assert.False(Active(changesTip));
            // UI-GIT-024: the click goes through toggleDiff, which clears a focused file first (ThreadRuntime.swift:408-412).
            h.Main.OnMain(() =>
            {
                typeof(ThreadRuntime).GetMethod("SetDiffFocus", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(runtime, [new FileEdit[] { new("a.txt") }]);
                Assert.NotEmpty(runtime.DiffFocusEdits);
                ChatGitFixtures.Click(ButtonWithTip(row, changesTip));
                Assert.True(runtime.IsDiffVisible);
                Assert.Empty(runtime.DiffFocusEdits);
            });
        });
    }
}

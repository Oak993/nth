using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

// Parity closure for ChatView.swift (1.7.0): the row's exact order, the git menus' glyphs, the sheets' fills, the spinner.
[Collection("Headless")]
public sealed class ChatViewParityDetailTests(HeadlessSession headless)
{
    private static T Show<T>(T control, double width = 900) where T : Control
    {
        var window = new AvaloniaWindow { Content = control, Width = width, Height = 700 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static ThreadRuntime NewRuntime(AppModel model, string directory) =>
        model.Runtime(model.NewThread(model.AddProject(new Project("P", directory).Url))!.Id);

    private static string? Tip(Control control) => ToolTip.GetTip(control) as string;

    private static string[] Symbols(Control root) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(root).Select(image => image.SymbolName ?? "").ToArray();

    private static PopoverItem[] Rows(Control menu) => LogicalQuery.SelfAndDescendants<PopoverItem>(menu).ToArray();

    private static string Title(PopoverItem item) => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText;

    private static PopoverItem Row(Control menu, string title) => Rows(menu).Single(item => Title(item) == title);

    private static void Click(PopoverItem item) => ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void SetRepository(GitStatusModel git, bool value) =>
        typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.IsRepository))!.SetValue(git, value);

    // ---- PR-CHAT-171 / PR-TERM-062 / UI-TERM-016: the exact slot order under the four project/repository states -------

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1124-1174 (the order; the one-glass-pass clause stays STOP #3)
    [Trait("Req", "PR-TERM-062")] // App/Views/Chat/ChatView.swift:1153-1158,1442
    [Trait("Req", "UI-TERM-016")] // App/Views/Chat/ChatView.swift:1153-1158,1442
    public void The_row_lays_its_slots_out_in_the_fixed_order_with_the_conditional_ones_absent_not_disabled(bool withProject, bool repository)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = true;
                var runtime = NewRuntime(model, h.NewFolder("project"));
                var project = withProject ? model.Project(runtime.Thread!.ProjectID) : null;
                var git = new GitStatusModel();
                SetRepository(git, repository);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", project, h.Scratch, git);
                var stack = Assert.IsType<ChatChromeRowStack>(LogicalQuery.SelfAndDescendants<StackPanel>(row).First());
                Assert.Equal(10, stack.Spacing); // :1124
                string Kind(Control slot) => slot switch
                {
                    ThreadsButton => "threads",
                    HydraButton => "hydra",
                    PermissionMenu => "permissions",
                    BranchMenu => "branch",
                    ChatChromeTitle => "title",
                    ChromeCircleButton when Tip(LogicalQuery.SelfAndDescendants<Button>(slot).Single())!.StartsWith("New thread", StringComparison.Ordinal) => "new",
                    StackPanel trailing when trailing.Spacing == 8 => "capsules",
                    _ => LogicalQuery.SelfAndDescendants<Control>(slot).Select(c => Kind2(c)).FirstOrDefault(k => k is not null) ?? slot.GetType().Name,
                };
                static string? Kind2(Control c) => c switch { ThreadsButton => "threads", HydraButton => "hydra", _ => null };
                var expected = new List<string> { "threads", "new", "hydra", "permissions" };
                if (repository) expected.Add("branch");
                expected.AddRange(["title", "capsules"]);
                Assert.Equal(expected, stack.Children.Select(Kind));
                var trailing = (StackPanel)stack.Children[^1];
                Assert.Equal(2, trailing.Children.Count);
                Assert.All(trailing.Children, capsule => Assert.IsType<ChromeCapsule>(capsule));
                var first = LogicalQuery.SelfAndDescendants<StackPanel>(trailing.Children[0]).Single(panel => panel.Children.FirstOrDefault() is OpenInMenu).Children;
                var parts = first.Select(part => part switch
                {
                    OpenInMenu => "open",
                    ChromeDivider => "divider",
                    ScriptsMenu => "scripts",
                    GitActionsMenu => "git",
                    _ => part.GetType().Name,
                }).ToList();
                var wanted = new List<string> { "open" };
                if (withProject) wanted.AddRange(["divider", "scripts"]);
                if (repository) wanted.AddRange(["divider", "git"]);
                Assert.Equal(wanted, parts);
                if (withProject)
                {
                    var scripts = first.OfType<ScriptsMenu>().Single();
                    Assert.Equal(["play"], Symbols(scripts)); // :1442 symbol: "play"
                    Assert.Equal("Run a project script", Tip(LogicalQuery.SelfAndDescendants<Button>(scripts).Single()));
                }
            });
        });
    }

    // ---- PR-CHAT-172: the insets under OD-0044 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-172")] // App/Views/Chat/ChatView.swift:1176-1180; DEVIATION OD-0044 (caption buttons at the top right)
    public void The_row_keeps_fourteen_leading_twelve_top_and_its_trailing_end_clear_of_the_caption_buttons_whoever_holds_them()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var row = new ChatChromeRow(model, NewRuntime(model, h.NewFolder("p")), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                Show(row, 1400);
                var group = LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row).Single();
                Assert.False(model.Sidebar.HoldsTrafficLights);
                Assert.Equal(new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, WindowChrome.CaptionTrailingReserve, 0), group.Margin);
                Assert.Equal(14, Chrome.ChromeHorizontalPadding); // Chrome.swift:31
                Assert.Equal(12, Chrome.ChromeTopPadding); // Chrome.swift:32
                model.Sidebar.NoteLaidOutWidth(400);
                Assert.True(model.Sidebar.HoldsTrafficLights);
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
        });
    }

    // ---- PR-CHAT-173 / UI-CHAT-074: the Threads button arrives through soft appear --------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1125-1131; ED-NEW-sidebarmodes-1: the button arrives as Panel only is left
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1130 .transition(.softAppear)
    public void The_Threads_button_arrives_faded_in_when_Panel_only_is_left_even_with_the_column_holding_the_window_buttons()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            ChatChromeRow row = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarMode = SidebarMode.PanelOnly;
                model.Sidebar.NoteLaidOutWidth(400);
                row = new ChatChromeRow(model, NewRuntime(model, h.NewFolder("p")), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                Show(row, 1400);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadsButton>(row));
                model.Settings.SidebarMode = SidebarMode.Floating; // the window buttons stay in the sidebar; the button arrives all the same
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            double Seen() => h.Main.OnMain(() =>
            {
                var button = LogicalQuery.SelfAndDescendants<ThreadsButton>(row).Single();
                Control? at = button;
                var opacity = 1.0;
                while (at is not null && at != row) { opacity *= at.Opacity; at = at.Parent as Control; }
                return opacity;
            });
            Assert.True(Seen() < 0.5, "the button starts faded"); // not a cut.
            for (var i = 0; i < 40; i++) { time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, Seen(), 3);
        });
    }

    // ---- PR-TERM-063 / UI-TERM-018: a script row runs its script ---------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-063")] // App/Views/Chat/ChatView.swift:1446-1448
    [Trait("Req", "UI-TERM-018")] // App/Views/Chat/ChatView.swift:1444-1448
    public void Clicking_a_script_row_runs_that_script_in_a_terminal_and_shows_the_drawer()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            ThreadRuntime runtime = null!;
            try
            {
                h.Main.OnMain(() =>
                {
                    runtime = NewRuntime(model, h.NewFolder("project"));
                    var project = model.Project(runtime.Thread!.ProjectID)! with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("First", "exit"), new ProjectScript("Second", "exit")]) };
                    model.UpdateProject(project.Id, _ => project);
                    Assert.False(runtime.IsTerminalVisible);
                    Click(Row(ScriptsMenu.BuildMenu(model, model.Project(project.Id)!, runtime), "Second"));
                });
                h.Main.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();
                h.Main.OnMain(() =>
                {
                    Assert.True(runtime.IsTerminalVisible);
                    Assert.Equal("Second", Assert.Single(model.Terminals.Sessions(runtime.ThreadID)).Title);
                });
            }
            finally { h.Main.OnMain(() => model.Terminals.TerminateAll()); h.Main.RunUntilIdle(); }
        });
    }

    // ---- PR-TERM-074 / UI-TERM-021 / UI-TERM-022: the explanation's colour ------------------------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1661-1665
    [Trait("Req", "UI-TERM-021")] // App/Views/Chat/ChatView.swift:1661-1662
    [Trait("Req", "UI-TERM-022")] // App/Views/Chat/ChatView.swift:1663-1665
    public void The_heading_is_in_the_default_primary_colour_and_the_explanation_in_the_secondary_colour()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var editor = Show(new ScriptsEditor(model, model.AddProject(new Project("P", h.NewFolder("p")).Url).Id, () => { }));
                var explanation = LogicalQuery.SelfAndDescendants<TextBlock>(editor).Single(t => t.Text!.StartsWith("Scripts run", StringComparison.Ordinal));
                Assert.Equal(Chrome.SecondaryText.Resolve(explanation.ActualThemeVariant), BrushColor(explanation.Foreground));
                var heading = LogicalQuery.SelfAndDescendants<TextBlock>(editor).Single(t => t.Text == "Project scripts");
                Assert.False(heading.IsSet(TextBlock.ForegroundProperty)); // no foreground of its own: the default label colour.
                Assert.Equal(new Thickness(0), heading.Margin);
                Assert.Same(heading, ((StackPanel)heading.Parent!).Children[0]); // first child, leading
                Assert.Same(explanation, ((StackPanel)heading.Parent!).Children[1]); // then the explanation, 14 below
            });
        });
    }

    // ---- PR-TERM-076 / UI-TERM-024 / UI-TERM-026: the rounded-border bezel (OD-0042, OD-0045) ----------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1672-1673,1681-1683
    [Trait("Req", "UI-TERM-024")] // App/Views/Chat/ChatView.swift:1672-1674
    [Trait("Req", "UI-TERM-026")] // App/Views/Chat/ChatView.swift:1681-1682
    public void Name_and_Command_draw_the_rounded_border_bezel_the_Apple_kit_gives_the_regular_text_field()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("p")).Url) with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));
                foreach (var placeholder in new[] { "Name", "Command" })
                {
                    var field = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == placeholder);
                    Assert.Equal("RoundedBorder", field.GetValue(ChatDialogControls.InputStyleProperty)); // .textFieldStyle(.roundedBorder)
                    var bezel = field.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius == new CornerRadius(6));
                    Assert.Equal(24, bezel.MinHeight); // Apple macOS 27 UI Kit, Text Fields / 3 Rg: 24 high, radius 6
                    Assert.Equal(new Thickness(1), bezel.BorderThickness);
                    Assert.Equal(ChatDialogControls.FieldFill.Resolve(bezel.ActualThemeVariant), BrushColor(bezel.Background));
                    Assert.Equal(ChatDialogControls.FieldStroke.Resolve(bezel.ActualThemeVariant), BrushColor(bezel.BorderBrush));
                    var prompt = field.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == placeholder);
                    Assert.True(prompt.IsVisible == string.IsNullOrEmpty(field.Text));
                }
            });
        });
    }

    // ---- UI-GIT-026 / UI-GIT-039 / UI-GIT-040 / UI-GIT-041: the git buttons' glyphs and the branch rows ------------------

    [Fact]
    [Trait("Req", "UI-GIT-026")] // App/Views/Chat/ChatView.swift:1159-1162,1478
    [Trait("Req", "UI-GIT-039")] // App/Views/Chat/ChatView.swift:1354-1359
    [Trait("Req", "UI-GIT-040")] // App/Views/Chat/ChatView.swift:1360-1369
    [Trait("Req", "UI-GIT-041")] // App/Views/Chat/ChatView.swift:1370-1374
    public void The_git_controls_carry_their_glyphs_the_Branch_tooltip_and_the_check_on_the_current_branch()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "branch", "feature");
            var git = new GitStatusModel();
            h.Main.Loop.Run(() => git.Refresh(directory));
            h.Main.OnMain(() =>
            {
                var runtime = NewRuntime(model, directory);
                var actions = new GitActionsMenu(model, runtime, directory, git);
                Assert.Equal(["arrow.triangle.pull"], Symbols(actions));
                var branch = new BranchMenu(model, runtime, directory, git);
                Assert.Equal("Branch", Tip(LogicalQuery.SelfAndDescendants<Button>(branch).Single()));
                Assert.Equal(["arrow.triangle.branch", "chevron.down"], Symbols(branch));
                Assert.Contains("main", LogicalQuery.SelfAndDescendants<TextBlock>(branch).Select(t => t.Text));
                var menu = (Decorator)BranchMenu.BuildMenu(model, runtime, directory, git);
                var stack = (StackPanel)menu.Child!;
                Assert.Contains("Switch branch", LogicalQuery.SelfAndDescendants<TextBlock>(Assert.IsType<PopoverSectionHeader>(stack.Children[0])).Select(t => t.Text));
                var current = Row(menu, "main");
                Assert.Equal(1, LogicalQuery.SelfAndDescendants<SwiftImage>(current).Single(i => i.SymbolName == "checkmark").Opacity);
                Assert.Equal(0, LogicalQuery.SelfAndDescendants<SwiftImage>(Row(menu, "feature")).Single(i => i.SymbolName == "checkmark").Opacity);
                Assert.True(LogicalQuery.SelfAndDescendants<Button>(Row(menu, "feature")).Single().IsEnabled);
                var newBranch = Row(menu, "New branch…");
                Assert.Contains("plus", Symbols(newBranch));
                Assert.IsType<PopoverDivider>(stack.Children[stack.Children.IndexOf(newBranch) - 1]);
            });
        });
    }

    // ---- UI-GIT-029 / UI-GIT-031: Push sets the upstream; Open repository opens the page ----------------------------

    [Fact]
    [Trait("Req", "UI-GIT-029")] // App/Views/Chat/ChatView.swift:1482,1495-1503
    [Trait("Req", "UI-GIT-031")] // App/Views/Chat/ChatView.swift:1484-1487
    public void Push_sends_HEAD_to_origin_with_tracking_and_Open_repository_opens_the_remote_page()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var bare = h.NewFolder("bare");
            ChatGitFixtures.Run(bare, "init", "--bare", "-q");
            ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://github.com/test/offline.git");
            ChatGitFixtures.Run(directory, "config", "remote.origin.pushurl", bare);
            var git = new GitStatusModel();
            h.Main.Loop.Run(() => git.Refresh(directory));
            SwiftURL? opened = null;
            var previous = InlineText.OpenUrl;
            InlineText.OpenUrl = url => opened = url;
            try
            {
                Control menu = null!;
                h.Main.OnMain(() =>
                {
                    menu = GitActionsMenu.BuildMenu(model, NewRuntime(model, directory), directory, git, 0);
                    Click(Row(menu, "Push"));
                });
                h.Main.Loop.Run(() => ChatGitFixtures.Until(() => git.Activity is null && ChatGitFixtures.Run(bare, "branch", "--list", "main").Contains("main", StringComparison.Ordinal)));
                Assert.Equal("origin/main", ChatGitFixtures.Run(directory, "rev-parse", "--abbrev-ref", "main@{upstream}").Trim()); // -u
                Assert.Null(model.Alert);
                h.Main.OnMain(() => Click(Row(menu, "Open repository")));
                h.Main.RunUntilIdle();
                Assert.Equal("https://github.com/test/offline", opened!.AbsoluteString);
            }
            finally { InlineText.OpenUrl = previous; }
        });
    }

    // ---- UI-GIT-060: the worktree alert raised from the branch menu ------------------------------------------------

    [Fact]
    [Trait("Req", "UI-GIT-060")] // App/Views/Chat/ChatView.swift:1376-1378; AppModel.swift:1119-1122
    public void The_worktree_offer_outside_a_repository_raises_Worktree_unavailable_with_its_fixed_body()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var directory = h.NewFolder("not-a-repo");
                var runtime = NewRuntime(model, directory);
                Click(Row(BranchMenu.BuildMenu(model, runtime, directory, new GitStatusModel()), "Use a new worktree for this thread"));
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => model.Alert is not null));
            Assert.Equal("Worktree unavailable", model.Alert!.Title);
            Assert.Equal("New worktrees need a git repository with at least one commit.", model.Alert.Message);
        });
    }

    // ---- UI-GIT-033 / UI-GIT-034: the sheet's two boxes ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-GIT-033")] // App/Views/Chat/ChatView.swift:1547-1556
    [Trait("Req", "UI-GIT-034")] // App/Views/Chat/ChatView.swift:1557-1571
    public void The_summary_and_message_boxes_are_twelve_point_continuous_overlay_fills_and_the_placeholder_is_body_secondary()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                var sheet = Show(new CommitSheet(model, NewRuntime(model, h.NewFolder("repo")), h.Scratch, new GitStatusModel(), () => { }));
                Assert.False(sheet.SummaryFrame.IsVisible); // :1547 absent while the summary is empty.
                foreach (var box in new[] { sheet.SummaryFrame, sheet.MessageFrame })
                {
                    Assert.Equal(new CornerRadius(12), box.CornerRadius);
                    Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(box));
                    Assert.Equal(Chrome.Overlay(0.05).Resolve(box.ActualThemeVariant), BrushColor(box.Background));
                }
                var editor = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sheet.MessageFrame));
                Assert.Equal(Colors.Transparent, BrushColor(editor.Background)); // :1560 .scrollContentBackground(.hidden)
                var placeholder = LogicalQuery.SelfAndDescendants<TextBlock>(sheet.MessageFrame).Single(t => t.Text == "Commit message");
                Assert.Equal(13, placeholder.FontSize); // the default body style, as the editor (ED-059 body 13).
                Assert.Equal(editor.FontFamily, placeholder.FontFamily);
                Assert.Equal(Chrome.SecondaryText.Resolve(placeholder.ActualThemeVariant), BrushColor(placeholder.Foreground));
            });
        });
    }

    // ---- UI-GIT-035 / PR-GIT-129: the spinner in place of the sparkles while writing; the first draft on appear -------

    [Fact]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1579-1591
    // PR-GIT-129 is closed by ChatViewCommitSheetTests (2026-10-04, unit rec-tlr); this test's own clauses are also part of it.
    [Trait("Req", "PR-GIT-129")] // App/Views/Chat/ChatView.swift:1563,1579-1591,1605-1631
    public void Appearing_with_an_empty_message_writes_a_draft_showing_the_small_spinner_in_place_of_the_sparkles()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            File.WriteAllText(Path.Combine(directory, "changed.txt"), "payload");
            var folder = h.NewFolder("stand-in");
            var engine = ChatProviderFixture.Create(folder, "Add the changed file\n");
            File.WriteAllText(Path.Combine(folder, "sleep-ms.txt"), "1500");
            CommitSheet sheet = null!;
            AvaloniaWindow window = null!;
            ProgressBar spinner = null!;
            SwiftImage sparkles = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
                model.Settings.TextGeneration = TextGenerationChoice.Claude;
                sheet = new CommitSheet(model, NewRuntime(model, directory), directory, new GitStatusModel(), () => { });
                spinner = (ProgressBar)typeof(CommitSheet).GetField("writeForMeSpinner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sheet)!;
                sparkles = (SwiftImage)typeof(CommitSheet).GetField("writeForMeIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sheet)!;
                Assert.Equal("sparkles", sparkles.SymbolName);
                Assert.False(spinner.IsVisible);
                window = new AvaloniaWindow { Content = sheet, Width = 700, Height = 600 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.IsGenerating));
            h.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("changed.txt", sheet.Summary, StringComparison.Ordinal); // :1607 the summary first.
                Assert.True(spinner.IsVisible);
                Assert.False(sparkles.IsVisible);
                Assert.True(spinner.IsIndeterminate);
                Assert.Equal(new Avalonia.Size(16, 16), Assert.Single(spinner.GetVisualDescendants().OfType<Marrow.UI.Port.Nth.NthSpinner>()).Bounds.Size); // .controlSize(.small) ProgressView: the nth spinner (OD-0136 amendment 2, step 1 B)
                Assert.False(sheet.WriteForMeButton.IsEnabled);
                Assert.Equal("Writing a message…", LogicalQuery.SelfAndDescendants<TextBlock>(sheet.MessageFrame).Single(t => t.IsVisible && t.Text!.StartsWith("Writing", StringComparison.Ordinal)).Text);
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => !sheet.IsGenerating));
            h.Main.OnMain(() =>
            {
                Assert.Equal("Add the changed file", sheet.Message); // :1629 the reply replaces the empty message.
                Assert.False(spinner.IsVisible);
                Assert.True(sparkles.IsVisible);
                Assert.True(sheet.WriteForMeButton.IsEnabled);
                window.Close();
            });
            h.Main.RunUntilIdle();
        });
    }

    [Fact]
    // PR-GIT-129 is closed by ChatViewCommitSheetTests (2026-10-04, unit rec-tlr); this test's own clauses are also part of it.
    [Trait("Req", "PR-GIT-129")] // App/Views/Chat/ChatView.swift:1605-1613
    public void Without_a_text_engine_appearing_loads_the_summary_and_leaves_the_message_empty()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            File.WriteAllText(Path.Combine(directory, "changed.txt"), "payload");
            CommitSheet sheet = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                sheet = new CommitSheet(model, NewRuntime(model, directory), directory, new GitStatusModel(), () => { });
                var window = new AvaloniaWindow { Content = sheet, Width = 700, Height = 600 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.Summary.Contains("changed.txt", StringComparison.Ordinal)));
            h.Main.RunUntilIdle();
            Assert.True(sheet.SummaryFrame.IsVisible);
            Assert.False(sheet.IsGenerating);
            Assert.Equal("", sheet.Message);
            Assert.Equal("Turn on text generation in Settings > Source control", Tip(sheet.WriteForMeButton));
        });
    }
}

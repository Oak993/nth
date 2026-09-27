using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewPart4ReviewTests(HeadlessSession headless)
{
    private static ThreadRuntime Runtime(AppModel model, Project project) => model.Runtime(model.NewThread(project)!.Id);

    [Theory]
    [InlineData(0, "Generated commit")]
    [InlineData(1, "Keep my message")]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1575-1592,1612-1631
    [Trait("Req", "UI-GIT-034")] // App/Views/Chat/ChatView.swift:1562-1567
    public void Generation_follows_engine_settings_shows_busy_state_and_preserves_text_on_a_nil_reply(int exit, string expected)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var engine = ChatProviderFixture.Create(h.NewFolder("stand-in"), "Generated commit\n", exit);
            CommitSheet sheet = null!;
            ThreadRuntime runtime = null!;
            Avalonia.Controls.Window window = null!;
            Task work = null!;
            bool wasGenerating = false, wasEnabled = true;
            string? placeholder = null;
            h.Main.OnMain(() =>
            {
                model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                runtime = Runtime(model, model.AddProject(new Project("P", directory).Url));
                sheet = new CommitSheet(model, runtime, directory, new GitStatusModel(), () => { }) { IsVisible = false };
                sheet.Message = "Keep my message";
                window = new Avalonia.Controls.Window { Content = sheet }; window.Show();
                Assert.False(sheet.WriteForMeButton.IsEnabled);
                model.Settings.TextGeneration = TextGenerationChoice.Claude;
            });
            h.Main.RunUntilIdle();
            h.Main.OnMain(() =>
            {
                Assert.True(sheet.WriteForMeButton.IsEnabled);
                Assert.Equal("Write a commit message from these changes", ToolTip.GetTip(sheet.WriteForMeButton));
                work = ChatGitFixtures.Invoke<Task>(sheet, "Generate", model, runtime, directory, ((string Summary, string Patch)?)("summary", "patch"));
                wasGenerating = sheet.IsGenerating;
                wasEnabled = sheet.WriteForMeButton.IsEnabled;
                placeholder = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sheet)).PlaceholderText;
            });
            h.Main.Loop.Run(() => work);
            h.Main.OnMain(() =>
            {
                Assert.True(wasGenerating);
                Assert.False(wasEnabled);
                Assert.Equal("Writing a message\U00002026", placeholder);
                Assert.Equal(expected, sheet.Message);
                Assert.False(sheet.IsGenerating);
                Assert.True(sheet.WriteForMeButton.IsEnabled);
                model.Settings.TextGeneration = TextGenerationChoice.Off;
            });
            h.Main.RunUntilIdle();
            Assert.False(sheet.WriteForMeButton.IsEnabled);
            h.Main.OnMain(() => window.Close());
            h.Main.RunUntilIdle();
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-040")] // App/Views/Chat/ChatView.swift:1360-1369
    [Trait("Req", "UI-GIT-041")] // App/Views/Chat/ChatView.swift:1370-1374
    [Trait("Req", "UI-GIT-042")] // App/Views/Chat/ChatView.swift:1375-1379
    public void Branch_rows_stop_at_twenty_five_disable_while_running_and_hide_the_worktree_offer_when_assigned()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = Runtime(model, model.AddProject(new Project("P", h.NewFolder("project")).Url));
                var git = new GitStatusModel();
                var branches = Enumerable.Range(0, 30).Select(i => new Marrow.Services.Git.GitBranch($"b{i}", i == 0, null));
                typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Branches))!.SetValue(git, new ValueList<Marrow.Services.Git.GitBranch>(branches));
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Running);
                model.UpdateThread(runtime.ThreadID, t => t with { WorktreePath = h.NewFolder("worktree") });
                var menu = BranchMenu.BuildMenu(model, runtime, h.Scratch, git);
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).ToArray();
                Assert.Equal(26, rows.Length); // 25 branches, plus New branch.
                Assert.All(rows.Take(25), row => Assert.False(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row)).IsEnabled));
                Assert.Equal("b24", Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(rows[24])).FullText);
                Assert.Equal("New branch\U00002026", Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(rows[25])).FullText);
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Idle);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-070")] // App/Views/Chat/ChatView.swift:642-690
    public void Reordering_onto_an_occupied_full_side_swaps_slots_and_rewrites_corner_order()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = Runtime(model, model.AddProject(new Project("P", h.NewFolder("project")).Url));
                var usage = new FloatingPanelID.Usage(); var hydra = new FloatingPanelID.Hydra(); var helper = new FloatingPanelID.Subagent();
                var baseScene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 120, null);
                var scene = baseScene with
                {
                    Slots = new ValueMap<FloatingPanelID, PanelScene.PanelSlot>(new Dictionary<FloatingPanelID, PanelScene.PanelSlot>
                    {
                        [hydra] = new() { Corner = PanelDockCorner.TopLeading, Below = 0, Lift = 0 },
                        [usage] = new() { Corner = PanelDockCorner.TopLeading, Below = 1, Lift = 0 },
                        [helper] = new() { Corner = PanelDockCorner.BottomLeading, Below = 0, Lift = 0 },
                    }),
                    SlotsOnSide = new ValueMap<PanelDockSide, long>(new Dictionary<PanelDockSide, long> { [PanelDockSide.Leading] = 3 }),
                };
                ChatViewReviewFixtures.Invoke("Reorder", model, runtime, usage, scene.Docks.TopLeading, PanelDockCorner.TopLeading, scene);
                Assert.Equal(new FloatingPanelID[] { usage, hydra, helper }, runtime.PanelStackOrder);
                Assert.Equal(PanelDockCorner.TopLeading, model.Settings.UsagePanelDock);
                Assert.Equal(PanelDockCorner.TopLeading, runtime.HydraPanelDock);
                Assert.Equal(PanelDockCorner.BottomLeading, runtime.SubagentPanelDock);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-024")] // App/Views/Chat/ChatView.swift:1672-1674
    [Trait("Req", "UI-TERM-025")] // App/Views/Chat/ChatView.swift:1674-1679
    [Trait("Req", "UI-TERM-026")] // App/Views/Chat/ChatView.swift:1681-1691
    [Trait("Req", "UI-TERM-027")] // App/Views/Chat/ChatView.swift:1692-1693
    [Trait("Req", "UI-TERM-028")] // App/Views/Chat/ChatView.swift:1701-1707
    [Trait("Req", "UI-TERM-029")] // App/Views/Chat/ChatView.swift:1709-1710
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1711-1718
    public void Script_controls_have_templates_cancel_roles_glass_styles_and_a_flexible_footer()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = new ScriptsEditor(model, project.Id, () => { });
                Assert.All(LogicalQuery.SelfAndDescendants<Button>(editor), b => Assert.NotNull(b.Template));
                Assert.All(LogicalQuery.SelfAndDescendants<TextBox>(editor), b => Assert.NotNull(b.Template));
                Assert.NotNull(Assert.Single(LogicalQuery.SelfAndDescendants<CheckBox>(editor)).Template);
                Assert.True(ChatGitFixtures.Button(editor, "Cancel").IsCancel);
                Assert.NotNull(SwiftGlass.GetEffect(ChatGitFixtures.Button(editor, "Add script")));
                Assert.True(SwiftGlass.GetEffect(editor.SaveButton)!.IsInteractive);
                Assert.Equal(Chrome.Accent, SwiftGlass.GetEffect(editor.SaveButton)!.TintColor);
                Assert.Contains(LogicalQuery.SelfAndDescendants<Grid>(editor), grid => grid.ColumnDefinitions.Any(c => c.Width.IsStar));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-075")] // App/Views/Chat/ChatView.swift:1657,1723
    [Trait("Req", "UI-TERM-029")] // App/Views/Chat/ChatView.swift:1709-1710
    public void Appearance_reloads_the_projects_local_copy_and_Escape_discards_edits()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var dismissed = 0;
                var editor = new ScriptsEditor(model, project.Id, () => dismissed++) { IsVisible = false }; // attach without unrelated font layout.
                var saved = new ProjectScript("New from project", "make");
                model.UpdateProject(project.Id, p => p with { Scripts = new ValueList<ProjectScript>([saved]) });
                var window = new Avalonia.Controls.Window { Content = editor };
                window.Show();
                Assert.Equal(saved, Assert.Single(editor.Scripts));
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Name").Text = "Local edit";
                editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.Equal(1, dismissed);
                Assert.Equal(saved, Assert.Single(model.Project(project.Id)!.Scripts));
                window.Content = null;
                window.Content = editor;
                Assert.Equal(saved, Assert.Single(editor.Scripts));
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-019")] // App/Views/Chat/ChatView.swift:1452-1454
    [Trait("Req", "UI-TERM-020")] // App/Views/Chat/ChatView.swift:1456-1463
    [Trait("Req", "PR-TERM-064")] // App/Views/Chat/ChatView.swift:1452-1463
    public void The_scripts_sheet_uses_the_presentation_seam_and_the_source_backdrop_then_dismisses()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "ShowScriptsEditor", model, project.Id);
            });
            h.Main.RunUntilIdle();
            var shown = Assert.Single(recorder.Calls);
            Assert.True(shown.Presented);
            var editor = Assert.Single(LogicalQuery.SelfAndDescendants<ScriptsEditor>(shown.Popup.Child!));
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(editor));
            Assert.Single(LogicalQuery.SelfAndDescendants<WindowBackdrop>(shown.Popup.Child!));
            Assert.Equal(580, editor.Width);
            Assert.Equal(460, editor.Height);
            h.Main.OnMain(() => ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Cancel")));
            Assert.False(recorder.Calls.Last().Presented);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-032")] // App/Views/Chat/ChatView.swift:1489-1492,1604
    [Trait("Req", "UI-GIT-037")] // App/Views/Chat/ChatView.swift:1596-1597
    public void The_commit_sheet_uses_the_presentation_seam_and_Escape_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                ChatGitFixtures.InvokeStatic(typeof(GitActionsMenu), "ShowCommitSheet", model, Runtime(model, project), project.Path, new GitStatusModel());
            });
            h.Main.RunUntilIdle();
            var shown = Assert.Single(recorder.Calls);
            Assert.True(shown.Presented);
            var sheet = Assert.IsType<CommitSheet>(shown.Popup.Child);
            Assert.True(Popovers.GetIsCapsuleButtonBorderShape(sheet));
            Assert.Equal(560, sheet.Width);
            h.Main.OnMain(() => sheet.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }));
            Assert.False(recorder.Calls.Last().Presented);
        });
    }

    [Theory]
    [InlineData(PanelDockCorner.TopLeading, 1, 1)]
    [InlineData(PanelDockCorner.TopTrailing, -1, 1)]
    [InlineData(PanelDockCorner.BottomLeading, 1, -1)]
    [InlineData(PanelDockCorner.BottomTrailing, -1, -1)]
    [Trait("Req", "UI-USAGE-041")] // App/Views/Chat/ChatView.swift:527,554-590
    public void Grip_resize_pins_the_start_commit_is_exact_and_the_live_width_is_stepped(PanelDockCorner corner, int across, int down)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = true;
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var runtime = Runtime(model, project);
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 120, 180);
                var state = new PanelResizeState();
                var size = new Size(scene.Layout.PanelWidth, 180);
                var grip = ChatViewReviewFixtures.Invoke<PanelResize>("UsageResizer", model, state, corner, scene, size, PanelScene.UsageMinimumHeight);
                grip.OnResize(new Size(13 * across, 30 * down), PanelResizeAxes.Both);
                var exact = scene.Layout.Fitted(new Size(size.Width + 13, scene.Layout.PanelHeight));
                Assert.Equal(exact, state.LiveSize);
                Assert.Equal(210, state.LiveUsageHeight);
                var stepped = ChatViewReviewFixtures.Invoke<Size?>("LivePreferredSize", state);
                Assert.Equal(new Size(Math.Round(exact.Width / 12, MidpointRounding.AwayFromZero) * 12, exact.Height), stepped); // :595-598.
                grip.OnResize(new Size(25 * across, 40 * down), PanelResizeAxes.Both);
                var latest = scene.Layout.Fitted(new Size(size.Width + 25, scene.Layout.PanelHeight));
                Assert.Equal(latest, state.LiveSize); // translation remains relative to the initial drag size.
                Assert.Null(model.Settings.UsagePanelHeight);
                grip.OnResizeEnd();
                Assert.Equal(latest, model.Settings.PanelSize);
                Assert.Equal(220, model.Settings.UsagePanelHeight);
                Assert.Null(ChatViewReviewFixtures.Invoke<Size?>("LivePreferredSize", state));
            });
        });
    }

    [Theory]
    [InlineData(1600, PanelDockCorner.TopTrailing)]
    [InlineData(500, PanelDockCorner.TopLeading)]
    [Trait("Req", "PR-USAGE-070")] // App/Views/Chat/ChatView.swift:620-625
    public void Usage_docking_follows_the_pointer_but_keeps_the_locked_side_in_a_narrow_pane(double width, PanelDockCorner expected)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = true;
                model.Settings.UsagePanelDock = PanelDockCorner.BottomLeading;
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var runtime = Runtime(model, project);
                runtime.HydraPanelDock = PanelDockCorner.TopLeading;
                var members = new PanelMembers(runtime, model) with { Heads = new ValueList<ChatThread>([runtime.Thread! with { Id = Guid.NewGuid() }]) };
                var scene = new PanelScene(members, runtime, model, new Size(width, 1200), 120, 180);
                ChatViewReviewFixtures.Invoke("DockUsage", model, new Point(1600, -500), scene);
                Assert.Equal(expected, model.Settings.UsagePanelDock);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1123
    public void The_glass_group_retains_the_source_spacing_for_the_deferred_compositor()
    {
        headless.OnUiThread(() =>
        {
            var group = new GlassEffectContainer(2, () => new Border());
            var spacing = typeof(GlassEffectContainer).GetProperty("Spacing");
            Assert.NotNull(spacing);
            Assert.Equal(2d, spacing.GetValue(group));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-172")] // App/Views/Chat/ChatView.swift:1177-1180
    public void Caption_clearance_moves_through_frames_when_the_sidebar_takes_the_caption_buttons()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var model = h.MakeModel();
            ChatChromeRow row = null!;
            Avalonia.Controls.Window window = null!;
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                row = new ChatChromeRow(model, Runtime(model, project), new ChromeScrollModel(), "T", project, project.Path, new GitStatusModel()) { IsVisible = false };
                window = new Avalonia.Controls.Window { Content = row }; window.Show();
                model.Sidebar.NoteLaidOutWidth(400); // ownership follows measured caption clearance, not IsVisible.
            });
            h.Main.RunUntilIdle();
            h.Main.AdvanceMilliseconds(50); frames.Advance(TimeSpan.Zero);
            var margin = Assert.Single(LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row)).Margin.Left;
            Assert.InRange(margin, Chrome.ChromeHorizontalPadding + .001, Chrome.ChromeHorizontalPadding + Chrome.TrafficLightsWidth + Chrome.TrafficLightClearance - .001);
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
            Assert.Equal(Chrome.ChromeHorizontalPadding, Assert.Single(LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row)).Margin.Left);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-131")] // App/Views/Chat/ChatView.swift:1505-1527
    [Trait("Req", "UI-GIT-030")] // App/Views/Chat/ChatView.swift:1483,1505-1527
    public void Pull_request_uses_the_last_commit_and_opens_the_offline_forges_returned_url()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "commit", "--allow-empty", "-m", "Subject", "-m", "Details");
            var bare = h.NewFolder("local-bare");
            ChatGitFixtures.Run(bare, "init", "--bare", "-q");
            ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://github.com/test/offline.git");
            ChatGitFixtures.Run(directory, "config", "remote.origin.pushurl", bare); // isolated fixture destination; no network or port branch.
            var forge = new ChatForgeScript(directory);
            forge.ReplyAlways(stdout: "https://github.com/test/offline/pull/7\n");
            var git = new GitStatusModel();
            SwiftURL? opened = null;
            var previous = InlineText.OpenUrl;
            InlineText.OpenUrl = url => opened = url;
            try
            {
                h.Main.OnMain(() => ChatGitFixtures.InvokeStatic(typeof(GitActionsMenu), "OpenPullRequest", model, git, directory));
                h.Main.Loop.Run(() => ChatGitFixtures.Until(() => opened is not null && git.Status is not null));
                Assert.Equal("https://github.com/test/offline/pull/7", opened!.AbsoluteString);
                Assert.Equal(["pr", "create", "--title", "Subject", "--body", "Details"], Assert.Single(forge.Calls()).Arguments);
                Assert.Null(model.Alert);
                Assert.Null(git.Activity);
            }
            finally { InlineText.OpenUrl = previous; }
        });
    }
}

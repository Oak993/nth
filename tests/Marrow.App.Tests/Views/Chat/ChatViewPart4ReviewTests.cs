using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Port;
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

    [Fact]
    [Trait("Rule", "R-FILE-1")] // App/Views/Chat/ChatView.swift:1191-1192,1248,1331,1347-1349,1436-1437,1470-1472,1533-1535,1655 (original view inputs).
    public void The_original_view_input_constructor_signatures_remain_available()
    {
        (Type View, Type[] Inputs)[] signatures = [
            (typeof(ChatChromeTitle), [typeof(ThreadRuntime), typeof(string)]),
            (typeof(ThreadsButton), []),
            (typeof(PermissionMenu), [typeof(ChatThread)]),
            (typeof(BranchMenu), [typeof(ThreadRuntime), typeof(string), typeof(GitStatusModel)]),
            (typeof(ScriptsMenu), [typeof(Project), typeof(ThreadRuntime)]),
            (typeof(GitActionsMenu), [typeof(ThreadRuntime), typeof(string), typeof(GitStatusModel)]),
            (typeof(CommitSheet), [typeof(ThreadRuntime), typeof(string), typeof(GitStatusModel)]),
            (typeof(ScriptsEditor), [typeof(Guid)]),
        ];
        Assert.All(signatures, signature => Assert.NotNull(signature.View.GetConstructor(signature.Inputs)));
    }

    [Fact]
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1653-1655,1709,1723
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1709,1723
    public void The_original_editor_inputs_use_the_inherited_model_and_dismiss_action()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var constructor = typeof(ScriptsEditor).GetConstructor([typeof(Guid)]);
                Assert.NotNull(constructor);
                var editor = (Control)constructor.Invoke([project.Id]);
                editor.IsVisible = false;
                var window = new Avalonia.Controls.Window { Content = editor };
                AppEnvironment.SetAppModel(window, model);
                var property = typeof(Marrow.App.Port.ChatSheetPresentation).GetField("DismissProperty", BindingFlags.Public | BindingFlags.Static);
                Assert.NotNull(property);
                var dismissed = false;
                window.SetValue((AttachedProperty<Action?>)property.GetValue(null)!, () => dismissed = true);
                ChatHeadlessFixtures.Track(window); window.Show();
                Assert.Equal("Build", LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(field => field.PlaceholderText == "Name").Text);
                ChatGitFixtures.Click(ChatGitFixtures.Button(editor, "Cancel"));
                Assert.True(dismissed);
                Assert.Equal("Build", model.Project(project.Id)!.Scripts[0].Name);
            });
        });
    }

    // ED-NEW-sidebarmodes-1: the Column-mode popover of the sidebar (:1255-1259) is gone; the button is the round glass button alone, with no
    // popover host beside it, and its press brings out the window's own column at the sidebar's live width instead.
    [Fact]
    [Trait("Req", "PR-CHAT-174")] // App/Views/Chat/ChatView.swift:1255-1259, removed
    public void The_threads_button_has_no_popover_factory_and_its_press_brings_out_the_column_at_the_sidebars_width()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                Assert.Null(typeof(ThreadsButton).GetMethod("BuildSidebarPopover", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public));
                var threads = new ThreadsButton(model);
                var circle = Assert.IsType<ChromeCircleButton>(threads.Child);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Sidebar.SidebarView>(threads));
                Assert.False(model.Sidebar.IsVisible);
                LogicalQuery.SelfAndDescendants<Button>(circle).Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.True(model.Sidebar.IsVisible);
                Assert.Equal(model.Sidebar.Width, model.Sidebar.RenderedWidth);
                Assert.InRange(model.Sidebar.Width, SidebarLayout.MinimumWidth, SidebarLayout.MaximumWidth);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-128")] // App/Views/Chat/ChatView.swift:1159-1162,1477-1488
    [Trait("Req", "UI-GIT-026")] // App/Views/Chat/ChatView.swift:1477-1478
    public void A_git_status_change_updates_the_actions_help_and_removing_the_repository_removes_the_menu()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            ChatChromeRow row = null!;
            var git = new GitStatusModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.IsRepository))!.SetValue(git, true);
                row = new ChatChromeRow(model, Runtime(model, project), new ChromeScrollModel(), "Title", project, project.Path, git) { IsVisible = false };
                var window = new Avalonia.Controls.Window { Content = row };
                ChatHeadlessFixtures.Track(window); window.Show();
                Assert.Equal("Commit, push and open pull requests", ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(Assert.Single(LogicalQuery.SelfAndDescendants<GitActionsMenu>(row))))));
                typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.Status))!.SetValue(git, new Marrow.Services.Git.GitStatus { ChangedFiles = 1 });
            });
            h.Main.RunUntilIdle();
            Assert.Equal("1 changed files", ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(Assert.Single(LogicalQuery.SelfAndDescendants<GitActionsMenu>(row))))));
            h.Main.OnMain(() => typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.IsRepository))!.SetValue(git, false));
            h.Main.RunUntilIdle();
            Assert.Empty(LogicalQuery.SelfAndDescendants<GitActionsMenu>(row));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1147-1151,1203-1214
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1214
    public void Updating_the_title_input_of_a_title_not_yet_drawn_takes_the_new_text_in_place_without_replacing_its_button()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = Runtime(model, model.AddProject(new Project("P", h.NewFolder("project")).Url));
                var title = new ChatChromeTitle(model, runtime, "Before");
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(title));
                var update = typeof(ChatChromeTitle).GetMethod("UpdateTitle", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(update);
                update.Invoke(title, ["After"]);
                Assert.Same(button, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(title)));
                Assert.Equal("After", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(title)).Text);
                Assert.Equal(1, button.Opacity);
                update.Invoke(title, [""]);
                Assert.Equal("New thread", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(title)).Text);
            });
        });
    }

    [Theory]
    [InlineData(PanelDockCorner.TopLeading, 370, 430)]
    [InlineData(PanelDockCorner.TopTrailing, 330, 430)]
    [InlineData(PanelDockCorner.BottomLeading, 370, 370)]
    [InlineData(PanelDockCorner.BottomTrailing, 330, 370)]
    [Trait("Rule", "R-FILE-1")] // App/Views/Chat/ChatView.swift:533-551 (no packet item names the shared-panel resizer).
    public void Shared_grips_grow_away_from_the_corner_and_reset_only_the_shared_size(PanelDockCorner corner, double width, double height)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.PanelSize = new Size(350, 400);
                model.Settings.UsagePanelHeight = 222;
                var runtime = Runtime(model, model.AddProject(new Project("P", h.NewFolder("project")).Url));
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 120, null);
                var resize = new PanelResizeState();
                var grip = ChatViewReviewFixtures.Invoke<PanelResize>("Resizer", model, resize, corner, scene);
                grip.OnResize(new Size(20, 30), PanelResizeAxes.Both);
                Assert.Equal(new Size(width, height), resize.LiveSize); // :541-546, signs and fitting; ample room avoids clipping.
                Assert.Equal(new Size(350, 400), model.Settings.PanelSize);
                grip.OnResizeEnd();
                Assert.Equal(new Size(width, height), model.Settings.PanelSize);
                Assert.False(resize.IsActive);
                grip.OnReset();
                Assert.Null(model.Settings.PanelSize);
                Assert.Equal(222, model.Settings.UsagePanelHeight);
            });
        });
    }

    [Fact]
    // PR-GIT-126 is closed by ChatViewGitReadsTests (2026-10-04, unit rec-tlr); this test's own clauses are also part of it.
    [Trait("Req", "PR-GIT-126")] // App/Views/Chat/ChatView.swift:1284-1307
    public void Git_snapshots_are_shared_and_are_fresh_only_while_their_age_is_less_than_thirty_seconds()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var first = new GitStatusModel();
            h.Main.Loop.Run(() => first.Refresh(directory));
            Assert.Equal("main", first.Status!.Branch);
            ChatGitFixtures.Run(directory, "switch", "-c", "changed");
            var second = new GitStatusModel();
            h.Main.Advance(TimeSpan.FromSeconds(29));
            h.Main.Loop.Run(() => second.Refresh(directory, force: false));
            Assert.Equal("main", second.Status!.Branch); // :1291 cached state applied before the age guard.
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.Loop.Run(() => second.Refresh(directory, force: false));
            Assert.Equal("changed", second.Status!.Branch); // :1292 strict <30, so exactly 30 is stale.
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1575,1591-1592
    public void Write_for_me_reacts_to_observable_provider_auth_changes()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var engine = ChatProviderFixture.Create(h.NewFolder("stand-in"), "Unused");
            CommitSheet sheet = null!;
            Avalonia.Controls.Window window = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.SetBinaryPath(engine, ProviderKind.Claude);
                model.Settings.TextGeneration = TextGenerationChoice.Claude;
                var runtime = Runtime(model, model.AddProject(new Project("P", directory).Url));
                sheet = new CommitSheet(model, runtime, directory, new GitStatusModel(), () => { }) { IsVisible = false };
                sheet.Message = "Typed"; // :1608 loading must not auto-generate.
                window = new Avalonia.Controls.Window { Content = sheet }; window.Show();
                Assert.True(sheet.WriteForMeButton.IsEnabled);
                var status = new Marrow.Services.Providers.ProviderStatus { ProviderStatusAuth = new Marrow.Services.Providers.ProviderStatus.Auth.SignedOut() };
                typeof(Marrow.Services.Providers.ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers,
                    new ValueMap<ProviderKind, Marrow.Services.Providers.ProviderStatus>(new Dictionary<ProviderKind, Marrow.Services.Providers.ProviderStatus> { [ProviderKind.Claude] = status }));
            });
            h.Main.RunUntilIdle();
            Assert.False(sheet.WriteForMeButton.IsEnabled);
            Assert.Equal("Turn on text generation in Settings > Source control", ToolTip.GetTip(sheet.WriteForMeButton));
            h.Main.OnMain(() => window.Close());
            h.Main.RunUntilIdle();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-CHAT-178")] // App/Views/Chat/ChatView.swift:1231-1236
    [Trait("Req", "UI-CHAT-078")] // App/Views/Chat/ChatView.swift:1231-1236
    [Trait("Req", "UI-GIT-043")] // App/Views/Chat/ChatView.swift:1381-1385
    public void The_production_prompt_uses_the_common_seam_and_cancel_never_performs_the_text_action(bool cancels)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var recorder = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(recorder);
            Task shown = null!;
            string? performed = null;
            h.Main.OnMain(() => shown = new Marrow.App.Port.WindowsPromptPresenter().Show("Rename thread", "Title", "Before", [
                new Marrow.App.Port.PromptButton("Rename", false, value => performed = value),
                new Marrow.App.Port.PromptButton("Cancel", true, _ => performed = "must not run"),
            ]));
            var popup = Assert.Single(recorder.Calls).Popup;
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(popup.Child!), text => text.Text == "Rename thread");
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popup.Child!));
            Assert.Equal("Title", field.PlaceholderText);
            Assert.Equal("Before", field.Text);
            Assert.False(shown.IsCompleted);
            h.Main.OnMain(() =>
            {
                field.Text = "Typed";
                var button = ChatGitFixtures.Button(popup.Child!, cancels ? "Cancel" : "Rename");
                Assert.Equal(cancels, button.IsCancel);
                ChatGitFixtures.Click(button);
            });
            Assert.True(shown.IsCompletedSuccessfully);
            Assert.False(recorder.Calls.Last().Presented);
            Assert.Equal(cancels ? null : "Typed", performed);
        });
    }

    // STOP App.Views.Chat.ChatView #5 (rewritten 2026-10-03)
    // The native-control dependency it named is resolved under OD-0042/OD-0045: `.textFieldStyle(.roundedBorder)` draws the
    // Apple-kit bezel of ChatDialogControls.TextEditor(style: "RoundedBorder") and ProgressView().controlSize(.small) draws
    // SystemSpinner (PortControlThemes). Proofs: ChatViewParityDetailTests.Name_and_Command_draw_the_rounded_border_bezel...
    // (UI-TERM-024, UI-TERM-026) and Appearing_with_an_empty_message_writes_a_draft_showing_the_small_spinner... (UI-GIT-035).
    // Kind: capture UNRESOLVED (no reference frame). Swift: App/Views/Chat/ChatView.swift:1652-1730.
    // Records: PR-TERM-074, PR-TERM-076.
    // Neither reference video shows the Project scripts sheet: 2-chat-use.mov opens only the scripts menu (C 131.3-132.2,
    // "Add a script…"), never the sheet, and 1-settings-tour.mov never reaches a chat. Every other clause of both records is
    // proven (ChatViewPart2Tests, ChatViewPart2ReviewTests, ChatViewPart4Tests, ChatViewParityTests, ChatViewParityDetailTests);
    // their capture comparison stays UNRESOLVED until a frame of the sheet exists.

    [Fact]
    [Trait("Req", "UI-TERM-023")] // App/Views/Chat/ChatView.swift:1667-1699
    [Trait("Req", "UI-TERM-028")] // App/Views/Chat/ChatView.swift:1701-1707
    public void Adding_a_script_keeps_existing_card_identity_and_the_source_plus_symbol()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url) with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = new ScriptsEditor(model, project.Id, () => { });
                var original = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Name");
                var add = ChatGitFixtures.Button(editor, "Add script");
                ChatGitFixtures.Click(add);
                Assert.Same(original, LogicalQuery.SelfAndDescendants<TextBox>(editor).First(t => t.PlaceholderText == "Name")); // R-VIEW-1 keyed ForEach.
                var symbol = typeof(Marrow.App.Port.ChatDialogControls).GetMethod("GetSymbol");
                Assert.NotNull(symbol);
                Assert.Equal("plus", symbol.Invoke(null, [add])); // :1705 Label("Add script", systemImage: "plus").
            });
        });
    }

    [Theory]
    [InlineData(0, "Generated commit")]
    [InlineData(1, "Keep my message")]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1575-1592,1612-1631
    [Trait("Req", "UI-GIT-034")] // App/Views/Chat/ChatView.swift:1562-1567
    public void Generation_follows_engine_settings_shows_busy_state_and_preserves_text_on_a_nil_reply(int exit, string expected)
    {
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
    // A part of UI-USAGE-041 (the pinned start, the exact commit, the stepped live width); it neither double-clicks nor drags below the 94 pt floor,
    // so the record is carried by ChatViewParityRound2Tests.The_usage_panels_grips_sit_on_its_free_edges... (review round 2).
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:527,554-590,595-598
    public void Grip_resize_pins_the_start_commit_is_exact_and_the_live_width_is_stepped(PanelDockCorner corner, int across, int down)
    {
        ChatHeadlessFixtures.Run(headless, () =>
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
        ChatHeadlessFixtures.Run(headless, () =>
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
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1123
    public void The_glass_group_retains_the_source_spacing_for_the_deferred_compositor()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            var group = new GlassEffectContainer(2, () => new Border());
            var spacing = typeof(GlassEffectContainer).GetProperty("Spacing");
            Assert.NotNull(spacing);
            Assert.Equal(2d, spacing.GetValue(group));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-172")] // App/Views/Chat/ChatView.swift:1177-1180; DEVIATION OD-0044: nothing to clear at the leading end
    public void The_leading_inset_stays_put_when_the_sidebar_takes_the_caption_buttons()
    {
        ChatHeadlessFixtures.Run(headless, () =>
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
            var margin = Assert.Single(LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row)).Margin;
            Assert.Equal(Chrome.ChromeHorizontalPadding, margin.Left); // OD-0044: no glide, the caption buttons are at the right
            Assert.Equal(156, margin.Right);
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
        ChatHeadlessFixtures.Run(headless, () =>
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

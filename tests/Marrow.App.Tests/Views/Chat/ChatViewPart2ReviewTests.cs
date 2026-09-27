using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewPart2ReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1214,1238
    public void The_title_has_the_source_font_single_line_tail_truncation_and_six_point_hit_padding()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var title = new ChatChromeTitle(model, model.Runtime(thread.Id), "Title");
                var text = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(title));
                Assert.Equal(15, text.FontSize); // ED-059, B1:AppZoom table title3 :50.
                Assert.Equal(Avalonia.Media.FontWeight.SemiBold, text.FontWeight);
                Assert.Equal(1, text.MaxLines);
                Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, text.TextTrimming);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(title));
                Assert.Equal(new Thickness(0, 6), Assert.IsType<Border>(button.Content).Padding);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, title.HorizontalAlignment);
                Assert.Equal("Thread title", Avalonia.Automation.AutomationProperties.GetName(button));
            });
        });
    }

    // STOP App.Views.Chat.ChatView #4
    // Kind: conflict
    // Swift: DroppyCode/App/Views/Chat/ChatView.swift:1726-1729 (1.7.0).
    // Records: PR-TERM-078, UI-TERM-026.
    // Packet prose says tabs survive .whitespaces; the pinned source calls that Foundation set,
    // whose already verified FoundationText.Trim includes tabs but excludes line feeds.
    // Keep the inherited tab-incomplete assertions and exact Swift operation; do not re-expect them.
    // Needed: orchestrator correction to packet prose. No invented trim rule is implemented.

    [Fact]
    [Trait("Req", "PR-TERM-076")] // App/Views/Chat/ChatView.swift:1672-1693
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1712-1714
    public void Editing_the_command_preserves_the_same_cards_edited_name_and_worktree_flag()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url)
                    with { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = new ScriptsEditor(model, project.Id, () => { });
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Name").Text = "Renamed";
                LogicalQuery.SelfAndDescendants<CheckBox>(editor).Single().IsChecked = true;
                LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Command").Text = "make test";
                editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = Assert.Single(model.Project(project.Id)!.Scripts);
                Assert.Equal("Renamed", saved.Name);
                Assert.Equal("make test", saved.Command);
                Assert.True(saved.RunOnWorktreeCreate);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-074")] // App/Views/Chat/ChatView.swift:1660,1721-1722
    public void The_scripts_sheet_pads_its_contents_by_twenty_inside_its_fixed_frame()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var editor = new ScriptsEditor(model, Guid.NewGuid(), () => { });
                var padding = Assert.IsType<Border>(editor.Child);
                Assert.Equal(new Thickness(20), padding.Padding);
                Assert.Equal(14, Assert.IsType<StackPanel>(padding.Child).Spacing);
                Assert.Equal(580, editor.Width);
                Assert.Equal(460, editor.Height);
            });
        });
    }

    [Theory]
    [InlineData(PanelDockCorner.TopLeading, 20, 30)]
    [InlineData(PanelDockCorner.TopTrailing, -20, 30)]
    [InlineData(PanelDockCorner.BottomLeading, 20, -30)]
    [InlineData(PanelDockCorner.BottomTrailing, -20, -30)]
    [Trait("Req", "PR-USAGE-073")] // App/Views/Chat/ChatView.swift:554-590
    public void Usage_grips_keep_width_shared_and_height_local_until_release(PanelDockCorner corner, double across, double down)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var runtime = model.Runtime(thread.Id);
                model.Settings.ShowsUsagePanel = true;
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 100, 200);
                var resize = new PanelResizeState();
                var original = model.Settings.PanelSize;
                var height = model.Settings.UsagePanelHeight;
                var grip = ChatViewReviewFixtures.Invoke<PanelResize>("UsageResizer", model, resize, corner, scene, new Size(scene.Layout.PanelWidth, 200), 94d);
                grip.OnResize(new Size(20, 30), PanelResizeAxes.Both);
                Assert.Equal(original, model.Settings.PanelSize);
                Assert.Equal(height, model.Settings.UsagePanelHeight);
                Assert.Equal(200 + down, resize.LiveUsageHeight);
                Assert.Equal(scene.Layout.Fitted(new Size(scene.Layout.PanelWidth + across, scene.Layout.PanelHeight)), resize.LiveSize);
                grip.OnResizeEnd();
                Assert.Equal(200 + down, model.Settings.UsagePanelHeight);
                Assert.NotNull(model.Settings.PanelSize);
                Assert.Null(resize.LiveSize);
                Assert.Null(resize.LiveUsageHeight);
                Assert.False(resize.IsActive);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-074")] // App/Views/Chat/ChatView.swift:574-577
    public void Resetting_a_usage_grip_clears_only_its_stored_height()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var runtime = model.Runtime(thread.Id);
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 100, 200);
                model.Settings.PanelSize = new Size(350, 400);
                model.Settings.UsagePanelHeight = 300;
                var grip = ChatViewReviewFixtures.Invoke<PanelResize>("UsageResizer", model, new PanelResizeState(), PanelDockCorner.BottomLeading, scene, new Size(350, 300), 94d);
                grip.OnReset();
                Assert.Null(model.Settings.UsagePanelHeight);
                Assert.Equal(new Size(350, 400), model.Settings.PanelSize);
            });
        });
    }

    [Theory]
    [InlineData(PanelDockCorner.TopLeading)]
    [InlineData(PanelDockCorner.BottomLeading)]
    [Trait("Req", "PR-USAGE-071")] // App/Views/Chat/ChatView.swift:496-503,707-710
    public void Usage_origin_preserves_the_docked_edge_when_its_height_differs(PanelDockCorner corner)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var runtime = model.Runtime(thread.Id);
                model.Settings.ShowsUsagePanel = true;
                model.Settings.UsagePanelDock = corner;
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 100, 200);
                var slot = scene.Slots[new FloatingPanelID.Usage()];
                var rest = ChatViewReviewFixtures.Invoke<Point>("Rest", slot, scene);
                var origin = ChatViewReviewFixtures.Invoke<Point>("UsageOrigin", slot, scene);
                Assert.Equal(rest.X, origin.X);
                Assert.Equal(corner.IsTop ? rest.Y : rest.Y + scene.Layout.PanelHeight - 200, origin.Y);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-072")] // App/Views/Chat/ChatView.swift:960-981
    public void Auto_popping_uses_the_fixed_number_of_free_slots_before_removing_candidates()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var runtime = model.Runtime(thread.Id);
                model.Settings.HydraAutoPopsHeads = true;
                model.Settings.ShowsUsagePanel = false;
                var heads = Enumerable.Range(0, 4).Select(_ => new ChatThread(project.Id, ProviderKind.Claude, null, null, RuntimeMode.Supervised)).ToArray();
                var members = new PanelMembers(runtime, model) with { Heads = new ValueList<ChatThread>(heads), Subagent = null, IsDocked = false, Usage = null };
                var scene = new PanelScene(members, runtime, model, new Size(2000, 2200), 100, null);
                Assert.Equal(3, scene.AutoPopped.Count); // :961 caps natural slots at three.
                Assert.All(scene.AutoPopped, head => Assert.Equal(runtime.HydraPanelDock.AcrossTheColumn, scene.Slots[new FloatingPanelID.Auto(head.Id)].Corner)); // :973 opposite side first.
            });
        });
    }
}

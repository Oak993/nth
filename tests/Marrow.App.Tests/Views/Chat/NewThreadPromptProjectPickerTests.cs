using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// The empty thread's project picker, App/Views/Chat/NewThreadPrompt.swift (B3 52bc792) :57-80, and the click path of
// PopoverItem (Popovers.swift:257-273). UI-CHAT-046 and UI-CHAT-047. The records cite NewThreadPrompt.swift:578-601 and
// :588-591, which is past the end of the 81-line file in the pinned reference; the picker is :57-72 and the move :74-80.
// The folder dialog goes through the OpenPanel seam (RecordingOpenPanelRunner): a real dialog is never opened.
// Each test steps through separate OnMain calls (a nested one throws): set up and click the project name; read the
// presented menu and click a row; read what the click did. NewThreadPickerRig carries the popover seam.
[Collection("Headless")]
public sealed class NewThreadPromptProjectPickerTests(HeadlessSession headless)
{
    private const string AddProjectTitle = "Add project…";

    private static string TitleOf(PopoverItem row) =>
        AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(row).First()) ?? "";

    private static double CheckOpacity(PopoverItem row) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(image => image.SymbolName == "checkmark").Opacity;

    private static SwiftURL FolderURL(string folder) => SwiftURL.Create(new Uri(folder).AbsoluteUri)!;

    [Fact]
    [Trait("Req", "UI-CHAT-046")] // App/Views/Chat/NewThreadPrompt.swift:57-72
    public void The_picker_reads_Projects_then_one_row_per_project_then_a_divider_then_Add_project()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dirs = new[] { harness.NewFolder("alpha"), harness.NewFolder("beta"), harness.NewFolder("gamma") };
            using var rig = new NewThreadPickerRig();
            string[] names = [];
            harness.Main.OnMain(() =>
            {
                var projects = dirs.Select(dir => model.AddProject(new Project("x", dir).Url)).ToArray();
                names = projects.Select(project => project.Name).ToArray();
                var thread = model.NewThread(projects[1])!;
                rig.Show(model, thread.Id, projects[1].Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                var rows = rig.Rows;
                Assert.Equal(6, rows.Count);
                var header = Assert.IsType<PopoverSectionHeader>(rows[0]); // :60
                Assert.Equal(["Projects"], LogicalQuery.SelfAndDescendants<TextBlock>(header).Select(block => block.Text ?? "").ToArray());
                // :61-65 one row per project, in the model's order.
                Assert.Equal(names, rows.Skip(1).Take(3).Select(row => TitleOf(Assert.IsType<PopoverItem>(row))).ToArray());
                Assert.IsType<PopoverDivider>(rows[4]); // :66
                Assert.Equal(AddProjectTitle, TitleOf(Assert.IsType<PopoverItem>(rows[5]))); // :67
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-046")] // App/Views/Chat/NewThreadPrompt.swift:61-64 isChecked: project.id == current
    public void Only_the_threads_current_project_is_checked()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var betaDir = harness.NewFolder("beta");
            using var rig = new NewThreadPickerRig();
            string alphaName = "", betaName = "";
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                var beta = model.AddProject(new Project("x", betaDir).Url);
                alphaName = alpha.Name;
                betaName = beta.Name;
                var thread = model.NewThread(beta)!;
                rig.Show(model, thread.Id, beta.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(0, CheckOpacity(rig.Row(alphaName)));
                Assert.Equal(1, CheckOpacity(rig.Row(betaName)));
                // :67 the Add project row passes no isChecked, so it has no checkmark column at all.
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(rig.Row(AddProjectTitle)), image => image.SymbolName == "checkmark");
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-046")] // App/Views/Chat/NewThreadPrompt.swift:62 leading: ProjectIconMark(icon: project.icon, size: 12)
    public void Each_project_row_leads_with_its_icon_mark_at_size_12()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dirs = new[] { harness.NewFolder("rocket"), harness.NewFolder("star"), harness.NewFolder("plain") };
            using var rig = new NewThreadPickerRig();
            string[] names = [];
            harness.Main.OnMain(() =>
            {
                var projects = dirs.Select(dir => model.AddProject(new Project("x", dir).Url)).ToArray();
                names = projects.Select(project => project.Name).ToArray();
                model.UpdateProject(projects[0].Id, project => project with { Icon = new ProjectIcon.Emoji("\U0001F680") });
                model.UpdateProject(projects[1].Id, project => project with { Icon = new ProjectIcon.Symbol("star.fill") });
                var thread = model.NewThread(projects[2])!;
                rig.Show(model, thread.Id, projects[2].Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                var emoji = LogicalQuery.SelfAndDescendants<ProjectIconMark>(rig.Row(names[0])).Single();
                var emojiText = LogicalQuery.SelfAndDescendants<TextBlock>(emoji).Single();
                Assert.Equal("\U0001F680", emojiText.Text);
                Assert.Equal(12d, emojiText.FontSize);

                var star = LogicalQuery.SelfAndDescendants<ProjectIconMark>(rig.Row(names[1])).Single();
                var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(star).Single();
                Assert.Equal("star.fill", symbol.SymbolName);
                Assert.Equal(12d, SwiftImage.GetFont(symbol)!.PointSize);

                // No icon picked: the folder mark, still at 12.
                var plain = LogicalQuery.SelfAndDescendants<ProjectIconMark>(rig.Row(names[2])).Single();
                var folder = LogicalQuery.SelfAndDescendants<SwiftImage>(plain).Single();
                Assert.Equal("folder.fill", folder.SymbolName);
                Assert.Equal(12d, SwiftImage.GetFont(folder)!.PointSize);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-046")] // App/Views/Chat/NewThreadPrompt.swift:62-63, :74-80 move(to:)
    public void Clicking_another_project_closes_the_popover_and_moves_the_empty_thread_there()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var betaDir = harness.NewFolder("beta");
            using var rig = new NewThreadPickerRig();
            Project beta = null!;
            Guid threadID = Guid.Empty;
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                beta = model.AddProject(new Project("x", betaDir).Url);
                var thread = model.NewThread(alpha)!;
                threadID = thread.Id;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                Assert.True(rig.IsPresented);
                rig.ClickRow(beta.Name);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(beta.Id, model.Thread(threadID)!.ProjectID); // :77-78
                Assert.False(rig.IsPresented); // :76 isChoosingProject = false
                Assert.Single(model.Threads); // moved, not copied
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-046")] // App/Views/Chat/NewThreadPrompt.swift:75 guard thread.projectID != project.id; Popovers.swift:271 dismiss()
    public void Clicking_the_current_project_changes_nothing_and_the_row_still_dismisses_the_popover()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var betaDir = harness.NewFolder("beta");
            using var rig = new NewThreadPickerRig();
            Project alpha = null!;
            ChatThread before = null!;
            Guid? revealBefore = null;
            harness.Main.OnMain(() =>
            {
                alpha = model.AddProject(new Project("x", alphaDir).Url);
                model.AddProject(new Project("x", betaDir).Url);
                var thread = model.NewThread(alpha)!;
                before = model.Thread(thread.Id)!;
                revealBefore = model.SidebarRevealThreadID;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                Assert.True(rig.IsPresented);
                rig.ClickRow(alpha.Name);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(before, model.Thread(before.Id));
                Assert.Single(model.Threads);
                Assert.Equal(revealBefore, model.SidebarRevealThreadID); // moveThread (:77-78) did not run
                // A SwiftUI popover answers the row's dismiss() whatever the action then does (Popovers.swift:176, :271).
                Assert.False(rig.IsPresented);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-047")] // App/Views/Chat/NewThreadPrompt.swift:67-70
    public void The_last_row_is_Add_project_with_a_real_ellipsis_and_the_plus_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            using var rig = new NewThreadPickerRig();
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                var thread = model.NewThread(alpha)!;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() =>
            {
                var last = Assert.IsType<PopoverItem>(rig.Rows[^1]);
                var title = TitleOf(last);
                Assert.Equal("Add project", title[..^1]);
                Assert.Equal('…', title[^1]); // the single character U+2026, not three full stops
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(last).Single(image => image.SymbolName is not null && image.SymbolName != "checkmark");
                Assert.Equal("plus", glyph.SymbolName);
                Assert.IsType<PopoverDivider>(rig.Rows[^2]); // directly after the divider
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-047")] // App/Views/Chat/NewThreadPrompt.swift:67-70; App/AppModel.swift:535-547
    public void Choosing_a_folder_adds_the_project_and_moves_the_thread_into_it()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var chosenDir = harness.NewFolder("chosen");
            var runner = new RecordingOpenPanelRunner([FolderURL(chosenDir)]);
            using var panel = OpenPanel.Runner.Override(runner);
            using var rig = new NewThreadPickerRig();
            Guid threadID = Guid.Empty;
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                var thread = model.NewThread(alpha)!;
                threadID = thread.Id;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() => rig.ClickRow(AddProjectTitle));
            harness.Main.OnMain(() =>
            {
                Assert.Equal((true, false, true, "Add project", "Choose a folder for your agents to work in."), Assert.Single(runner.Shown));
                Assert.Equal(2, model.Projects.Count);
                var added = model.Projects[1];
                Assert.True(SwiftPath.SamePath(chosenDir, added.Path));
                Assert.Equal(added.Id, model.Thread(threadID)!.ProjectID); // :69 move(to: added)
                Assert.Single(model.Threads); // no second empty thread is left behind
                Assert.False(rig.IsPresented);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-047")] // App/Views/Chat/NewThreadPrompt.swift:69 guard let added = model.addProjectsFromPanel() else { return }
    public void Cancelling_the_folder_dialog_changes_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var runner = new RecordingOpenPanelRunner(null);
            using var panel = OpenPanel.Runner.Override(runner);
            using var rig = new NewThreadPickerRig();
            ChatThread before = null!;
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                var thread = model.NewThread(alpha)!;
                before = model.Thread(thread.Id)!;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() => rig.ClickRow(AddProjectTitle));
            harness.Main.OnMain(() =>
            {
                Assert.Single(runner.Shown);
                Assert.Single(model.Projects);
                Assert.Equal(before, model.Thread(before.Id));
                Assert.Single(model.Threads);
                // The row's dismiss() ran before the action (Popovers.swift:271-273), so the popover is gone either way.
                Assert.False(rig.IsPresented);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-047")] // App/AppModel.swift:543-545 the last folder chosen is the one returned and moved into
    public void Choosing_several_folders_adds_them_all_and_moves_the_thread_into_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var alphaDir = harness.NewFolder("alpha");
            var firstDir = harness.NewFolder("first");
            var lastDir = harness.NewFolder("last");
            using var panel = OpenPanel.Runner.Override(new RecordingOpenPanelRunner([FolderURL(firstDir), FolderURL(lastDir)]));
            using var rig = new NewThreadPickerRig();
            Guid threadID = Guid.Empty;
            harness.Main.OnMain(() =>
            {
                var alpha = model.AddProject(new Project("x", alphaDir).Url);
                var thread = model.NewThread(alpha)!;
                threadID = thread.Id;
                rig.Show(model, thread.Id, alpha.Name);
                rig.ClickName();
            });
            harness.Main.OnMain(() => rig.ClickRow(AddProjectTitle));
            harness.Main.OnMain(() =>
            {
                Assert.Equal(3, model.Projects.Count);
                Assert.True(SwiftPath.SamePath(lastDir, model.Projects[2].Path));
                Assert.Equal(model.Projects[2].Id, model.Thread(threadID)!.ProjectID);
            });
        });
    }
}

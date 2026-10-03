using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Expectations for DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), part 4 of 4 (the last part of this unit):
// UI-TERM-018-030, UI-USAGE-041.
[Collection("Headless")]
public sealed class ChatViewPart4Tests(HeadlessSession headless)
{
    private static T Show<T>(T control) where T : Control
    {
        // All editor assertions concern source properties/structure and actions; keep the appearance lifecycle,
        // without unrelated Skia glyph-cache layout failures after a prior editor's template is torn down.
        if (control is ScriptsEditor) control.IsVisible = false;
        var window = new AvaloniaWindow { Content = control };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static ChatThread MakeThread(AppModelHarness harness, AppModel model) =>
        model.NewThread(model.AddProject(new Project("P", harness.NewFolder("proj")).Url))!;

    // ---- Project scripts sheet geometry: UI-TERM-020, UI-TERM-023 (the parts not already covered by part 2's ------
    // ---- PR-TERM-074 (title/explanation/580x460) and PR-TERM-076 (the card's own field layout) tests) ------------

    [Fact]
    [Trait("Req", "UI-TERM-023")] // App/Views/Chat/ChatView.swift:1666-1667,1669,1700
    public void Cards_sit_10_points_apart_inside_a_scroll_area_at_least_220_points_tall_each_padded_14()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build"), new ProjectScript("Test", "make test")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(editor).Single();
                Assert.Equal(220, scroll.MinHeight); // :1700 .frame(minHeight: 220)

                var rows = LogicalQuery.SelfAndDescendants<StackPanel>(editor).Single(s => s.Spacing == 10); // :1667
                Assert.Equal(2, rows.Children.Count);

                // :1695 .padding(14) on each card's own body.
                var cardBodies = LogicalQuery.SelfAndDescendants<StackPanel>(editor).Where(s => s.Margin == new Thickness(14)).ToList();
                Assert.Equal(2, cardBodies.Count);
                Assert.All(cardBodies, body => Assert.Equal(8, body.Spacing)); // :1670 VStack(alignment: .leading, spacing: 8)
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-020")] // App/Views/Chat/ChatView.swift:1721-1722
    public void The_sheet_is_a_fixed_size_that_does_not_grow_with_the_number_of_scripts()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var many = Enumerable.Range(1, 10).Select(i => new ProjectScript($"Script {i}", $"cmd{i}")).ToArray();
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url) with { Scripts = new ValueList<ProjectScript>(many) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                Assert.Equal(580, editor.Width); // fixed, regardless of the 10 cards above
                Assert.Equal(460, editor.Height);
            });
        });
    }

    // ---- Name field: UI-TERM-024 (the part not already covered by part 2's PR-TERM-076 placeholder/text test) -----

    [Fact]
    [Trait("Req", "UI-TERM-024")] // App/Views/Chat/ChatView.swift:1672-1674
    public void A_blank_name_is_allowed_and_does_not_block_saving()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("", "make build")]) }; // blank name, real command
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                var nameField = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Name");
                Assert.Equal("", nameField.Text);
                Assert.True(editor.SaveButton.IsEnabled); // only the command is validated (PR-TERM-078)

                LogicalQuery.SelfAndDescendants<Button>(editor).Single(b => Equals(b.Content, "Save")).RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("", model.Project(project.Id)!.Scripts[0].Name); // saved as-is, empty
            });
        });
    }

    // ---- ScriptsMenu rows: UI-TERM-018 (structure only; the click-runs-it interaction spawns a real terminal via ---
    // ---- Terminals.Run, an already-DONE service outside this unit, so it is not driven end to end here, matching --
    // ---- the restraint already established for CommitSheet/GitActionsMenu's own real-process actions) -------------

    [Fact]
    [Trait("Req", "UI-TERM-018")] // App/Views/Chat/ChatView.swift:1445-1448
    public void Every_script_appears_as_its_own_row_in_the_projects_stored_order_with_no_disabled_state()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Zed", "cmd"), new ProjectScript("Alpha", "cmd")]) };
                model.UpdateProject(project.Id, _ => project);
                var thread = model.NewThread(project)!;

                var menu = Show(ScriptsMenu.BuildMenu(model, model.Project(project.Id)!, model.Runtime(thread.Id)));
                var items = LogicalQuery.SelfAndDescendants<PopoverItem>(menu)
                    .Where(i => LogicalQuery.SelfAndDescendants<TextBlock>(i).Any(t => t.Text is "Zed" or "Alpha"))
                    .ToList();
                Assert.Equal(2, items.Count);
                // :1445 ForEach(project.scripts): the project's own stored order, not alphabetised.
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(items[0]), t => t.Text == "Zed");
                Assert.All(items, item => Assert.True(LogicalQuery.SelfAndDescendants<Button>(item).Single().IsEnabled)); // no disabled state
            });
        });
    }

    // UI-TERM-019 is covered by part 1's With_no_scripts_the_header_and_divider_are_omitted... and
    // The_menu_lists_every_script_under_a_Scripts_header... tests, and part 2's The_last_row_switches_between_...
    // test (the click-presents-the-editor half).

    // UI-TERM-021, UI-TERM-022 are covered by part 2's The_editor_is_a_fixed_580_by_460_sheet... test (title2
    // semibold heading, callout explanation text, the exact OD-0012 F1 string).

    // UI-TERM-025 is covered by part 2's The_trash_button_removes_the_card_immediately... test.

    // UI-TERM-026 is covered by part 2's A_blank_command_is_outlined_in_danger... test (the overlay, the two
    // states, and the tab/whitespace trim rule, with the packet-vs-verified-behavior note already flagged there).

    // UI-TERM-027 is covered by part 2's Each_card_holds_a_Name_field... test (the checkbox's exact label and its
    // checked state read from RunOnWorktreeCreate).

    // UI-TERM-028 is covered by part 2's Add_script_appends_a_New_script_row... test.

    // UI-TERM-029, UI-TERM-030 are covered by part 2's Cancel_discards_every_change_while_Save_writes...,
    // Save_is_unreachable_while_any_script_is_incomplete and A_blank_command_is_outlined_in_danger... (the two
    // exact tooltip strings) tests.

    // ---- UI-USAGE-041 (updated 2026-10-03): proven by ChatViewPart4ReviewTests.Grip_resize_pins_the_start_commit_is_exact... ----
    // (every corner), ChatViewReviewFixTests.A_mounted_resize_grip_survives_multiple_real_pointer_moves... (a real drag,
    // one write on release) and ChatViewParityChromeTests.Resetting_the_usage_height_removes_the_key... (the reset).
}

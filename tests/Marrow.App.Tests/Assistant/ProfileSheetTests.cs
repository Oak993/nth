using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.2; the approved canvas "Create an Astur"; ED-NEW-dextra-as16-15): the profile sheet in a headless window. A sentence and six
// templates start the card; name, six looks (one picked, none that another Astur has, the state shown by a tick and in words), area, duties to tick, connections,
// budget; the problems under it as the owner types; Create only when there are none; Escape and Cancel close it and put the focus back on the opener; and it wraps
// at 375 px, with long text and right to left, without running past the window.
[Collection("Headless")]
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-15")]
public sealed class ProfileSheetTests(HeadlessSession headless)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static List<AssistantProfile> Team() =>
    [
        new("dextra", "Dextra") { Created = Now },
        new("a", "Channel A") { ParentId = "dextra", Shape = AsturShape.Squircle, Area = "Looks after something.", Created = Now },
    ];

    private sealed class Host
    {
        public List<AssistantProfile> Team { get; } = ProfileSheetTests.Team();

        public List<AsturDraft> Created { get; } = [];

        public int Cancelled { get; set; }

        public IReadOnlyList<string> Answer { get; set; } = [];

        public ProfileSheetModel Sheet(ProfileSheetMode mode = ProfileSheetMode.Add, AsturDraft? draft = null, string? editing = null) =>
            new(mode, () => Team, made =>
            {
                Created.Add(made);
                return Task.FromResult(Answer);
            }, () => Cancelled++, draft, editing, "Keeps 30% on Claude 20x (your standing rule).");
    }

    private static Button Opener() => new() { Content = "Add an Astur", Focusable = true };

    private static (Panel Root, Button Opener) Mount(ProfileSheet sheet, Button opener) => (new StackPanel { Children = { opener, sheet } }, opener);

    [Fact]
    public void An_Add_sheet_has_a_sentence_a_Draft_it_button_and_the_six_templates_all_named_and_nothing_else_until_a_card_exists()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var sheet = new ProfileSheet(host.Sheet());
            using var stage = new ReserveViewStage(sheet);

            Assert.Equal("Describe the assistant", AutomationProperties.GetName(sheet.SentenceBox!));
            Assert.Equal("Draft the card from what you wrote", AutomationProperties.GetName(sheet.DraftButton!));
            Assert.Equal(["Start from the Channel template", "Start from the Store template", "Start from the Project maintainer template",
                "Start from the Research desk template", "Start from the Inbox (drafts only) template", "Start from the Finance watcher (read-only) template"],
                sheet.TemplateButtons.Select(b => AutomationProperties.GetName(b)));
            Assert.Null(sheet.NameBox);
            Assert.Null(sheet.CreateButton);                                                          // there is no Create without a card
            Assert.NotNull(sheet.CancelButton);
            Assert.Contains("No card yet. Draft one from your words, or pick a template.", Texts(sheet));
            Assert.Contains("Add an Astur", Texts(sheet));
        });
    }

    [Fact]
    public void A_template_makes_the_card_with_every_field_named_and_the_picked_look_shown_by_a_tick_and_in_words()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var model = host.Sheet();
            var sheet = new ProfileSheet(model);
            using var stage = new ReserveViewStage(sheet);

            Click(sheet.TemplateButtons[0]);
            stage.Layout();

            Assert.Equal("Channel", sheet.NameBox!.Text);
            Assert.Equal("Name", AutomationProperties.GetName(sheet.NameBox));
            Assert.Equal("What it looks after", AutomationProperties.GetName(sheet.AreaBox!));
            Assert.Equal("A channel: scripts, voiceovers, edits, uploads and its numbers.", sheet.AreaBox!.Text);
            Assert.Equal("How it writes", AutomationProperties.GetName(sheet.StyleBox!));
            Assert.Equal(["Squircle, taken by Channel A", "Hexagon", "Diamond", "Rounded triangle", "Clover", "Star"], sheet.ShapeButtons.Select(b => AutomationProperties.GetName(b)));
            Assert.Equal([false, true, true, true, true, true], sheet.ShapeButtons.Select(b => b.IsEnabled));
            Assert.Equal([false, true, false, false, false, false], sheet.ShapeButtons.Select(b => b.IsChecked == true));
            Assert.Contains("Hexagon. Squircle belongs to Channel A.", Texts(sheet));                       // the picked look and the taken ones are also said, not only drawn
            Assert.Contains("✓", Texts(sheet));
            Assert.All(sheet.ShapeButtons, b => Assert.Equal((44.0, 44.0), (b.Bounds.Width, b.Bounds.Height)));
            Assert.Equal(3, sheet.DutyBoxes.Count);
            Assert.All(sheet.DutyBoxes, box => Assert.True(box.IsChecked));
            Assert.Equal("Watch the numbers, weekdays 09:00", AutomationProperties.GetName(sheet.DutyBoxes[0]));
            Assert.Contains("Site: YouTube Studio · read · you sign in once", sheet.ConnectionLines);
            Assert.Contains("Sign-in: Voice site · sign-in asks you", sheet.ConnectionLines);
            Assert.Contains("Up to 15% of any account a day.", Texts(sheet));
            Assert.Contains("Keeps 30% on Claude 20x (your standing rule).", Texts(sheet));
            Assert.Contains("Own browser profile and folder. Logins never shared.", Texts(sheet));
            Assert.Contains("Asks you: sign-ins, uploads, posting, anything sent.", Texts(sheet));
            Assert.Equal("Create the Astur", AutomationProperties.GetName(sheet.CreateButton!));
            Assert.True(sheet.CreateButton!.IsEnabled);
            Assert.Equal("", sheet.ProblemsText);
            Assert.Equal("The Channel template.", sheet.StatusText);
        });
    }

    [Fact]
    public void Tab_goes_through_the_sheet_in_the_order_it_reads_and_every_focusable_control_has_a_name()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var sheet = new ProfileSheet(host.Sheet());
            using var stage = new ReserveViewStage(sheet);
            Click(sheet.TemplateButtons[3]);
            stage.Layout();

            var order = Nodes<Control>(sheet).Where(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c is Button or TextBox or CheckBox).ToList();
            var expected = new List<Control> { sheet.SentenceBox!, sheet.DraftButton! };
            expected.AddRange(sheet.TemplateButtons);
            expected.AddRange([sheet.NameBox!]);
            expected.AddRange(sheet.ShapeButtons.Where(b => b.IsEnabled));
            expected.AddRange([sheet.AreaBox!, sheet.StyleBox!]);
            expected.AddRange(sheet.DutyBoxes);
            expected.AddRange([sheet.PercentBox!, sheet.DollarsBox!, sheet.CreateButton!, sheet.CancelButton!]);
            // The text boxes are the plain boxes inside their fields; compare the controls the owner tabs to, in the tree's order.
            var tabbed = order.Where(c => expected.Contains(c)).ToList();
            Assert.Equal(expected.Count, tabbed.Count);
            Assert.Equal(expected, tabbed);
            Assert.All(expected, c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)), c.GetType().Name));
        });
    }

    [Fact]
    public void Typing_lists_what_is_wrong_under_the_card_and_Create_waits_until_nothing_is_without_rebuilding_the_fields()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var sheet = new ProfileSheet(host.Sheet());
            using var stage = new ReserveViewStage(sheet);
            Click(sheet.TemplateButtons[0]);
            stage.Layout();
            var name = sheet.NameBox!;
            name.Focus();

            name.Text = "Dextra";
            stage.Layout();
            Assert.Same(name, sheet.NameBox);                                                               // not rebuilt: the caret and focus stay
            Assert.True(name.IsKeyboardFocusWithin);
            Assert.Equal("• \"Dextra\" is a word nth listens for; pick another name.", sheet.ProblemsText);
            Assert.False(sheet.CreateButton!.IsEnabled);

            name.Text = "Channel B";
            stage.Layout();
            Assert.Equal("", sheet.ProblemsText);
            Assert.True(sheet.CreateButton.IsEnabled);

            sheet.AreaBox!.Text = "";
            Assert.Equal("• Say what it looks after.", sheet.ProblemsText);
            Assert.False(sheet.CreateButton.IsEnabled);
        });
    }

    [Fact]
    public void A_look_that_another_Astur_has_cannot_be_picked_and_a_free_one_can_and_the_focus_stays_on_it()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var model = host.Sheet();
            var sheet = new ProfileSheet(model);
            using var stage = new ReserveViewStage(sheet);
            Click(sheet.TemplateButtons[0]);
            stage.Layout();

            Click(sheet.ShapeButtons[0]);                                                                    // Channel A's squircle
            Assert.Equal(AsturShape.Hexagon, model.Draft!.Shape);

            sheet.ShapeButtons[5].Focus();
            Click(sheet.ShapeButtons[5]);
            stage.Layout();
            Assert.Equal(AsturShape.Star, model.Draft.Shape);
            Assert.Equal([false, false, false, false, false, true], sheet.ShapeButtons.Select(b => b.IsChecked == true));
            Assert.True(sheet.ShapeButtons[5].IsKeyboardFocusWithin);                                        // the sheet was rebuilt, and the focus was put back
            Assert.Contains("Star. Squircle belongs to Channel A.", Texts(sheet));
        });
    }

    [Fact]
    public void Ticking_a_duty_and_typing_a_budget_reach_the_card_and_a_half_typed_number_is_left_alone()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var model = host.Sheet();
            var sheet = new ProfileSheet(model);
            using var stage = new ReserveViewStage(sheet);
            Click(sheet.TemplateButtons[0]);
            stage.Layout();

            sheet.DutyBoxes[1].IsChecked = false;
            stage.Layout();
            Assert.Equal([true, false, true], model.Draft!.Duties.Select(d => d.Keep));
            Assert.Equal([true, false, true], sheet.DutyBoxes.Select(b => b.IsChecked == true));

            sheet.PercentBox!.Text = "7.";
            stage.Layout();
            Assert.Equal("7.", sheet.PercentBox.Text);                                                       // nothing rewrites what is being typed
            Assert.Equal(7.0, model.Draft.Budget.DailyPercent);
            sheet.DollarsBox!.Text = "2.5";
            Assert.Equal(2.5m, model.Draft.Budget.DailyDollars);
            sheet.PercentBox.Text = "lots";
            Assert.Equal("• Type the daily percent as a number, like 15.", sheet.ProblemsText);
            Assert.False(sheet.CreateButton!.IsEnabled);
        });
    }

    [Fact]
    public void Create_hands_the_card_over_closes_the_sheet_and_puts_the_focus_back_on_the_opener()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var opener = Opener();
            var sheet = new ProfileSheet(host.Sheet(), opener);
            var (root, _) = Mount(sheet, opener);
            using var stage = new ReserveViewStage(root);
            var finished = 0;
            sheet.Finished += () => finished++;
            Click(sheet.TemplateButtons[1]);
            stage.Layout();

            Click(sheet.CreateButton!);
            stage.Layout();

            var made = Assert.Single(host.Created);
            Assert.Equal(("Store", "store"), (made.Name, made.Template));
            Assert.Equal(1, finished);
            Assert.True(opener.IsFocused);
            Click(sheet.CancelButton!);                                                                      // after it is done, a stray Cancel does nothing more
            Assert.Equal(1, finished);
            Assert.Equal(0, host.Cancelled);
        });
    }

    [Fact]
    public void A_refusal_from_the_host_stays_on_the_sheet_and_the_sheet_stays_open()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host { Answer = ["There can be at most 6 Asturs. Retire one first."] };
            var sheet = new ProfileSheet(host.Sheet());
            using var stage = new ReserveViewStage(sheet);
            var finished = 0;
            sheet.Finished += () => finished++;
            Click(sheet.TemplateButtons[0]);
            stage.Layout();
            Click(sheet.CreateButton!);
            stage.Layout();
            Assert.Equal(0, finished);
            Assert.Equal("• There can be at most 6 Asturs. Retire one first.", sheet.ProblemsText);
            Assert.False(sheet.CreateButton!.IsEnabled);
        });
    }

    [Fact]
    public void Escape_closes_the_sheet_tells_the_host_and_returns_the_focus_to_the_opener_from_any_field()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var opener = Opener();
            var sheet = new ProfileSheet(host.Sheet(), opener);
            var (root, _) = Mount(sheet, opener);
            using var stage = new ReserveViewStage(root);
            var finished = 0;
            sheet.Finished += () => finished++;
            Click(sheet.TemplateButtons[0]);
            stage.Layout();
            sheet.AreaBox!.Focus();

            stage.Press(Key.Escape);

            Assert.Equal((1, 1), (finished, host.Cancelled));
            Assert.True(opener.IsFocused);
            stage.Press(Key.Escape);
            Assert.Equal((1, 1), (finished, host.Cancelled));                                                // once
        });
    }

    [Fact]
    public void Cancel_does_the_same_as_Escape()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var opener = Opener();
            var sheet = new ProfileSheet(host.Sheet(), opener);
            var (root, _) = Mount(sheet, opener);
            using var stage = new ReserveViewStage(root);
            Click(sheet.CancelButton!);
            Assert.Equal(1, host.Cancelled);
            Assert.True(opener.IsFocused);
            Assert.Equal("Cancel and close the sheet", AutomationProperties.GetName(sheet.CancelButton!));
        });
    }

    [Fact]
    public void A_sheet_for_review_shows_Dextras_card_at_once_and_a_sheet_for_editing_says_Save()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var draft = AsturDrafter.FromTemplate("research", host.Team);
            var review = new ProfileSheet(host.Sheet(ProfileSheetMode.Review, draft));
            using var stage = new ReserveViewStage(review);
            Assert.Null(review.SentenceBox);
            Assert.Empty(review.TemplateButtons);
            Assert.Equal("Desk", review.NameBox!.Text);
            Assert.Contains("Dextra drafted this profile. Nothing is made until you create it; change any field first.", Texts(review));
            Assert.Contains("Never: signing in, writing through connectors.", Texts(review));              // what the owner made stricter is said

            var edit = new ProfileSheet(host.Sheet(ProfileSheetMode.Edit, AsturTeamRules.ToDraft(host.Team[1]), "a"));
            using var editStage = new ReserveViewStage(edit);
            Assert.Equal("Edit Channel A", AutomationProperties.GetName(edit));
            Assert.Equal("Save the changes", AutomationProperties.GetName(edit.CreateButton!));
            Assert.True(edit.CreateButton!.IsEnabled);                                                       // its own name and look are not clashes
            Assert.Equal([true, true, true, true, true, true], edit.ShapeButtons.Select(b => b.IsEnabled));
        });
    }

    [Fact]
    public void Long_text_a_narrow_window_and_right_to_left_text_wrap_without_running_past_the_window()
    {
        headless.OnUiThread(() =>
        {
            var host = new Host();
            var sheet = new ProfileSheet(host.Sheet());
            var scroll = new ScrollViewer { Content = sheet, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            using var stage = new ReserveViewStage(scroll, width: 375, height: 800, flow: FlowDirection.RightToLeft);
            Click(sheet.TemplateButtons[4]);
            stage.Layout();
            sheet.AreaBox!.Text = string.Join(' ', Enumerable.Repeat("مجھے ہر ہفتے کی اعداد و شمار کی ایک مختصر رپورٹ چاہیے", 14))[..AsturTeamRules.AreaMax];
            sheet.NameBox!.Text = new string('م', AsturTeamRules.NameMax + 5);
            stage.Layout();

            Assert.Contains("• A name holds at most 40 characters.", sheet.ProblemsText, StringComparison.Ordinal);
            foreach (var control in Nodes<Control>(sheet).Where(c => c is Button or TextBox or CheckBox or ToggleButton && c.IsEffectivelyVisible))
            {
                var area = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(stage.Window)!.Value);
                Assert.True(area.Left >= -0.5 && area.Right <= 375.5, $"{control.GetType().Name} runs past the window: {area.Left} to {area.Right}");
            }
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, "the sheet scrolls sideways");
            Assert.True(sheet.AreaBox.Bounds.Height > 40, "the long area wrapped onto more lines");
            Assert.All(Nodes<Button>(sheet).Where(b => b.IsEffectivelyVisible && b is not ToggleButton), b => Assert.True(b.Bounds.Height >= 28, "a button is shorter than 28 px"));
        });
    }

    [Fact]
    public async Task The_review_sheet_answers_the_real_decision_card_through_the_controller()
    {
        using var rig = await AsturRig.OpenAsync();
        var home = rig.DextraHome();
        var runner = rig.Runner(home);
        await rig.Call(runner, "create_assistant", """{"name":"Channel C","area":"Channel","purpose":"My third channel: scripts and numbers.","shape":"star"}""");
        var card = Assert.Single(rig.Controller.Queue.Waiting);

        var closed = 0;
        var model = new ProfileSheetModel(ProfileSheetMode.Review, () => rig.Team.Profiles,
            made => Task.FromResult(rig.Controller.CreateFromCard(card.Id, made)), () => closed++, rig.Controller.DraftOf(card.Id));
        Assert.Equal("Channel C", model.Draft!.Name);
        model.SetName("Channel Three");
        Assert.True(await model.Create());
        await rig.Controller.Settled();

        Assert.Equal("Channel Three", Assert.Single(rig.Team.Active, p => !p.IsRoot).Name);
        Assert.DoesNotContain(rig.Controller.Queue.Waiting, c => c.Id == card.Id);
        Assert.True(model.Done);
    }
}

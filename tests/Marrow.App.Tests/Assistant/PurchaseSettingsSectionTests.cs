using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7; 16.2; the canvas's section 9 "Purchases"; lean-tests 10 "New controls"; ED-NEW-dextra-as20-13):
// Settings › Assistant › Purchases in a headless window. Without Dextra running it says so; without a fill path it says Dextra
// hands the payment over and offers no switch; with one, the switch, the card's buttons and every limit are named, and the card
// is only "•••• 5556". The add-card sheet draws dots, takes nothing from the clipboard, and Escape closes it and puts the focus
// back. It fits a 375 px window right to left. A purchase card on the Decisions tab pays by its total.
[Collection("Headless")]
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-13")]
public sealed class PurchaseSettingsSectionTests(HeadlessSession headless)
{
    private static void Wait(Task task)
    {
        HeadlessSession.RunDispatcherUntil(task, TimeSpan.FromSeconds(10));
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Without_dextra_running_the_section_says_purchases_are_set_up_while_it_is_on()
    {
        headless.OnUiThread(() =>
        {
            var section = new PurchaseSettingsSection(() => null);
            using var stage = new ReserveViewStage(section);
            Assert.Contains(PurchaseWords.Section, Texts(section));
            Assert.Contains(PurchaseWords.NotRunning, Texts(section));
            Assert.Null(section.Switch);
        });
    }

    [Fact]
    public void Without_a_fill_path_it_says_dextra_hands_the_payment_over_and_offers_no_switch_card_or_limits()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig(canPay: false);
            rig.Spent(46.80m, PurchaseRig.Start);
            var section = new PurchaseSettingsSection(() => rig.Desk);
            using var stage = new ReserveViewStage(section);
            var texts = Texts(section);
            Assert.Contains(PurchaseWords.HandOverLead, texts);
            Assert.Contains(PurchaseWords.HandOverWhy, texts);
            Assert.Contains(PurchaseWords.NeverHint, texts);
            Assert.Null(section.Switch);
            Assert.Null(section.AddButton);
            Assert.Empty(section.LimitBoxes);
            var line = Assert.Single(section.HistoryLines);
            Assert.StartsWith("shop.example · $46.80 · ", line, StringComparison.Ordinal);    // the shop, the amount, the date (the PC's own day)
            Assert.EndsWith(" Oct 2026", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void With_a_fill_path_every_control_is_named_and_the_card_is_only_its_last_4()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig();
            var section = new PurchaseSettingsSection(() => rig.Desk);
            using var stage = new ReserveViewStage(section);
            Assert.Contains(PurchaseWords.NoCardYet, Texts(section));
            Assert.Equal(PurchaseWords.AddCard, AutomationProperties.GetName(section.AddButton!));
            Assert.False(rig.Desk.State.Policy.On);

            Wait(rig.On());
            section.Show();
            stage.Layout();
            var texts = Texts(section);
            Assert.Contains("•••• 5556", texts);
            Assert.Contains(PurchaseWords.SwitchDetail(hasCard: true, on: true), texts);   // the state is said in words, not only by the switch
            Assert.Equal(PurchaseWords.Switch, AutomationProperties.GetName(section.Switch!));
            Assert.Equal("Replace the card", AutomationProperties.GetName(section.ReplaceButton!));
            Assert.Equal("Remove the card", AutomationProperties.GetName(section.RemoveButton!));
            Assert.Equal(["Limit per purchase, in USD", "Limit per day, in USD", "Limit per month, in USD"], section.LimitBoxes.Values.Select(b => AutomationProperties.GetName(b)));
            Assert.Equal(["50.00", "150.00", "300.00"], section.LimitBoxes.Values.Select(b => b.Text));
            var (astur, box) = Assert.Single(section.AsturBoxes);
            Assert.Equal(("store-2", "Store: limit per month, in USD", "0.00"), (astur.Id, AutomationProperties.GetName(box), box.Text));
            Assert.All(texts, t => Assert.DoesNotContain(PurchaseRig.Number[..12], t, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_limit_typed_in_its_box_counts_only_after_windows_hello()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig();
            Wait(rig.On());
            var section = new PurchaseSettingsSection(() => rig.Desk);
            using var stage = new ReserveViewStage(section);
            var day = section.LimitBoxes[PurchaseLimitKind.PerDay];
            rig.Hello.Then(HelloResult.Cancelled);
            day.Focus();
            day.Text = "90";
            stage.Press(Key.Enter);
            Assert.Equal(150m, rig.Desk.State.Policy.Limits.PerDay);
            Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingChanged), section.StatusText);
            day.Text = "90";
            stage.Press(Key.Enter);
            Assert.Equal(90m, rig.Desk.State.Policy.Limits.PerDay);
            Assert.Equal(PurchaseWords.LimitSaved, section.StatusText);
            day.Text = "ninety";
            stage.Press(Key.Enter);
            Assert.Equal(PurchaseWords.LimitTyped, section.StatusText);
        });
    }

    [Fact]
    public void The_add_card_sheet_draws_dots_takes_nothing_from_the_clipboard_and_escape_closes_it_and_returns_focus()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig();
            var section = new PurchaseSettingsSection(() => rig.Desk);
            using var stage = new ReserveViewStage(section);
            Click(section.AddButton!);
            stage.Layout();
            var sheet = section.Sheet!;
            Assert.Equal([PurchaseWords.NumberName, PurchaseWords.ExpiryName, PurchaseWords.CodeName],
                new[] { sheet.Number, sheet.Expiry, sheet.Code }.Select(b => AutomationProperties.GetName(b)));
            Assert.True(sheet.Number.IsFocused);
            stage.Window.KeyTextInput(PurchaseRig.Number[..8]);
            stage.Layout();
            Assert.Equal("••••••••", sheet.Number.Shown);
            foreach (var key in new[] { Key.V, Key.C, Key.X, Key.A })                    // paste, copy, cut and select-all do nothing
            {
                stage.Window.KeyPress(key, RawInputModifiers.Control, PhysicalKey.None, null);
                stage.Window.KeyRelease(key, RawInputModifiers.Control, PhysicalKey.None, null);
            }
            stage.Layout();
            Assert.Equal("••••••••", sheet.Number.Shown);
            stage.Press(Key.Back);
            Assert.Equal("•••••••", sheet.Number.Shown);
            stage.Window.KeyTextInput(PurchaseRig.Number[7..]);
            stage.Layout();
            Assert.Equal("•••••••••••• 5556", sheet.Number.Shown);                     // whole and checked: its last 4, nothing more
            Assert.All(Texts(section), t => Assert.DoesNotContain(PurchaseRig.Number[..12], t, StringComparison.Ordinal));
            Assert.False(sheet.SaveButton.IsEnabled);

            stage.Press(Key.Escape);
            Assert.Null(section.Sheet);
            Assert.Same(section.AddButton, TopLevel.GetTopLevel(section)!.FocusManager!.GetFocusedElement());
            Assert.Equal(0, rig.Hello.Asked);
        });
    }

    [Fact]
    public void A_card_typed_whole_is_checked_in_words_and_saved_with_windows_hello()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig();
            var section = new PurchaseSettingsSection(() => rig.Desk);
            using var stage = new ReserveViewStage(section);
            Click(section.AddButton!);
            stage.Layout();
            var sheet = section.Sheet!;
            stage.Window.KeyTextInput("4000056655665557");                               // one digit off: the check digit fails
            sheet.Expiry.Focus();
            stage.Window.KeyTextInput(PurchaseRig.Expiry);
            sheet.Code.Focus();
            stage.Window.KeyTextInput(PurchaseRig.Code);
            stage.Layout();
            Assert.Equal("! " + PurchaseWords.BadNumber, sheet.CheckText);
            sheet.Number.Focus();
            stage.Press(Key.Back);
            stage.Window.KeyTextInput("6");
            stage.Layout();
            Assert.Equal("✓ " + PurchaseWords.Checked, sheet.CheckText);
            Assert.True(sheet.SaveButton.IsEnabled);
            Click(sheet.SaveButton);
            stage.Layout();
            Assert.Null(section.Sheet);
            Assert.Equal(["save the card"], rig.Hello.Reasons);
            Assert.Contains("•••• 5556", Texts(section));
            Assert.Same(section.ReplaceButton, TopLevel.GetTopLevel(section)!.FocusManager!.GetFocusedElement());
        });
    }

    [Fact]
    public void Long_text_a_narrow_window_and_right_to_left_fit_without_running_past_the_window()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig
            {
                Profiles = [new AssistantProfile("dextra", "Dextra"), new AssistantProfile("store-2", new string('م', 60)) { ParentId = "dextra" }],
            };
            Wait(rig.On());
            var section = new PurchaseSettingsSection(() => rig.Desk);
            var scroll = new ScrollViewer { Content = section, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            using var stage = new ReserveViewStage(scroll, width: 375, height: 900, flow: Avalonia.Media.FlowDirection.RightToLeft);
            Click(section.ReplaceButton!);
            stage.Layout();
            foreach (var control in Nodes<Control>(section).Where(c => c is Button or TextBox or SecretDigitsBox && c.IsEffectivelyVisible))
            {
                var area = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(stage.Window)!.Value);
                Assert.True(area.Left >= -0.5 && area.Right <= 375.5, $"{control.GetType().Name} runs past the window: {area.Left} to {area.Right}");
            }
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, "the section scrolls sideways");
        });
    }

    [Fact]
    public void A_purchase_card_on_the_decisions_tab_pays_by_its_total_and_shows_only_the_last_4()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new PurchaseRig();
            Wait(rig.On());
            var item = rig.Queue.Find(Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!))!;
            var answered = new List<DecisionAnswer>();
            var card = new DecisionCard(item, new DecisionContext((_, answer) => { answered.Add(answer); return null; }));
            using var stage = new ReserveViewStage(card);
            var texts = Texts(card);
            Assert.Contains("Pay $46.80", texts);
            Assert.Contains(PurchaseWords.DontBuy, texts);
            Assert.Contains("•••• 5556", texts);
            Assert.Contains(PurchaseWords.Rule, texts);
            Assert.All(texts, t => Assert.DoesNotContain(PurchaseRig.Number[..12], t, StringComparison.Ordinal));
        });
    }
}

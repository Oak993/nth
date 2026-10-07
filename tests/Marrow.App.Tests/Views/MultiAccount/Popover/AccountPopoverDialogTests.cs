using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Popover;

// PORT-ONLY: MA-3b-4, account defaults :2699 and Handshake dialog :2628-2660, OD-0119.
[Collection("Headless")]
public sealed class AccountPopoverDialogTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Defaults_popup_stays_on_the_same_row_when_defaults_change()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts); AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model, new AccountPopoverContext(rig.Accounts, _ => Models)), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle(); var row = view!.Rows[0];
            Assert.True(h.Press("popover-config-" + a.Id.ToString("N"))); Assert.Empty(port.Pins); Assert.True(row.Dialogs!.DefaultsPopup.IsOpen);
            Assert.Single(h.All().OfType<AccountDefaultsPanel>()).SelectModel("sonnet"); port.Refresh(); h.Settle();
            Assert.Same(row, view.Rows[0]); Assert.True(row.Dialogs.DefaultsPopup.IsOpen);
            Assert.Equal("sonnet", rig.Accounts.Get(a.Id).Defaults.Model);
        });
    }
    private static readonly ModelOption[] Models = [new("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "medium", "high", "max"]), defaultEffort: "medium", fastTier: "fast"), new("sonnet", "Sonnet 5.5", efforts: new ValueList<string>(["low", "medium", "high"]), defaultEffort: "medium")];
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Defaults_use_the_locked_effort_card_and_write_only_this_account()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Accounts.SetDefaults(a.Id, new AccountDefaults { Model = "opus", Effort = "medium" }); AccountDefaultsPanel? panel = null;
        MultiAccountHarness.Run(headless, rig, () => panel = new AccountDefaultsPanel(rig.Accounts, a.Id, "Claude Max 1", Models), h =>
        {
            Assert.Contains("This account’s defaults · used whenever a thread moves to it", h.Words());
            Assert.Contains("Changing the model or effort in the composer applies to this thread until the next switch.", h.Words());
            Assert.NotNull(panel!.Card); var slider = Assert.Single(h.All().OfType<EffortSlider>());
            slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); h.Settle();
            Assert.Equal("high", rig.Accounts.Get(a.Id).Defaults.Effort);
            var fast = Assert.Single(h.All().OfType<FastModeButton>()); Assert.Single(fast.GetVisualDescendants().OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            Assert.True(rig.Accounts.Get(a.Id).Defaults.FastMode); Assert.False(rig.Accounts.Get(b.Id).Defaults.FastMode);
            panel.SelectModel("sonnet"); h.Settle(); Assert.Equal("sonnet", rig.Accounts.Get(a.Id).Defaults.Model); Assert.False(rig.Accounts.Get(a.Id).Defaults.FastMode);
        });
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData("Pro", 95)] [InlineData("Max 20x", 98)]
    public void Smart_shows_the_plan_start_and_slider_stops_write_80_through_99(string plan, int start)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, plan); AccountHandshakePanel? panel = null;
        MultiAccountHarness.Run(headless, rig, () => panel = new AccountHandshakePanel(rig.Accounts, a.Id, "Claude", ProviderKind.Claude), h =>
        {
            Assert.Contains("near " + start + "% on this plan", h.Words());
            // Retired generic slider aria in both Pro/Max20x cases: T15-review-rulings.md #8 / ED-507.
            // HTML2635 / MAUI-024 pins "Handshake point for this account"; slider/store assertions stay.
            for (var n = 80; n <= 99; n++) { panel!.SetPoint(new HandshakePoint(n)); Assert.Equal(n, rig.Accounts.Get(a.Id).Defaults.HandshakePoint!.Percentage); }
            panel!.UseDefault(); Assert.Null(rig.Accounts.Get(a.Id).Defaults.HandshakePoint);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Row_menu_has_handshake_and_a_quiet_note_only_when_the_point_differs()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts); AccountPopover? view = null;
        var context = new AccountPopoverContext(rig.Accounts, _ => Models);
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model, context), h =>
        {
            Assert.Contains(view!.Rows[0].Menu, e => e.Key == "handshake" && e.Label == "Handshake at…"); Assert.Null(view.Rows[0].HandshakeNote);
            rig.Accounts.SetDefaults(a.Id, new AccountDefaults { HandshakePoint = new HandshakePoint(99) }); port.Refresh(); h.Settle(); Assert.Equal("Handshake at 99%", view.Rows[0].HandshakeNote);
            rig.Accounts.SetDefaults(a.Id, new AccountDefaults { HandshakePoint = HandshakePoint.Smart }); port.Refresh(); h.Settle(); Assert.Null(view.Rows[0].HandshakeNote);
        });
    }
}

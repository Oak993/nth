using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Popover;

// PORT-ONLY: MA-3b-3, AccountPanel dragList :2729-2745 and Settings same-order contract.
[Collection("Headless")]
public sealed class AccountPopoverReorderTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Opening_the_row_menu_does_not_pin_the_thread()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts);
        MultiAccountHarness.Run(headless, rig, () => new AccountPopover(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            Assert.True(h.Press("popover-more-" + a.Id.ToString("N")));
            Assert.Empty(port.Pins); Assert.Contains("Move to top", h.Words());
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Pointer_drag_on_grip_uses_the_production_input_path_without_pinning()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts);
        MultiAccountHarness.Run(headless, rig, () => new AccountPopover(model), h =>
        {
            var grip = h.Find<Control>("popover-grip-" + a.Id.ToString("N"));
            var point = grip.TranslatePoint(new Avalonia.Point(11, 11), h.Window)!.Value;
            h.Window.MouseDown(point, MouseButton.Left); h.Window.MouseMove(point + new Avalonia.Vector(0, 80)); h.Window.MouseUp(point + new Avalonia.Vector(0, 80), MouseButton.Left);
            port.Refresh(); h.Settle();
            Assert.Equal(new[] { b.Id, a.Id }, rig.Accounts.ForService(ProviderKind.Claude).Select(x => x.Id)); Assert.Empty(port.Pins);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Grip_drag_moves_by_row_distance_and_persists_the_Settings_order()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude); var c = rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts);
        AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model), h =>
        {
            view!.Rows[0].DragBy(141); port.Refresh(); h.Settle();
            Assert.Equal(new[] { b.Id, c.Id, a.Id }, rig.Accounts.ForService(ProviderKind.Claude).Select(x => x.Id));
            Assert.Equal(new[] { b.Id, c.Id, a.Id }, view.Rows.Select(x => x.AccountId));
            Assert.True(h.Find<Button>("popover-pin-" + a.Id.ToString("N")).IsFocused);
            view.Rows[2].DragBy(-10000); port.Refresh(); h.Settle(); Assert.Equal(a.Id, view.Rows[0].AccountId);
        });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Alt_arrows_reorder_keep_focus_and_menu_actions_disable_at_ends()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Codex); var b = rig.ConnectAnother(ProviderKind.Codex);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Codex); using var model = new AccountPopoverModel(port, rig.Accounts); AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model), h =>
        {
            Assert.True(view!.Rows[0].Menu.Single(x => x.Key == "up").Disabled);
            Assert.True(view.Rows[1].Menu.Single(x => x.Key == "down").Disabled);
            view.Rows[1].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up, KeyModifiers = KeyModifiers.Alt });
            port.Refresh(); h.Settle(); Assert.Equal(b.Id, view.Rows[0].AccountId);
            Assert.True(h.Find<Button>("popover-pin-" + b.Id.ToString("N")).IsFocused);
            view.Rows[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt });
            port.Refresh(); h.Settle(); Assert.Equal(a.Id, view.Rows[0].AccountId);
        });
    }
}

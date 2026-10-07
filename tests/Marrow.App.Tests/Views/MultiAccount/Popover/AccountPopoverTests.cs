using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Capsule;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Popover;

// PORT-ONLY: MA-3b-2, locked AccountPanel :2674-2797, OD-0115/0119.
[Collection("Headless")]
public sealed class AccountPopoverTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Capsule_click_toggles_exactly_one_glass_popover()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude, "Max");
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, ProviderKind.Claude), rig.Accounts);
        ComposerCapsuleSlot? slot = null; AccountPopoverHost? host = null;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            slot = new ComposerCapsuleSlot(new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => null), new CapsuleStyleSetting(rig.Settings), () => new CapsuleSubject(ProviderKind.Claude, a.Id, null, false, null));
            host = new AccountPopoverHost(slot, model); return new Panel { Children = { slot, host.Popup } };
        }, h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            var face = Assert.Single(slot!.Capsule!.GetVisualDescendants().OfType<Button>());
            face.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            Assert.True(host!.Presented); Assert.True(host.Popup.IsOpen);
            Assert.IsType<AccountPopover>(PopoverHost.Content(host.Popup));
            face.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            Assert.False(host.Presented); Assert.False(host.Popup.IsOpen);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Rows_preserve_order_render_usage_and_pin_through_the_port()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Usage[a.Id] = new(.9, null, .43, null, false, false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new AccountPopoverModel(port, rig.Accounts);
        AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model), h =>
        {
            Assert.Equal(new[] { a.Id, b.Id }, view!.Rows.Select(r => r.AccountId));
            Assert.Contains("Claude Code accounts", h.Words()); Assert.Contains("Drag to set the order", h.Words());
            Assert.Equal(.9, view.Rows[0].FiveHour.Target); Assert.True(view.Rows[0].FiveHour.Hot);
            Assert.Equal("90%", view.Rows[0].FiveHour.Percent); Assert.Equal("43%", view.Rows[0].Week.Percent);
            Assert.Contains("Claude Max 1", AutomationProperties.GetName(view.Rows[0])!, StringComparison.Ordinal);
            Assert.True(h.Press("popover-pin-" + b.Id.ToString("N")));
            Assert.Equal(b.Id, Assert.Single(port.Pins).Account);
            Assert.Contains("This thread: Claude Max 2", string.Join(" ", h.Words()), StringComparison.Ordinal);
            Assert.True(h.Press("popover-back-to-order")); Assert.Null(port.PinnedAccountId);
        });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Exhausted_row_does_not_pin_and_feedback_lasts_300_ms()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); rig.ConnectAnother(ProviderKind.Claude);
        rig.Usage[a.Id] = new(1, null, .5, null, false, false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var model = new AccountPopoverModel(port, rig.Accounts);
        AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model), h =>
        {
            h.Press("popover-pin-" + a.Id.ToString("N")); Assert.Empty(port.Pins);
            Assert.True(view!.Rows[0].FeedbackRunning); Assert.Contains("Skipped", h.Words());
            Assert.True(view.Rows[0].FiveHour.Out); Assert.True(view.Rows[0].Dimmed);
            h.Step(150); Assert.True(view.Rows[0].FeedbackRunning);
            h.Step(150); Assert.False(view.Rows[0].FeedbackRunning);
        }, motion: true);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void No_five_hour_is_report_driven_and_local_has_no_bars(bool local)
    {
        var rig = new AccountsRig(); var service = local ? ProviderKind.Local : ProviderKind.Codex;
        var a = rig.Connect(service); rig.ConnectAnother(service); rig.Usage[a.Id] = new(null, null, .2, null, true, false);
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, service), rig.Accounts);
        AccountPopover? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new AccountPopover(model), h =>
        {
            Assert.False(view!.Rows[0].FiveHour.IsEffectivelyVisible);
            Assert.Equal(!local, view.Rows[0].Week.IsEffectivelyVisible);
            Assert.Contains(local ? "No limit · Stays on this PC" : "No 5-hour limit", h.Words());
            if (local) Assert.Contains("Never runs out", h.Words());
        });
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Escape_requests_close_and_reduce_motion_feedback_has_no_translation()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); rig.ConnectAnother(ProviderKind.Claude);
        rig.Usage[a.Id] = new(1, null, null, null, false, false);
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, ProviderKind.Claude), rig.Accounts);
        AccountPopover? view = null; var closed = 0;
        MultiAccountHarness.Run(headless, rig, () => { view = new AccountPopover(model); view.CloseRequested += () => closed++; return view; }, h =>
        {
            h.Press("popover-pin-" + a.Id.ToString("N")); Assert.Null(view!.Rows[0].RenderTransform);
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(1, closed);
        }, reduceMotion: true);
    }
}

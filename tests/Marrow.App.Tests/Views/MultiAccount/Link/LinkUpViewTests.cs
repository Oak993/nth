using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Testing.MultiAccountSettings;
namespace Marrow.App.Tests.Views.MultiAccount.Link;
[Collection("Headless")]
public sealed class LinkUpViewTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Unavailable_flight_stops_and_cannot_cancel_the_next_link()
    {
        var rig = new OpinionViewRig(); var third = rig.Accounts.ConnectAnother(ProviderKind.Claude, "Max").Id; rig.Current.Refresh();
        var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            h.Press("link-account-" + rig.Other.ToString("N"));
            rig.Accounts.Usage[rig.Other] = new(1, null, null, null, false, false); rig.Current.Refresh(); h.Settle();
            Assert.False(view!.Flight!.Running);
            Assert.True(h.Press("link-account-" + third.ToString("N"))); h.Step(560);
            Assert.Equal(third, Assert.Single(port.Calls).Account);
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Unlink_restores_the_retained_capsule_layers_and_stops_the_extra_painter()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        ComposerCapsuleSlot? slot = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () =>
        {
            slot = new(new ComposerCapsuleModel(rig.Accounts.Accounts, rig.Accounts.Pills, rig.Accounts.IsConnected, _ => null), new CapsuleStyleSetting(rig.Accounts.Settings), () => new CapsuleSubject(ProviderKind.Claude, rig.Lead, null, false, null));
            return new LinkedAccountCapsule(slot, model);
        }, h =>
        {
            var original = slot!.Capsule!.Children[0]; var opacity = original.Opacity;
            port.Link(rig.Thread, rig.Other); h.Step(260); h.Settle();
            Assert.True(h.Press("capsule-unlink-" + rig.Other.ToString("N"))); h.Step(260); h.Settle();
            Assert.Equal(opacity, original.Opacity);
            Assert.DoesNotContain(slot.Capsule.GetVisualDescendants(), c => c is LinkedCapsulePaint);
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Fly_opacity_stays_one_until_the_midpoint_keyframe()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            h.Press("link-account-" + rig.Other.ToString("N")); h.Step(50);
            Assert.Equal(1, view!.Flight!.Opacity); Assert.Empty(port.Calls);
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Actual_capsule_popover_adds_link_column_and_flight_uses_row_number_and_capsule_coordinates()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort();
        using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts);
        ComposerCapsuleSlot? slot = null; AccountPopoverHost? host = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () =>
        {
            slot = new(new ComposerCapsuleModel(rig.Accounts.Accounts, rig.Accounts.Pills, rig.Accounts.IsConnected, _ => null), new CapsuleStyleSetting(rig.Accounts.Settings), () => new CapsuleSubject(ProviderKind.Claude, rig.Lead, null, false, null));
            host = new(slot, popover);
            return LinkComposerHost.With(slot, new Panel { Children = { slot, host.Popup } }, model);
        }, h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            Assert.Single(slot!.Capsule!.GetVisualDescendants().OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            var view = Assert.IsType<LinkPopoverColumns>(PopoverHost.Content(host!.Popup));
            var row = Assert.Single(view.Popover.Rows, r => r.AccountId == rig.Other);
            var number = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2");
            var source = h.Window.PointToClient(number.PointToScreen(new Point(number.Bounds.Width / 2 - 11, number.Bounds.Height / 2 - 11)));
            var destination = h.Window.PointToClient(slot.PointToScreen(new Point(slot.Bounds.Width - 16, slot.Bounds.Height / 2 - 11)));
            Assert.True(h.Press("link-account-" + rig.Other.ToString("N")));
            var flight = Assert.IsType<LinkFlyMark>(view.Flight);
            // Retired row-number origin equality: owner ruling / ED-NEW-ma-wave1-32, HTML2716/2741-2749.
            // Link_flight_starts_at_the_account_monogram_in_the_actual_popover pins the exact monogram origin.
            Assert.Equal(destination, h.Window.PointToClient(flight.PointToScreen(flight.ToPosition - flight.FromPosition)));
            Assert.NotEqual(flight.FromPosition, flight.ToPosition); Assert.Empty(port.Calls);
            h.Step(560); Assert.Single(port.Calls); h.Step(260); h.Settle(); Assert.Contains("M2", h.Words());
            Assert.True(slot.Capsule!.Bounds.Width > 34, "The shared capsule must grow around its linked marks.");
            Assert.Contains(slot.Capsule.GetVisualDescendants().OfType<Button>(), b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "capsule-unlink-" + rig.Other.ToString("N"));
            Assert.Contains("linked with Claude Max 2", Avalonia.Automation.AutomationProperties.GetName(slot.Capsule.Face)!, StringComparison.Ordinal);
            Assert.True(host.Presented); Assert.Empty(rig.Current.Pins);
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Column_hides_active_and_out_links_and_fly_waits_560_ms_before_port_call()
    {
        var rig = new OpinionViewRig(); var outId = rig.Accounts.ConnectAnother(ProviderKind.Claude, "Max").Id;
        rig.Accounts.Usage[outId] = new(1, null, null, null, false, false); rig.Current.Refresh();
        var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current, rig.Codex], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            Assert.Null(h.TryFind("link-account-" + rig.Lead.ToString("N"))); Assert.Null(h.TryFind("link-account-" + outId.ToString("N")));
            Assert.Equal("Link Claude Max 2 to check this thread’s work", Avalonia.Automation.AutomationProperties.GetName(h.Find<Button>("link-account-" + rig.Other.ToString("N"))));
            h.Press("link-account-" + rig.Other.ToString("N")); Assert.Empty(port.Calls);
            Assert.True(view!.Flight!.Running); Assert.Equal(.560, view.Flight.DurationSeconds);
            Assert.Equal((.45, 0d, .2, 1d), view.Flight.Curve);
            h.Step(559); Assert.Empty(port.Calls); h.Step(1); Assert.Equal("link", Assert.Single(port.Calls).Call);
            h.Press("link-account-" + rig.Other.ToString("N")); Assert.Equal("unlink", port.Calls.Last().Call);
            Assert.Empty(rig.Current.Pins);
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Reduce_motion_crossfades_for_260_ms_without_translation()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            h.Press("link-account-" + rig.Other.ToString("N")); Assert.Empty(port.Calls); Assert.True(view!.Flight!.Crossfade);
            Assert.Null(view.Flight.RenderTransform); h.Step(130); Assert.InRange(view.Flight.Opacity, .1, .9); Assert.Empty(port.Calls);
            h.Step(130); Assert.Single(port.Calls);
        }, motion: true, reduceMotion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Capsule_grows_for_two_links_keeps_order_and_unlinks_by_keyboard_button()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current, rig.Codex], port, rig.Consent);
        LinkedAccountCapsule? capsule = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => capsule = new(new Border { Width = 34, Height = 28 }, model), h =>
        {
            var initial = capsule!.Bounds.Width; port.Link(rig.Thread, rig.Other); h.Settle(); var one = capsule.Bounds.Width;
            port.Link(rig.Thread, rig.Cross); h.Settle(); var two = capsule.Bounds.Width;
            Assert.True(one > initial); Assert.True(two > one); Assert.Equal(new[] { "M2", "C1" }, capsule.Marks);
            Assert.Contains("linked with Claude Max 2 and Codex 1 to check its work", capsule.Aria, StringComparison.Ordinal);
            h.Press("capsule-unlink-" + rig.Other.ToString("N")); Assert.Equal(new[] { "C1" }, capsule.Marks);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Cross_service_confirm_is_unchecked_cancel_sends_nothing_and_explicit_accept_remembers()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current, rig.Codex], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Codex, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle(); Assert.True(h.Press("link-account-" + rig.Cross.ToString("N")));
            Assert.True(model.Pending is not null, "Cross-service consent must remain pending until the user answers; busy=" + model.Busy);
            Assert.Empty(port.Calls); Assert.False(view!.Confirmation!.DontAskAgain.IsChecked ?? false);
            h.Press("other-service-cancel"); Assert.Empty(port.Calls);
            h.Step(260); h.Settle(); // let the glass presenter's closing transition finish before another physical click.
            Assert.True(h.Press("link-account-" + rig.Cross.ToString("N")));
            view.Confirmation!.DontAskAgain.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            h.Press("other-service-accept"); h.Step(560); Assert.Single(port.Calls);
            Assert.True(rig.Consent.Permits(ConsentPurpose.Link, ProviderKind.Codex));
            Assert.False(rig.Consent.Permits(ConsentPurpose.Opinions, ProviderKind.Codex));
        }, motion: true);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Becoming_out_during_fly_cancels_link_and_detaching_releases_frame()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort(); using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts); LinkPopoverColumns? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => { var target = new Border { Width = 34, Height = 28 }; view = new(new AccountPopover(popover), model, () => target); return new StackPanel { Children = { target, view } }; }, h =>
        {
            h.Press("link-account-" + rig.Other.ToString("N")); rig.Accounts.Usage[rig.Other] = new(1, null, null, null, false, false); rig.Current.Refresh(); h.Settle();
            h.Step(560); Assert.Empty(port.Calls);
            h.Root.Children.Clear(); h.Settle(); Assert.False(view!.Flight?.Running ?? false);
        }, motion: true);
    }
}

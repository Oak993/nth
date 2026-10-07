using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.MultiAccount.Link;

[Collection("Headless")]
public sealed class LinkMonogramReviewProofTests(HeadlessSession headless)
{
    // HTML 2716/2741-2749: the actual account monogram flies from its own screen rectangle.
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Link_flight_starts_at_the_account_monogram_in_the_actual_popover()
    {
        var rig = new OpinionViewRig(); var port = new FakeLinkPort();
        using var model = new LinkUpModel(rig.Thread, rig.Current, [rig.Current], port, rig.Consent);
        using var popover = new AccountPopoverModel(rig.Current, rig.Accounts.Accounts);
        ComposerCapsuleSlot? slot = null; AccountPopoverHost? host = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () =>
        {
            slot = new(new ComposerCapsuleModel(rig.Accounts.Accounts, rig.Accounts.Pills, rig.Accounts.IsConnected, _ => null), new CapsuleStyleSetting(rig.Accounts.Settings), () => new CapsuleSubject(ProviderKind.Claude, rig.Lead, null, false, null));
            host = new(slot, popover); return LinkComposerHost.With(slot, new Panel { Children = { slot, host.Popup } }, model);
        }, h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            slot!.Capsule!.Face.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); h.Settle();
            var view = Assert.IsType<LinkPopoverColumns>(PopoverHost.Content(host!.Popup));
            var mark = Assert.Single(view.Popover.Rows, row => row.AccountId == rig.Other).LinkMark;
            Assert.Contains(mark.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "M2");
            var start = h.Window.PointToClient(mark.PointToScreen(new Point(mark.Bounds.Width / 2 - 11, mark.Bounds.Height / 2 - 11)));
            Assert.True(h.Press("link-account-" + rig.Other.ToString("N")));
            var flight = Assert.IsType<LinkFlyMark>(view.Flight);
            Assert.Equal(start, h.Window.PointToClient(flight.PointToScreen(default)));
            Assert.Empty(port.Calls); h.Step(560); Assert.Single(port.Calls);
        }, motion: true);
    }
}

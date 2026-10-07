// PORT-ONLY: MA-3b-2. Capsule click opens one glass PopoverHost via PlainControls, same presenter as MenuPopover.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountPopoverHost : MainActorModel
{
    private bool presented;
    internal bool Presented { get => Read(presented); set => Set(ref presented, value); }
    internal Popup Popup { get; }
    internal AccountPopoverHost(ComposerCapsuleSlot slot, AccountPopoverModel model, AccountPopoverContext? context = null)
    {
        slot.Clicked += Toggle;
        Popup = PlainControls.Popover(slot, new Binding<bool>(() => Presented, value => Presented = value), () =>
        {
            var view = new AccountPopover(model, context); view.CloseRequested += () => Presented = false;
            view.CreatePillRequested += () => { Presented = false; SettingsNavigation.Shared.RequestedPage = SettingsPage.Pills; };
            return view;
        });
        slot.DetachedFromVisualTree += (_, _) => Presented = false;
    }
    private void Toggle() => Presented = !Presented;
    internal static Control With(ComposerCapsuleSlot slot, ThreadRuntime runtime)
    {
        if (runtime.App is not { } app || runtime.Thread is not { } thread || ThreadPortSeams.Offer.Current.Account(runtime.ThreadID, thread.Provider) is not { } port) return slot;
        var model = new AccountPopoverModel(port, app.Settings.Accounts, service => app.Providers.Models(service),
            () => app.Settings.Accounts.Accounts.Count(app.IsAccountConnected));
        var host = new AccountPopoverHost(slot, model, new AccountPopoverContext(app.Settings.Accounts, service => app.Providers.Models(service)));
        var panel = new Panel { Children = { slot, host.Popup } }; return panel;
    }
}

// PORT-ONLY: MA-3b-4, account dialogs use the same glass presenter as the account popover.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Popover;
internal sealed class AccountPopoverDialogs : MainActorModel
{
    private bool defaults;
    private bool handshake;
    private bool Defaults { get => Read(defaults); set => Set(ref defaults, value); }
    private bool Handshake { get => Read(handshake); set => Set(ref handshake, value); }
    internal AccountPopoverDialogs(AccountPopoverContext context, Guid id, string name, ProviderKind service, Control anchor)
    {
        DefaultsPopup = PlainControls.Popover(anchor, new Binding<bool>(() => Defaults, v => Defaults = v), () => CloseKeys(new AccountDefaultsPanel(context.Store, id, name, context.Models(service)), () => Defaults = false));
        HandshakePopup = PlainControls.Popover(anchor, new Binding<bool>(() => Handshake, v => Handshake = v), () => CloseKeys(new AccountHandshakePanel(context.Store, id, name, service), () => Handshake = false));
        Host = new Panel { Width = 0, Height = 0, Children = { DefaultsPopup, HandshakePopup } };
    }
    internal Popup DefaultsPopup { get; }
    internal Popup HandshakePopup { get; }
    internal Control Host { get; }
    internal bool Presented => Defaults || Handshake;
    internal void OpenDefaults() { Handshake = false; Defaults = !Defaults; }
    internal void OpenHandshake() { Defaults = false; Handshake = !Handshake; }
    private static Control CloseKeys(Control content, Action close)
    {
        content.KeyDown += (_, e) => { if (e.Key != Key.Escape) return; close(); e.Handled = true; }; return content;
    }
}

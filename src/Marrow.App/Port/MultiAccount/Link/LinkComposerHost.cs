// PORT-ONLY: MA-3b-5 optional extension around T16's existing capsule/popover host. T16 files remain byte-exact.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Runtime;
using Marrow.UI.Port;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Core.Port;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Opinions;
namespace Marrow.App.Port.MultiAccount.Link;
internal static class LinkComposerHost
{
    internal static Control With(ComposerCapsuleSlot slot, ThreadRuntime runtime)
    {
        var existing = AccountPopoverHost.With(slot, runtime);
        var model = LinkComposerSeams.Offer.Current(runtime.ThreadID);
        if (model is null && runtime.Thread is { } thread)
            model = ThreadPortSeams.Offer.Current.CreateLinkModel(runtime.ThreadID, thread.Provider, new OtherServiceConsentStore(UserDefaults.Standard.Current));
        if (model is null) return existing;
        var view = With(slot, existing, model);
        var lifetime = model; view.AttachedToVisualTree += (_, _) => lifetime.Resume(); view.DetachedFromVisualTree += (_, _) => lifetime.Dispose();
        return view;
    }
    internal static Control With(ComposerCapsuleSlot slot, Control existing, LinkUpModel model)
    {
        if (existing is Panel panel && panel.Children.OfType<Popup>().FirstOrDefault() is { } popup)
        {
            popup.Opened += (_, _) =>
            {
                if (PopoverHost.Content(popup) is AccountPopover content)
                {
                    if (popup.Child is PopoverCard card) { card.Child = null; card.Child = new LinkPopoverColumns(content, model, () => slot); }
                    else { popup.Child = null; popup.Child = new LinkPopoverColumns(content, model, () => slot); }
                }
            };
        }
        return new LinkedAccountCapsule(existing, model);
    }
}

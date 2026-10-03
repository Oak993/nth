// PORT-ONLY: ChatView.swift:1456-1463,1489-1492 sheets use R-VIEW-9's common presentation seam.
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Marrow.UI.Port;
using Marrow.App.Window;
using Marrow.App.Views.Chat;
using Avalonia.VisualTree;

namespace Marrow.App.Port;

internal static class ChatSheetPresentation
{
    // PORT-ONLY: R-VIEW-6/7 preserve the skeleton's Swift view-input constructors while resolving environments on attach.
    public static readonly AttachedProperty<Action?> DismissProperty =
        AvaloniaProperty.RegisterAttached<Control, Action?>("ChatDismiss", typeof(ChatSheetPresentation), inherits: true);

    public static void BuildWhenAttached(Decorator owner, Func<AppModel, Action, Control> content) =>
        owner.Track(() => AppEnvironment.GetAppModel(owner), model =>
        {
            if (owner.Child is null) owner.Child = content(model, () => owner.GetValue(DismissProperty)?.Invoke());
        });

    public static void Show(Control? anchor, Func<Action, Control> content, Action? onDismiss = null)
    {
        anchor ??= (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        // ChatView.swift:1456-1463,1489-1492 / R-VIEW-9. Sheet state belongs to the column,
        // whose chrome body can re-evaluate while a sheet is open (including git.Perform).
        if (anchor is not null)
            anchor = anchor.GetVisualAncestors().OfType<ChatView>().FirstOrDefault()
                ?? (Control?)anchor.GetVisualAncestors().OfType<ChatChromeRow>().FirstOrDefault() ?? anchor;
        var presenter = Presentation.Presenter.Current;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Center, IsLightDismissEnabled = false };
        PopoverHost.SetIsSheet(popup, true); // a SwiftUI .sheet: no popover tip (OD-0095), ED-238's panel on its top-level
        var closed = false;
        EventHandler<VisualTreeAttachmentEventArgs>? detached = null;
        void Cleanup()
        {
            if (anchor is not null && detached is not null) anchor.DetachedFromVisualTree -= detached;
            popup.Child = null;
            onDismiss?.Invoke();
        }
        void Dismiss()
        {
            if (closed) return;
            closed = true;
            try { presenter.SetPresented(popup, false); }
            finally { Cleanup(); }
        }
        popup.Child = content(Dismiss);
        popup.Child.SetValue(DismissProperty, Dismiss);
        popup.Closed += (_, _) => { if (!closed) { closed = true; Cleanup(); } };
        if (anchor is not null) { detached = (_, _) => Dismiss(); anchor.DetachedFromVisualTree += detached; }
        presenter.SetPresented(popup, true);
    }
}

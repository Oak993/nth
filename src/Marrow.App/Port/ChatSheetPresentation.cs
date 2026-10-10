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

    /// <param name="inWindow">PORT-ONLY (ED-333): the sheet is drawn in its anchor's window (WindowSheetLayer) instead of a
    /// Popup window of its own, for a sheet whose background is glass over the app (ChatView.swift:1459-1463): the app's material reads only
    /// what its own window drew under it. A window whose content is not a plain panel (none the port's factory makes) keeps the Popup.</param>
    public static void Show(Control? anchor, Func<Action, Control> content, Action? onDismiss = null, bool inWindow = false, bool still = false)
    {
        anchor ??= (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        // ChatView.swift:1456-1463,1489-1492 / R-VIEW-9. Sheet state belongs to the column,
        // whose chrome body can re-evaluate while a sheet is open (including git.Perform).
        if (anchor is not null)
            anchor = anchor.GetVisualAncestors().OfType<ChatView>().FirstOrDefault()
                ?? (Control?)anchor.GetVisualAncestors().OfType<ChatChromeRow>().FirstOrDefault() ?? anchor;
        // the window's content is the factory's plain container panel (WindowManager.MakeWindow), which lays every child over the whole window
        if (inWindow && anchor is not null && TopLevel.GetTopLevel(anchor) is Avalonia.Controls.Window { Content: Panel root } window && root.GetType() == typeof(Panel))
        {
            ShowInWindow(anchor, root, window, content, onDismiss, still);
            return;
        }
        var presenter = Presentation.Presenter.Current;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Center, IsLightDismissEnabled = false };
        PopoverHost.SetIsSheet(popup, true); // a SwiftUI .sheet: no popover tip (OD-0095), ED-238's panel on its top-level
        var closed = false;
        var cleaned = false;
        EventHandler<VisualTreeAttachmentEventArgs>? detached = null;
        Action release = static () => { };
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            release(); release = static () => { };
            if (anchor is not null && detached is not null) anchor.DetachedFromVisualTree -= detached;
            popup.Child = null;
            onDismiss?.Invoke();
        }
        void Dismiss()
        {
            if (closed) return;
            closed = true;
            try { presenter.SetPresented(popup, false); }
            finally { if (!popup.IsOpen) Cleanup(); }
        }
        popup.Child = content(Dismiss);
        popup.Child.SetValue(PopoverAppear.StillProperty, still);
        popup.Child.SetValue(DismissProperty, Dismiss);
        popup.Closed += (_, _) => { closed = true; Cleanup(); };
        if (anchor is not null) { detached = (_, _) => Dismiss(); anchor.DetachedFromVisualTree += detached; }
        // the window's Escape closes the sheet before what holds the keyboard under it acts on it (SheetKeyboard)
        if (anchor is not null && TopLevel.GetTopLevel(anchor) is { } owner) release = SheetKeyboard.Take(owner, popup.Child, _ => false, Dismiss, blockUnder: false);
        presenter.SetPresented(popup, true);
    }

    // The sheet on a layer over the window's content, under its caption buttons (OD-0044), centred on the anchor. It closes as the Popup
    // did: by its own Dismiss (Cancel, Save), when the anchor leaves the tree, and by Escape (SheetKeyboard, before anything under it acts on the
    // key; WindowTransients too), shrinking away as it grew in (PopoverAppear, Center). While it shows it holds the keyboard: it takes the focus,
    // Tab cycles inside it, no key or text reaches the content under it, and the focus goes back where it was when it closes.
    private static void ShowInWindow(Control anchor, Panel root, Avalonia.Controls.Window window, Func<Action, Control> content, Action? onDismiss, bool still = false)
    {
        WindowSheetLayer? layer = null;
        var closed = false;
        EventHandler<VisualTreeAttachmentEventArgs>? detached = null;
        var focusedBefore = window.FocusManager?.GetFocusedElement() as Control;
        Action release = static () => { };
        void Close(bool animated)
        {
            if (closed || layer is not { } shown) return;
            closed = true;
            release();
            WindowTransients.Hide(shown);
            if (animated && focusedBefore is { } back && TopLevel.GetTopLevel(back) is not null) back.Focus(); // the keyboard goes back where it was
            anchor.DetachedFromVisualTree -= detached;
            // a sheet whose window or column goes away goes with it at once; one dismissed shrinks away first
            if (animated && still) PopoverAppear.Fade(shown.Sheet, opening: false, () => root.Children.Remove(shown));
            else if (animated) PopoverAppear.Dismiss(shown.Sheet, PlacementMode.Center, () => root.Children.Remove(shown));
            else Avalonia.Threading.Dispatcher.UIThread.Post(() => root.Children.Remove(shown)); // not while the tree it is in detaches
            onDismiss?.Invoke();
        }
        void Dismiss() => Close(animated: true);
        var sheet = content(Dismiss);
        sheet.SetValue(DismissProperty, Dismiss);
        layer = new WindowSheetLayer(anchor, sheet);
        var caption = Marrow.UI.Chrome.WindowChrome.CaptionLayer(window);
        var at = root.Children.IndexOf(caption);
        root.Children.Insert(at >= 0 ? at : root.Children.Count, layer);
        detached = (_, _) => Close(animated: false);
        anchor.DetachedFromVisualTree += detached;
        WindowTransients.Show(layer, layer, Dismiss);
        var taken = layer;
        release = SheetKeyboard.Take(window, sheet, visual => SheetKeyboard.Within(visual, taken), Dismiss, blockUnder: true);
        layer.Focus(); // the sheet takes the keyboard (Tab then cycles inside it)
        if (still) PopoverAppear.Fade(sheet, opening: true);
        else PopoverAppear.Play(sheet, PlacementMode.Center);
    }
}

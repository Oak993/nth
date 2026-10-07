using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY (ED-238): what an NSPopover brings by itself and an Avalonia Popup does not. A popover is its own window
// that draws in the appearance of the window it hangs from, on the system's panel material. On Windows a Popup opens as a
// PopupRoot, a separate top-level, and two things went wrong in the native gallery (composer-model-picker-dark.png,
// composer-effort-*-dark.png, main-thread-popover-diff-dark.png, 2026-10-02):
//   1. The popup's appearance. A PopupRoot binds its ActualThemeVariant to the Popup's, and a Popup takes its appearance
//      from its logical parent. Avalonia gives one a parent only for a flyout, a tooltip or a menu (Popup.SetPopupParent,
//      internal); every site of this port builds `new Popup { PlacementTarget = anchor }` and never adds it to a tree, so
//      the popup inherited nothing, the root's variant was null and the content resolved Light inside a dark app.
//      Prepare gives the popup the anchor as its logical parent while it is presented, as Avalonia's own flyout does,
//      and takes it back when the popup closes.
//   2. The fill. PopupRoot's default Background is opaque white (PopupRoot's static constructor), in both appearances,
//      so a dark popover was a white card. The panel the macOS 27 UI Kit draws for popovers' plain panels is the window
//      background with an edge: Dark fill #1E1E1E and a 1 px #FFFFFF at 0.2 border, Light fill #FFFFFF (the kit's shadow is
//      the window manager's). SOURCE FACT (Apple design resource, OD-0045; E:\Droppy\work\apple-kit\macos27-uikit-controls.md,
//      "Panels"). Only a popup the presenter shows as a popover wears it: a tooltip is a Popup too and draws its own card
//      inside a clear surround (PortControlThemes.TipTemplate). Since OD-0095 the panel is drawn by the card the content
//      hangs on (PopoverCard: one outline with the tip facing the anchor), and the PopupRoot is clear around it.
//   3. The material (OD-0107 and its amendment, OD-0005; ED-357). The owner: "when any popup opens, anything in the
//      composer, I see a black, dark background, not liquid glass". An NSPopover is the system's Liquid Glass, so the
//      tipped card is no longer the kit's flat panel: its popup window shows the blurred scene behind it (PopoverGlass, put
//      in when the popup opens and taken out when it closes) and the card draws the glass's tint, hairline and rim over it
//      (Tint, Edge, Rim below, measured on the reference frames). The kit's panel stays for a sheet, and under the tint of a
//      card that has no glass (Fill, Border).
public static class PopoverHost
{
    /// <summary>The style class a presented popover's Popup carries; the panel style matches its PopupRoot through it.</summary>
    internal const string PopoverClass = "marrow-popover";

    internal const string FillKey = "MarrowPort.PopoverFill";

    internal const string BorderKey = "MarrowPort.PopoverBorder";

    internal const string TintKey = "MarrowPort.PopoverTint";

    internal const string EdgeKey = "MarrowPort.PopoverEdge";

    internal const string RimKey = "MarrowPort.PopoverRim";

    // Panels / Dark and Light, macOS 27 UI Kit.
    internal static SwiftColor Fill { get; } = SwiftColor.Dynamic(SwiftColor.White, SwiftColor.Rgb(0x1E1E1E));

    internal static SwiftColor Border { get; } = SwiftColor.Dynamic(SwiftColor.Clear, new SwiftColor(white: 1, opacity: 0.2));

    // The glass's own layers over the window's see-through glass (ED-357 (3)), fitted to the reference frames
    // (PopoverCard's file note): the glass reads lighter than what is round it, by about 20 in Dark and 25 in Light (8-bit),
    // over the blurred backdrop, which the window's glass has capped in Dark so white text keeps its contrast (Light's tint
    // alone keeps black text at 4.5:1 over black, and lands at the Mac's 235-240 over its light window); a
    // near-black hairline outermost; a light rim a point inside it.
    internal static SwiftColor Tint { get; } = SwiftColor.Dynamic(new SwiftColor(white: 1, opacity: 0.50), new SwiftColor(white: 1, opacity: 0.10));

    internal static SwiftColor Edge { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.28), new SwiftColor(white: 0, opacity: 0.80));

    internal static SwiftColor Rim { get; } = SwiftColor.Dynamic(new SwiftColor(white: 1, opacity: 0.20), new SwiftColor(white: 1, opacity: 0.20));

    private static readonly ConditionalWeakTable<Popup, object> adopted = new();

    /// <summary>The style class of a presented popover that hangs on its tipped card (OD-0095): its top-level is clear.</summary>
    internal const string TippedClass = "marrow-popover-tipped";

    /// <summary>
    /// A popup the source presents as a sheet (SwiftUI <c>.sheet</c>, ChatView.swift:1456,1489), not as a popover: it keeps
    /// ED-238's panel on its top-level and gets no tip (OD-0095 gives the tip only to what the source shows as an NSPopover).
    /// </summary>
    public static readonly AttachedProperty<bool> IsSheetProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("IsSheet", typeof(PopoverHost));

    public static bool GetIsSheet(Popup popup) => popup.GetValue(IsSheetProperty);

    public static void SetIsSheet(Popup popup, bool value) => popup.SetValue(IsSheetProperty, value);

    /// <summary>The site's own content of a presented popup: the card's child once it hangs on one.</summary>
    public static Control? Content(Popup popup) => popup.Child is PopoverCard card ? card.Child : popup.Child;

    /// <summary>
    /// What the card adds round a popover's content, for a site that sizes its popup to its content (PromptEditor's
    /// resize): the edge, the tip with its gap and the room the card keeps for its shadow; nothing for a sheet.
    /// </summary>
    public static Size Room(Popup popup)
    {
        if (Facing(popup) is not { } edge) return default;
        var (inside, shadow) = (PopoverCard.RoomAround(edge), PopoverCard.ShadowMargin(edge));
        return new Size(inside.Width + shadow.Left + shadow.Right, inside.Height + shadow.Top + shadow.Bottom);
    }

    /// <summary>Readies <paramref name="popup"/> to be presented as a popover: the panel class, its card (OD-0095), and its
    /// anchor as its logical parent until it closes (so it, and the top-level it opens, inherit the anchor's appearance).</summary>
    internal static void Prepare(Popup popup)
    {
        if (!popup.Classes.Contains(PopoverClass)) popup.Classes.Add(PopoverClass);
        // A popup presented with no child, whose site builds its content as it opens (AboutUpdateViews' release-note
        // sections, HydraSettingsPage's cookbook and pair editor set popup.Child in their own Opened handlers, which run
        // before Follow's): Avalonia 12.1.2's Popup.Open hands its host that null child, and content set while a popup is
        // open reaches the host only through the child it replaces (Follow; Popup.Host is internal), so the first showing was
        // an empty, clear window. It opens with an empty placeholder instead, which the site's content then replaces.
        if (popup.Child is null && !popup.IsOpen) popup.Child = new PendingContent();
        var tipped = Facing(popup) is not null;
        popup.Classes.Set(TippedClass, tipped);
        if (tipped) Wrap(popup);
        if (carded.TryAdd(popup, popup)) Follow(popup);
        if (popup.Parent is not null || popup.PlacementTarget is not { } anchor) return;
        ((ISetLogicalParent)popup).SetParent(anchor);
        adopted.Add(popup, popup);
        EventHandler<EventArgs>? release = null;
        release = (_, _) =>
        {
            popup.Closed -= release;
            if (adopted.Remove(popup) && popup.Parent is not null) ((ISetLogicalParent)popup).SetParent(null);
        };
        popup.Closed += release;
    }

    private static readonly ConditionalWeakTable<Popup, object> carded = new();

    /// <summary>What a popup presented with no child shows until its site sets its content (Prepare): nothing, no size.</summary>
    internal sealed class PendingContent : Control
    {
    }

    // What a presented popup does while it is open, from the one time it is prepared (the popup keeps these handlers for its
    // life; each acts only while it is open).
    private static void Follow(Popup popup)
    {
        // Its host: the top-level it opens in (a PopupRoot, or the overlay's host, which is no window of its own).
        Control? host = null;
        // A press in the popover's own top-level but outside the card's outline (beside the tip, in the gap, in the
        // shadow's room) is a press outside the popover. A native popup window answers the hit test itself (Track: the press
        // reaches the window under it, and a light-dismissed popover closes as for any press outside); a press that does
        // arrive here (the overlay's host) light-dismisses it, as Avalonia does for a press outside the top-level.
        void PressBeside(object? sender, PointerPressedEventArgs e)
        {
            if (!popup.IsOpen || !(popup.IsLightDismissEnabled || popup.GetValue(Presentation.MotionProperty) is not null) || popup.Child is not PopoverCard card) return;
            if (card.HitTest(e.GetPosition(card))) return;
            if (popup.GetValue(Presentation.MotionProperty) is not null) Presentation.DismissMotion(popup);
            else popup.IsOpen = false;
            e.Handled = true;
        }
        void Detach()
        {
            host?.RemoveHandler(InputElement.PointerPressedEvent, PressBeside);
            host = null;
            Untrack(popup);
        }
        // A site that sets its content while the popover shows has it shown (PlainControls' deferred content, built once the
        // popup has opened; a popover shown again in place, or while it shrinks away; content refreshed under a presented
        // panel) and hung on the card too. Avalonia 12.1.2 gives a Popup's child to its host once, when it opens (Popup.Open:
        // popupHost.SetChild(Child)); Popup.ChildChanged only moves the child in the logical tree, so a child set while the
        // popup was open was never shown. Only while it is open: a closed popup is wrapped when it opens again, so a site may
        // handle its own content in between (PromptEditor measures it in a window of its own).
        popup.PropertyChanged += (_, change) =>
        {
            if (change.Property != Popup.ChildProperty || !popup.IsOpen) return;
            // The child it replaces is the one the host shows (before the popup's Opened handlers have all run, the host is
            // not known from the new one, which is in no tree yet).
            if (host is null && change.OldValue is Visual shown) host = HostOf(shown);
            if (Facing(popup) is not null) Wrap(popup);
            // What Popup.Open's SetChild does: both hosts (PopupRoot, OverlayPopupHost) show their Content. A child taken away
            // for a while (a site measures it elsewhere) leaves the host showing what it shows.
            if (popup.Child is { } child && host is ContentControl hosted && !ReferenceEquals(hosted.Content, child)) hosted.Content = child;
        };
        // The window the popover hangs over keeps the keyboard (a native popup window is not activated: Windows sends every key to the window under it), so the keys a popover
        // owns reach it from there: Escape closes it and Return is its default button (see ForwardKey).
        Action? release = null;
        void Release()
        {
            release?.Invoke();
            release = null;
        }
        popup.Opened += (_, _) =>
        {
            var found = host ?? HostOf(popup.Child);
            Detach();
            host = found;
            host?.AddHandler(InputElement.PointerPressedEvent, PressBeside, RoutingStrategies.Tunnel, handledEventsToo: true);
            Track(popup, host);
            if (popup.Child is PopoverCard card) card.Shown(host); // its window's see-through glass
            Release();
            if (popup.PlacementTarget is { } target && WindowTransients.WindowOf(target) is { } owner) release = Listen(popup, owner, () => popup.IsOpen);
        };
        popup.Closed += (_, _) =>
        {
            Detach();
            Release();
            if (popup.Child is PopoverCard card) card.Hidden();
        };
    }

    // Every control from the button up to the popover's content is visible: a part the popover itself collapses (a page of a pager, a folded section) hides its buttons, and a popup that
    // is not in a tree yet reports each control's own IsVisible only.
    private static bool ShownIn(Visual button, Visual content)
    {
        for (var at = button; at is not null; at = at.GetVisualParent())
        {
            if (!at.IsVisible) return false;
            if (ReferenceEquals(at, content)) break;
        }
        return true;
    }

    /// <summary>
    /// Hands the keys of <paramref name="owner"/> to <paramref name="popup"/> while <paramref name="isOpen"/> says it is open (the popup's own IsOpen; a test that cannot open a Popup
    /// says it). THE ONE RULE for a key the host forwards (PR-CHAT-110 review): the host marks it handled when it acts, and a site's own window-level key monitor for the same
    /// popover (the sidebar's archive popover, LinkPopover, the badge popovers) ignores a key that is already handled, so a key acts once whichever handler the window offers it to
    /// first. Returns the call that stops listening.
    /// </summary>
    internal static Action Listen(Popup popup, TopLevel owner, Func<bool> isOpen)
    {
        void OwnerKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.Handled && isOpen() && ForwardKey(popup, owner, e.Key, e.KeyModifiers)) e.Handled = true;
        }
        owner.AddHandler(InputElement.KeyDownEvent, OwnerKeyDown, RoutingStrategies.Tunnel);
        return () => owner.RemoveHandler(InputElement.KeyDownEvent, OwnerKeyDown);
    }

    /// <summary>
    /// A key pressed in the window <paramref name="owner"/> a popover hangs over while it is open. An NSPopover is a key window of its own, so Escape closes it and
    /// `.keyboardShortcut(.defaultAction)` (TimelineRows.swift:2791, HydraIntroPopover, SidebarView, QuestionTab) makes Return press its default button wherever the pointer
    /// went. A popup window on Windows is not activated, so those keys arrive at the owner instead and are handed on here: Escape to the newest transient over the window
    /// (Presentation.SetPresented registered how it closes), Return to the newest popover's enabled default button. True when the popover took the key.
    /// </summary>
    internal static bool ForwardKey(Popup popup, TopLevel owner, Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None) return false;
        if (key == Key.Escape) return WindowTransients.DismissNewest(owner);
        if (key != Key.Enter || !ReferenceEquals(WindowTransients.Newest(owner), popup) || Content(popup) is not { } content) return false;
        var button = content.GetSelfAndVisualDescendants().OfType<Button>().FirstOrDefault(candidate => candidate.IsDefault && candidate.IsEffectivelyVisible && candidate.IsEffectivelyEnabled && ShownIn(candidate, content));
        if (button is null) return false;
        (ControlAutomationPeer.CreatePeerForElement(button).GetProvider<IInvokeProvider>() ?? throw new InvalidOperationException("a Button has an invoke provider")).Invoke();
        return true;
    }

    // The top-level or overlay host a visual is shown in: a PopupRoot, or the overlay's host.
    private static Control? HostOf(Visual? shown) => shown?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(ancestor => ancestor is PopupRoot or OverlayPopupHost);

    // The card's clear surround is part of the popup's native window (the tip's band, the room for the shadow), and a native
    // popup window is a client area to Windows' own hit test: a press there goes to the popup window, not to the window under
    // it (Avalonia.Win32 12.1.2: PopupImpl answers WM_NCHITTEST only for a popup that is not hit-test visible at all, and
    // the extended client area's own hit test is for a Window). The sites that do not light-dismiss (PromptEditor,
    // DiffInspector, AttachmentPreviewPanel, ChatBadge) have their own monitors on the window under it, so a press within
    // 11 to 13 pt of the panel neither closed the popover nor reached them. The popup window answers WM_NCHITTEST itself
    // (a window procedure hook, which Avalonia offers for a window's own messages): outside the card's outline it answers
    // HTTRANSPARENT, so the press, and the pointer moving there, reach the window beneath (the same thread's window under
    // it), and the popover that light-dismisses closes as for any press outside it. UNVERIFIED natively (no headless test can
    // reach Win32's hit test): owed at the quiet-PC gate.
    internal const uint WmNcHitTest = 0x0084;

    internal const int HtTransparent = -1;

    private sealed record Hooked(Control Host, TopLevel? Window, Win32Properties.CustomWndProcHookCallback? Hook);

    private static readonly ConditionalWeakTable<Popup, Hooked> hooks = new();

    /// <summary>What puts a window procedure hook on a popup's own window: Avalonia's, which a test replaces.</summary>
    internal interface IWindowProcedureHooks
    {
        void Add(TopLevel window, Win32Properties.CustomWndProcHookCallback hook);

        void Remove(TopLevel window, Win32Properties.CustomWndProcHookCallback hook);
    }

    private sealed class AvaloniaWindowProcedureHooks : IWindowProcedureHooks
    {
        public void Add(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Win32Properties.AddWndProcHookCallback(window, hook);

        public void Remove(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Win32Properties.RemoveWndProcHookCallback(window, hook);
    }

    internal static Ambient<IWindowProcedureHooks> WindowProcedure { get; } = new(new AvaloniaWindowProcedureHooks());

    /// <summary>The popup's own window <paramref name="host"/> has just opened: its hit test is the card's outline (a host
    /// that is no window of its own, the overlay's, has none).</summary>
    internal static void Track(Popup popup, Control? host)
    {
        Untrack(popup);
        if (host is null) return;
        if (host is not TopLevel window)
        {
            hooks.Add(popup, new Hooked(host, null, null));
            return;
        }
        var hook = PassPressesBeside(() => popup.Child as PopoverCard, window);
        WindowProcedure.Current.Add(window, hook);
        hooks.Add(popup, new Hooked(host, window, hook));
    }

    /// <summary>The popup's window has closed.</summary>
    internal static void Untrack(Popup popup)
    {
        if (!hooks.TryGetValue(popup, out var tracked)) return;
        hooks.Remove(popup);
        if (tracked.Window is { } window && tracked.Hook is { } hook) WindowProcedure.Current.Remove(window, hook);
    }

    /// <summary>The top-level (or overlay host) the popup's card is shown in while the popup is open; null when it is not.</summary>
    internal static Control? Hosted(Popup popup) => hooks.TryGetValue(popup, out var tracked) ? tracked.Host : null;

    /// <summary>The screen point (physical pixels, signed: a monitor left of or above the primary one has negative ones) in a
    /// pointer message's lParam.</summary>
    internal static PixelPoint ScreenPoint(nint lParam)
    {
        var bits = (long)lParam;
        return new PixelPoint(unchecked((short)(bits & 0xFFFF)), unchecked((short)((bits >> 16) & 0xFFFF)));
    }

    /// <summary>The hook for the popup window <paramref name="window"/>: a hit test (WM_NCHITTEST) outside the card's outline is
    /// transparent to the window under it; every other message, and a hit on the card itself, is left to Avalonia.</summary>
    internal static Win32Properties.CustomWndProcHookCallback PassPressesBeside(Func<PopoverCard?> card, TopLevel window) =>
        (nint hWnd, uint message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message != WmNcHitTest || card() is not { } shown) return 0;
            if (window.TranslatePoint(window.PointToClient(ScreenPoint(lParam)), shown) is not { } inCard || shown.HitTest(inCard)) return 0;
            handled = true;
            return HtTransparent;
        };

    // OD-0095: the popover's content hangs on the panel's card, its tip on the edge facing the anchor (the side the
    // placement puts it on; the card turns it if the popup flips), so the panel's fill and edge are the card's one
    // outline and the top-level around it is clear. The card is made when its content is first hung on it; a popup shown
    // again while its card is still on screen (the presenter keeps a closing popover open for the 0.17 s it shrinks into its
    // tip; some sites re-show in place, or hide and show at once to move the popover to another control) keeps that card with
    // whatever flip and offset it has, so the card reads the popup's anchor and rect live, as it reads them after every
    // layout, and not the ones the popup had when the card was made.
    private static void Wrap(Popup popup)
    {
        if (popup.Child is null or PopoverCard || Facing(popup) is not { } edge) return;
        var content = popup.Child;
        popup.Child = null;
        var card = new PopoverCard { Child = content, TipEdge = edge, AnchorSource = () => popup.PlacementTarget, AnchorRect = () => popup.PlacementRect, ShadowRoom = true };
        card.Bind(PopoverCard.BackgroundProperty, new DynamicResourceExtension(TintKey));
        card.Bind(PopoverCard.BorderBrushProperty, new DynamicResourceExtension(EdgeKey));
        card.Bind(PopoverCard.RimBrushProperty, new DynamicResourceExtension(RimKey));
        card.Bind(PopoverCard.FallbackBrushProperty, new DynamicResourceExtension(FillKey));
        popup.Child = card;
    }

    // The card edge facing the anchor for the placement asked for: above the anchor the tip is at the bottom, and so on.
    // None for a sheet, and none for a popup centred on its target (it faces no edge of it).
    private static PopoverTipEdge? Facing(Popup popup) => GetIsSheet(popup) ? null : popup.Placement switch
    {
        PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight => PopoverTipEdge.Bottom,
        PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom => PopoverTipEdge.Right,
        PlacementMode.Right or PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom => PopoverTipEdge.Left,
        PlacementMode.Center => null,
        PlacementMode.AnchorAndGravity => popup.PlacementGravity switch
        {
            PopupGravity.Top or PopupGravity.TopLeft or PopupGravity.TopRight => PopoverTipEdge.Bottom,
            PopupGravity.Left => PopoverTipEdge.Right,
            PopupGravity.Right => PopoverTipEdge.Left,
            _ => PopoverTipEdge.Top,
        },
        _ => PopoverTipEdge.Top,
    };

    /// <summary>Puts the panel style and its two colours in the application's resources (PortControlThemes.Register).</summary>
    internal static void Register(Application application)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!application.Resources.ThemeDictionaries.TryGetValue(variant, out var provider))
                application.Resources.ThemeDictionaries[variant] = provider = new ResourceDictionary();
            var dictionary = (IResourceDictionary)provider;
            dictionary[FillKey] = new SolidColorBrush(Fill.Resolve(variant));
            dictionary[BorderKey] = new SolidColorBrush(Border.Resolve(variant));
            dictionary[TintKey] = new SolidColorBrush(Tint.Resolve(variant));
            dictionary[EdgeKey] = new SolidColorBrush(Edge.Resolve(variant));
            dictionary[RimKey] = new SolidColorBrush(Rim.Resolve(variant));
        }
        // ED-238: a presented popup's top-level wears the panel (a sheet, which has no tip).
        application.Styles.Add(new Style(selector => selector.OfType<Popup>().Class(PopoverClass).Not(x => x.Class(TippedClass)).Child().OfType<PopupRoot>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(FillKey)),
                new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(BorderKey)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        });
        application.Styles.Add(new Style(selector => selector.OfType<Popup>().Class(PopoverClass).Class(TippedClass).Child().OfType<PopupRoot>())
        {
            Setters =
            {
                // OD-0095: the panel is the card's (PopoverCard); the top-level is clear around its tip.
                // No fill at all, not Transparent (which is drawn, so hit-tested): a press beside the tip finds nothing of
                // the popover (PopoverCard.HitTest) and light-dismisses it (PressBeside); in a native popup window the hit
                // test itself passes it to the window under the popup (Track, PassPressesBeside).
                new Setter(TemplatedControl.BackgroundProperty, null),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TopLevel.TransparencyLevelHintProperty, new[] { WindowTransparencyLevel.Transparent }),
            },
        });
    }
}

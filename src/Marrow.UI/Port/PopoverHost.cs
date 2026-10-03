using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

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
public static class PopoverHost
{
    /// <summary>The style class a presented popover's Popup carries; the panel style matches its PopupRoot through it.</summary>
    internal const string PopoverClass = "marrow-popover";

    internal const string FillKey = "MarrowPort.PopoverFill";

    internal const string BorderKey = "MarrowPort.PopoverBorder";

    // Panels / Dark and Light, macOS 27 UI Kit.
    internal static SwiftColor Fill { get; } = SwiftColor.Dynamic(SwiftColor.White, SwiftColor.Rgb(0x1E1E1E));

    internal static SwiftColor Border { get; } = SwiftColor.Dynamic(SwiftColor.Clear, new SwiftColor(white: 1, opacity: 0.2));

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
        var tipped = Facing(popup) is not null;
        popup.Classes.Set(TippedClass, tipped);
        if (tipped)
        {
            Wrap(popup);
            // A site that swaps its content while the popover shows (PlainControls' deferred content, built once it has
            // opened) has the new content hung on the card too. Only while it is open: a closed popup is wrapped when it
            // opens again, so a site may handle its own content in between (PromptEditor measures it in a window of its own).
            if (carded.TryAdd(popup, popup))
            {
                popup.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Popup.ChildProperty && popup.IsOpen && Facing(popup) is not null) Wrap(popup);
                };
                // A press in the popover's own top-level but outside the card's outline (beside the tip, in the gap, in the
                // shadow's room) is a press outside the popover: it light-dismisses it, as Avalonia does for a press
                // outside the top-level (a native press there reaching the window under it is owed to the native gate).
                Control? hooked = null;
                void PressBeside(object? sender, PointerPressedEventArgs e)
                {
                    if (!popup.IsOpen || !popup.IsLightDismissEnabled || popup.Child is not PopoverCard card) return;
                    if (card.HitTest(e.GetPosition(card))) return;
                    popup.IsOpen = false;
                    e.Handled = true;
                }
                popup.Opened += (_, _) =>
                {
                    hooked?.RemoveHandler(InputElement.PointerPressedEvent, PressBeside);
                    hooked = (popup.Child as Visual)?.GetVisualAncestors().OfType<Control>().FirstOrDefault(ancestor => ancestor is PopupRoot or OverlayPopupHost); // its top-level (a PopupRoot, or the overlay's host)
                    hooked?.AddHandler(InputElement.PointerPressedEvent, PressBeside, RoutingStrategies.Tunnel, handledEventsToo: true);
                };
                popup.Closed += (_, _) =>
                {
                    hooked?.RemoveHandler(InputElement.PointerPressedEvent, PressBeside);
                    hooked = null;
                };
            }
        }
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

    // OD-0095: the popover's content hangs on the panel's card, its tip on the edge facing the anchor (the side the
    // placement puts it on; the card turns it if the popup flips), so the panel's fill and edge are the card's one
    // outline and the top-level around it is clear. A new card for each showing, so a flip of the last one is not kept.
    private static void Wrap(Popup popup)
    {
        if (popup.Child is null or PopoverCard || Facing(popup) is not { } edge) return;
        var content = popup.Child;
        popup.Child = null;
        var card = new PopoverCard { Child = content, TipEdge = edge, Anchor = popup.PlacementTarget, AnchorRect = () => popup.PlacementRect, ShadowRoom = true };
        card.Bind(PopoverCard.BackgroundProperty, new DynamicResourceExtension(FillKey));
        card.Bind(PopoverCard.BorderBrushProperty, new DynamicResourceExtension(BorderKey));
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
                // the popover (PopoverCard.HitTest) and light-dismisses it (PressBeside).
                new Setter(TemplatedControl.BackgroundProperty, null),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TopLevel.TransparencyLevelHintProperty, new[] { WindowTransparencyLevel.Transparent }),
            },
        });
    }
}

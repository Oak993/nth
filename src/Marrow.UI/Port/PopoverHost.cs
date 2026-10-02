using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

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
//      inside a clear surround (PortControlThemes.TipTemplate).
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

    /// <summary>Readies <paramref name="popup"/> to be presented as a popover: the panel class, and its anchor as its
    /// logical parent until it closes (so it, and the top-level it opens, inherit the anchor's appearance).</summary>
    internal static void Prepare(Popup popup)
    {
        if (!popup.Classes.Contains(PopoverClass)) popup.Classes.Add(PopoverClass);
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
        application.Styles.Add(new Style(selector => selector.OfType<Popup>().Class(PopoverClass).Child().OfType<PopupRoot>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(FillKey)),
                new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(BorderKey)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        });
    }
}

// PORT-ONLY. SwiftUI's `.buttonStyle(.bordered)` and `.buttonStyle(.borderedProminent)` are AppKit's system push buttons
// (a bezel; the prominent one filled with the accent). The app carries no theme package, so an Avalonia Button without a
// template shows its label and nothing else (probe 2026-10-01).
//
// OD-0042 ("Mac look, native menu") and OD-0045 ("Use Apple's design kits"): the macOS push button at
// `.controlSize(.regular)`, the size every site sets (QuestionTab.swift:165, SidebarView.swift:1018). Every value below
// is a SOURCE FACT (Apple design resource) from the "Apple macOS 27 UI Kit" (Sketch web share, Buttons / <Dark|Light> /
// Content Area / Bordered and Bordered Default / 3 Rg, Inspect panel, read 2026-10-02; E:\Droppy\work\apple-kit\
// macos27-uikit-controls.md): 24 high, padding 4 / 4 / 16 / 16, radius 6 (the kit's corners are Smooth "Apple (60%)";
// the port draws a circular arc, OD-0005), no border, the label SF Pro Medium 13 / 16 (the port's own font family, as
// everywhere). States are the kit's: Idle, Clicked, Disabled; there is no hover state. Replaces the Windows 11 buttons of
// the ED-229 proposal (OD-0042).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The bordered and bordered-prominent buttons (see the file note).</summary>
public static class SystemButtons
{
    /// <summary>Layer style Content Area / 01 - Bordered / 01 - Idle: Light #000000@0.08, Dark #FFFFFF@0.07.</summary>
    public static SwiftColor StandardFill { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.08), new SwiftColor(white: 1, opacity: 0.07));

    /// <summary>01 - Bordered / 03 - Clicked: #000000@0.16 / #FFFFFF@0.16.</summary>
    public static SwiftColor StandardFillPressed { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.16), new SwiftColor(white: 1, opacity: 0.16));

    /// <summary>01 - Bordered / 04 - Disabled: #000000@0.04 / #FFFFFF@0.04.</summary>
    public static SwiftColor StandardFillDisabled { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.04), new SwiftColor(white: 1, opacity: 0.04));

    /// <summary>Labels 1 Primary: Light #000000@0.85, Dark #FFFFFF@1.</summary>
    public static SwiftColor LabelPrimary { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.85), new SwiftColor(white: 1, opacity: 1));

    /// <summary>Labels 3 Tertiary: Light #000000@0.25, Dark #FFFFFF@0.25.</summary>
    public static SwiftColor LabelTertiary { get; } = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.25), new SwiftColor(white: 1, opacity: 0.25));

    /// <summary>04 - Prominent / 03 - Clicked: the fill under #000000@0.1 (Light, plus darker) / #FFFFFF@0.1 (Dark, plus lighter).</summary>
    private static readonly SwiftColor ProminentPress = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.1), new SwiftColor(white: 1, opacity: 0.1));

    /// <summary>3 Rg: radius 6.</summary>
    public const double Radius = 6;

    /// <summary>The focus ring style: a 3.5 pt band outside the control.</summary>
    public const double FocusBand = 3.5;

    /// <summary><c>Button(title, action:).buttonStyle(.bordered)</c>.</summary>
    public static Button Bordered(string title, Action action) => Make(Title(title), action, prominent: false);

    /// <summary><c>Button(title, action:).buttonStyle(.borderedProminent)</c>.</summary>
    public static Button BorderedProminent(string title, Action action) => Make(Title(title), action, prominent: true);

    /// <summary><c>Button(action:) { label }.buttonStyle(.bordered)</c> with a label view (a glyph).</summary>
    public static Button Bordered(Control label, Action action) => Make(label, action, prominent: false);

    // 3 Rg label: SF Pro Medium (500) 13 px, line height 16, centred.
    private static TextBlock Title(string title)
    {
        var label = new TextBlock { Text = title, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, LineHeight = 16 };
        SwiftFont.System(13, weight: SwiftFont.Weight.Medium).Apply(label);
        return label;
    }

    private static Button Make(Control label, Action action, bool prominent)
    {
        var button = new Button
        {
            Content = label,
            Template = new FuncControlTemplate<Button>((owner, scope) =>
            {
                var bezel = new Border
                {
                    Name = "PART_Bezel",
                    CornerRadius = new CornerRadius(Radius),
                    Padding = new Thickness(16, 4, 16, 4),
                };
                var presenter = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                }.RegisterInNameScope(scope);
                // The prominent button's Clicked state lays a wash over its fill and under its label (the whole bezel, out
                // past the padding); the bordered one swaps its fill instead.
                var press = new Border { Name = "PART_Press", CornerRadius = new CornerRadius(Radius), Margin = new Thickness(-16, -4, -16, -4), IsVisible = false, IsHitTestVisible = false };
                press.Background = ProminentPress.Brush(press);
                bezel.Child = new Panel { Children = { press, presenter } };
                // The focus ring: 3.5 px #0088FF@0.25 outside the control (the accent stands for System Blue, as it does
                // for the prominent fill), shown for keyboard focus only (:focus-visible, so a click does not show it).
                var ring = new Border
                {
                    Name = "PART_Focus",
                    Margin = new Thickness(-FocusBand),
                    CornerRadius = new CornerRadius(Radius + FocusBand),
                    BorderThickness = new Thickness(FocusBand),
                    IsVisible = false,
                    IsHitTestVisible = false,
                };
                ring.BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(ring);
                var root = new Panel { Children = { bezel, ring } };
                var idle = (prominent ? Marrow.UI.Chrome.Chrome.Accent : StandardFill).Brush(bezel);
                var pressed = StandardFillPressed.Brush(bezel);
                var disabled = StandardFillDisabled.Brush(bezel);
                var text = label as TextBlock;
                var primary = text is null ? null : (prominent ? SwiftColor.White : LabelPrimary).Brush(text);
                var tertiary = text is null || prominent ? null : LabelTertiary.Brush(text);
                // `.buttonBorderShape(.capsule)` restated on a presented tree (Popovers.swift:10, `presentedChrome()`): the button
                // takes the environment's border shape. ENGINEERING DECISION (the framework's documented behaviour for
                // bordered and prominent styles, OD-0098): a capsule's radius is half the button's height, for the bezel, the
                // press wash and the focus ring alike; the kit's radius 6 stands outside such a tree.
                void Shape()
                {
                    var capsule = Marrow.UI.Chrome.Popovers.GetIsCapsuleButtonBorderShape(owner) && bezel.Bounds.Height > 0;
                    var radius = capsule ? bezel.Bounds.Height / 2 : Radius;
                    bezel.CornerRadius = new CornerRadius(radius);
                    press.CornerRadius = new CornerRadius(radius);
                    ring.CornerRadius = new CornerRadius(radius + FocusBand);
                }
                bezel.PropertyChanged += (_, e) => { if (e.Property == Visual.BoundsProperty) Shape(); };
                owner.PropertyChanged += (_, e) => { if (e.Property == Marrow.UI.Chrome.Popovers.IsCapsuleButtonBorderShapeProperty) Shape(); };
                var drawnEnabled = true;
                void Paint()
                {
                    // Effective, so a button disabled by its parent shows its disabled look too; an Opacity the site sets on
                    // the button itself is kept.
                    var enabled = EnabledLook.Of(owner, ref drawnEnabled); // a column fading out of a chat switch keeps its look (ED-NEW-session-hotkeys)
                    var down = owner.IsPressed && enabled;
                    if (prominent)
                    {
                        // 04 - Disabled: the fill at 40 % layer opacity and the white label at 50 %.
                        bezel.Background = idle;
                        bezel.Opacity = enabled ? 1 : 0.4;
                        label.Opacity = enabled ? 1 : 0.5;
                        press.IsVisible = down;
                    }
                    else
                    {
                        bezel.Background = !enabled ? disabled : down ? pressed : idle;
                        if (text is not null) text.Foreground = enabled ? primary : tertiary;
                    }
                    ring.IsVisible = owner.Classes.Contains(":focus-visible");
                }
                owner.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Button.IsPressedProperty || EnabledLook.Changes(e.Property) ||
                        e.Property == Avalonia.Input.InputElement.IsFocusedProperty) Paint();
                };
                owner.Classes.CollectionChanged += (_, _) => Paint();
                Paint();
                return root;
            }),
        };
        if (label is TextBlock title) title.Foreground = (prominent ? SwiftColor.White : LabelPrimary).Brush(title);
        button.Click += (_, _) => action();
        return button;
    }
}

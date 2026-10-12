using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

/// <summary>
/// <c>Button(...).buttonStyle(.glass).controlSize(.small)</c> as the hosted Settings window draws it
/// (AboutUpdateViews.swift:826-833): the window root's <c>.tint(theme.spec.accent)</c> and
/// <c>.buttonBorderShape(.capsule)</c> (WindowManager.swift:162,170-191) make the glass button an accent-tinted capsule.
/// SOURCE FACT (reference video 1-settings-tour.mov, the Check now button, S 211.7-213.9, 2560x1440 at 1 px = 1 pt, measured
/// on the extracted frames): the capsule is 20 pt tall and about 78 pt wide with a white label (~11 pt) at rest, 20 pt
/// tall with the label grey on a grey translucent capsule while disabled ("Checking..."), the accent fill (247,46,120)
/// over a (52,59,60) backdrop on the recorded Monokai theme (accent F92672 = 249,38,114), lighter on hover (254,68,141) and on press.
/// ENGINEERING DECISION (ED-261): the accent glass is the port's own material (OD-0005) tinted with the
/// accent (Chrome.Accent, following the theme live) at full strength, so its interactive hover and press light shows on top of it;
/// the disabled capsule is the same material, unwashed since ED-356 (it was washed with the label colour at 11 % over the old dark veil). Side padding 10 pt (Apple kit small
/// row, E:\Droppy\work\apple-kit\macos27-uikit-controls.md:68), label 11 pt Medium.
/// </summary>
public static class SmallAccentGlassButton
{
    /// <summary>The capsule's height at <c>.controlSize(.small)</c> (video: 20 pt).</summary>
    public const double Height = 20;
    public const double HorizontalPadding = 10;
    public const double LabelSize = 11;
    /// <summary>ED-356: 0 (it was 0.11, set against the old material's dark veil, 18 % of #1E1E20): the material's own film now lifts the
    /// capsule to (82,90,91) over the reference backdrop, against the reference's (75,84,85); any wash on top would take it past.</summary>
    public const double DisabledWash = 0;
    public const double DisabledLabelOpacity = 0.30;
    /// <summary>White over the accent capsule under the pointer and while held: the reference's hover frame is (254,68,141) over a rest of (247,46,120); with the material's own pointer light on top, 8 % white reproduces the step and 13 % the press, within the tolerances of SmallAccentGlassButtonTests.</summary>
    public const double HoverLight = 0.08;
    public const double PressLight = 0.13;

    public static Control Create(string title, Action action, bool enabled)
    {
        var label = new TextBlock
        {
            Text = title,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.System(LabelSize, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = (enabled ? SwiftColor.White : Chrome.PrimaryText.Opacity(DisabledLabelOpacity)).Brush(label);

        var face = new Border { Height = Height, Padding = new Thickness(HorizontalPadding, 0), Child = label };
        face.Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(face, action);
        button.IsEnabled = enabled;
        AutomationProperties.SetName(button, title);
        if (enabled)
        {
            // The reference's hover and press frames (CATALOG hover.accent-pill-instant, press.accent-pill) lighten the whole capsule;
            // the material's own pointer light is a soft spot, so a white wash over the face carries the measured step, eased with
            // Chrome.hover (OD-0076: nothing cuts).
            var wash = new AnimatedColor(face);
            face.Background = wash.Brush;
            wash.Set(SwiftColor.White.Opacity(0), null);
            bool hovered = false, pressed = false;
            void Light() => wash.Set(SwiftColor.White.Opacity(pressed ? PressLight : hovered ? HoverLight : 0), Chrome.Hover);
            button.PointerEntered += (_, _) => { hovered = true; Light(); };
            button.PointerExited += (_, _) => { hovered = false; pressed = false; Light(); };
            button.PropertyChanged += (_, e) =>
            {
                if (e.Property != Button.IsPressedProperty) return;
                pressed = button.IsPressed;
                Light();
            };
        }

        // The material is drawn behind this border and shows its own tint (a border background would cover it).
        var fill = new Border { IsHitTestVisible = false, Background = Brushes.Transparent }; // transparent, not null: a control with nothing to draw gets no composition visual for the material to sit behind
        fill.Capsule(RoundedCornerStyle.Continuous);
        var capsule = GlassShape.Capsule(RoundedCornerStyle.Continuous);
        // OD-0147: a solid accent control keeps the release material its reference capture was matched with (UI-UPDATE-022); the approved canvas
        // has no solid control but Send, which keeps its own face (OD-0136)
        var glass = (enabled
            ? GlassEffect.Regular(capsule).Tint(Chrome.Accent, 1).Interactive()
            : GlassEffect.Regular(capsule).Tint(Chrome.PrimaryText, DisabledWash)).WithLook(GlassLook.Release);
        SwiftGlass.SetEffect(fill, glass);
        return new Panel { Children = { fill, button } };
    }
}

// PORT-ONLY, first-release UE-4 (OD-0100 place A, words U1; OD-0076; ED-320). The one button of the Updates page: "Check now", "Update everything", "Retry", "Restart Marrow", or while it works "Checking...",
// "Updating..." and "Restarting...". It is a 30 pt capsule of GlassMaterial in three layers that crossfade: the action state is glass tinted with the accent (the design's accent button), the quiet state
// ("Check now" when nothing is due) is plain glass, and the busy state is glass washed with the label colour and carries the spinner (the design's .ub, .ub.sec and .ub.busy). The label crossfades
// while the button's width eases to it, and the spinner's slot opens and shuts (.3 s ez). The press scale, the keyboard and the focus ring are CzButton's.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal enum UpdatesButtonMode
{
    /// <summary>The thing to press: accent glass, white label.</summary>
    Primary,

    /// <summary>Plain glass: Check now, when nothing is due.</summary>
    Quiet,

    /// <summary>Working: washed glass, a spinner, and a press that does nothing.</summary>
    Busy,
}

internal sealed class UpdatesMainButton : CzButton
{
    public const double ButtonHeight = 30;
    private const double HorizontalPadding = 15;
    private const double SpinnerSlot = 21;
    private const double BusyWash = .11;

    private readonly CzNumber primary, quiet, busy, slot;
    private readonly UpdatesFadeText label;
    private readonly Border slotBox;
    private readonly NthSpinner spinner;

    public UpdatesMainButton(string text, UpdatesButtonMode mode) : base(text)
    {
        ChromeFill = false;
        Background = null;
        Height = ButtonHeight;
        CornerRadius = new CornerRadius(ButtonHeight / 2);
        Padding = new Thickness(0);
        PressScale = .95;
        Mode = mode;

        PrimaryFill = Fill(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 1).Interactive());
        QuietFill = Fill(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        BusyFill = Fill(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.PrimaryText, BusyWash));
        primary = new CzNumber(Motion, mode == UpdatesButtonMode.Primary ? 1 : 0, v => PrimaryFill.Opacity = v);
        quiet = new CzNumber(Motion, mode == UpdatesButtonMode.Quiet ? 1 : 0, v => QuietFill.Opacity = v);
        busy = new CzNumber(Motion, mode == UpdatesButtonMode.Busy ? 1 : 0, v => BusyFill.Opacity = v);
        PrimaryFill.Opacity = primary.Value;
        QuietFill.Opacity = quiet.Value;
        BusyFill.Opacity = busy.Value;

        spinner = new NthSpinner { Width = UpdatesGlyph.Size, Height = UpdatesGlyph.Size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        slot = new CzNumber(Motion, mode == UpdatesButtonMode.Busy ? SpinnerSlot : 0, SetSlot);
        slotBox = new Border { ClipToBounds = true, Child = spinner, VerticalAlignment = VerticalAlignment.Center };
        SetSlot(slot.Value);
        label = new UpdatesFadeText(text, Ink(mode), 12.5, SwiftFont.Weight.Semibold) { VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(HorizontalPadding, 0), Children = { slotBox, label } };
        Child = new Grid { Children = { PrimaryFill, QuietFill, BusyFill, content } };
        AutomationProperties.SetName(this, text);
    }

    public UpdatesButtonMode Mode { get; private set; }

    /// <summary>The label now shown (the incoming one).</summary>
    public string Label => label.Text;

    /// <summary>The width the spinner's slot has opened to (0 to 21).</summary>
    public double SpinnerSlotWidth => slot.Value;

    public bool IsSpinning => spinner.IsVisible;

    public Control PrimaryFill { get; }

    public Control QuietFill { get; }

    public Control BusyFill { get; }

    public void Apply(string text, UpdatesButtonMode mode)
    {
        label.SetText(text, Ink(mode));
        AutomationProperties.SetName(this, text);
        if (mode == Mode) return;
        Mode = mode;
        primary.To(mode == UpdatesButtonMode.Primary ? 1 : 0, CzMotion.Ease(.3));
        quiet.To(mode == UpdatesButtonMode.Quiet ? 1 : 0, CzMotion.Ease(.3));
        busy.To(mode == UpdatesButtonMode.Busy ? 1 : 0, CzMotion.Ease(.3));
        if (mode == UpdatesButtonMode.Busy) spinner.IsVisible = true;
        slot.To(mode == UpdatesButtonMode.Busy ? SpinnerSlot : 0, CzMotion.Ez(.3));
    }

    private void SetSlot(double value)
    {
        slotBox.Width = value;
        spinner.Opacity = value / SpinnerSlot;
        if (value <= 0 && Mode != UpdatesButtonMode.Busy) spinner.IsVisible = false; // not asking for frames while shut
        else if (value > 0) spinner.IsVisible = true;
    }

    private static SwiftColor Ink(UpdatesButtonMode mode) => mode switch
    {
        UpdatesButtonMode.Primary => SwiftColor.White,
        UpdatesButtonMode.Quiet => Chrome.PrimaryText,
        _ => Chrome.SecondaryText,
    };

    // The material is drawn behind this border and shows its own tint (a border background would cover it); transparent, not null, so the control gets a composition visual for it.
    private static Border Fill(GlassEffect effect)
    {
        var fill = new Border { IsHitTestVisible = false, Background = Brushes.Transparent }.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(fill, effect);
        return fill;
    }
}

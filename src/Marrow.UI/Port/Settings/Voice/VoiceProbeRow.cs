// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0096; first-release voice typing, VT-14): one line of the hardware card's probe list (DESIGN gen.py:416-425 .hwi).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// `.hwi`: a 16 px icon cell, the key in a 104 px column (12.5 / 16, secondary) and the value (12.5 / 16, crossfading in when the probe has it), 8 px apart, 24 px high at least.
/// The icon is a 12 px spinner while that row's probe runs (opacity .8, scaling in from .6 over 0.3 s ez) and a check mark once it is done (0.2 s / 0.3 s ez). The row is dimmed to
/// .35 and sits 2 px low until it is <see cref="On"/>, then rises into place (0.3 s ease / 0.4 s ez).
/// </summary>
public sealed class VoiceProbeRow : Grid
{
    private readonly CzNumber opacity, shift, spinFade, spinScale;
    private readonly Nth.NthSpinner spinner = new() { Width = 12, Height = 12, Opacity = 0 }; // step 1 B at the probe row's 12
    private readonly CzCheckMark check = new() { Width = 12, Height = 12 };
    private readonly Panel icon = new() { Width = 12, Height = 12 };
    private bool on, busy, done;

    /// <summary>The key column's text (`Processor`, `Memory`, `Graphics`, `GPU backend`).</summary>
    public string Key { get; }

    /// <summary>The value, crossfading; empty until the probe has the row.</summary>
    public CzCrossfadeText Value { get; }

    public bool On => on;

    public bool IsBusy => busy;

    public bool IsDone => done;

    /// <summary>The row's opacity now (.35 to 1).</summary>
    public double RowOpacity => opacity.Value;

    /// <summary>How far down the row sits now, in points (2 to 0).</summary>
    public double RowShift => shift.Value;

    public double SpinnerOpacity => spinFade.Value;

    public double SpinnerScale => spinScale.Value;

    /// <summary>The check mark's opacity now.</summary>
    public double CheckOpacity => check.Opacity;

    public VoiceProbeRow(string key)
    {
        Key = key;
        ColumnDefinitions = new ColumnDefinitions("16,8,104,8,*");
        MinHeight = 24;
        VerticalAlignment = VerticalAlignment.Center;
        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        Opacity = .35;
        RenderTransform = new TranslateTransform(0, 2);
        var motion = new CzMotion(this);
        opacity = new CzNumber(motion, .35, v => Opacity = v);
        shift = new CzNumber(motion, 2, v => RenderTransform = new TranslateTransform(0, v));
        spinFade = new CzNumber(motion, 0, v => spinner.Opacity = v);
        spinScale = new CzNumber(motion, .6, v => spinner.RenderTransform = new ScaleTransform(v, v));
        spinner.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        spinner.RenderTransform = new ScaleTransform(.6, .6);
        check.Opacity = 0;
        icon.Children.Add(spinner);
        icon.Children.Add(check);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        check.Opacity = 0;
        var label = new TextBlock { Text = key, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        label.Foreground = CzTheme.Text(this, true);
        ActualThemeVariantChanged += (_, _) => label.Foreground = CzTheme.Text(this, true);
        Value = new CzCrossfadeText("") { FontSize = 12.5, Wrap = true, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(icon, 0);
        SetColumn(label, 2);
        SetColumn(Value, 4);
        Children.Add(icon);
        Children.Add(label);
        Children.Add(Value);
    }

    /// <summary>True once the list is open for a running or finished check: the row rises to full opacity.</summary>
    public void SetOn(bool value)
    {
        if (on == value) return;
        on = value;
        opacity.To(value ? 1 : .35, CzMotion.Ease(.3));
        shift.To(value ? 0 : 2, CzMotion.Ez(.4));
    }

    /// <summary>True while this row's probe runs: the spinner shows. A finished row shows the check mark instead.</summary>
    public void SetBusy(bool value)
    {
        if (busy == value) return;
        busy = value;
        spinFade.To(value ? .8 : 0, CzMotion.Ease(.2));
        spinScale.To(value ? 1 : .6, CzMotion.Ez(.3));
    }

    /// <summary>True once the probe has the row's value: the check mark shows.</summary>
    public void SetDone(bool value)
    {
        if (done == value) return;
        done = value;
        check.SetChecked(value);
    }

    /// <summary>The row as it was before any check: dim, low, nothing shown. Without a transition (a fresh start); not used by the card, which lets values and checks fade out.</summary>
    public void Reset()
    {
        on = busy = done = false;
        opacity.Jump(.35);
        shift.Jump(2);
        spinFade.Jump(0);
        spinScale.Jump(.6);
        check.SetChecked(false);
        Value.SetText("");
    }
}

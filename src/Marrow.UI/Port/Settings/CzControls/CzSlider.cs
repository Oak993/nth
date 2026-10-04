// PORT-ONLY CZ-1: exact slider mapping/snap/Auto, DESIGN cz.js:448-489, CZ-12.2.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Settings;
namespace Marrow.UI.Port.Settings;

public sealed class CzSlider : StackPanel
{
    public CzSliderRail Rail { get; }
    public CzAutoButton AutoButton { get; }
    public TextBlock ValueText { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public bool Inverted { get; }
    public double? Value { get; private set; }
    public double? AutoValue { get; private set; }
    public bool IsAuto => AutoValue.HasValue && !Value.HasValue;
    public double ShownValue => IsAuto ? AutoValue!.Value : Value!.Value;
    public double Fraction => Inverted ? 1 - Math.Clamp((ShownValue - Minimum) / (Maximum - Minimum), 0, 1) : Math.Clamp((ShownValue - Minimum) / (Maximum - Minimum), 0, 1);
    public Func<double, string> Format { get; }
    public event Action<double?>? ValueChanged;
    private bool dimmed;
    private readonly CzNumber opacity;
    public bool Dimmed
    {
        get => dimmed;
        set
        {
            dimmed = value; IsEnabled = !value; Rail.Focusable = !value; AutoButton.Focusable = !value && AutoValue.HasValue;
            AutoButton.IsEnabled = !value && AutoValue.HasValue; opacity.To(value ? .5 : 1, CzMotion.Ease(.3));
        }
    }
    public CzSlider(string title, double min, double max, double step, double? value, double? autoValue = null, bool inverted = false, Func<double, string>? format = null, string detail = "")
    {
        if (!(max > min) || !(step > 0) || (!value.HasValue && !autoValue.HasValue)) throw new ArgumentException("Slider needs a range, positive step, and a shown value.");
        Minimum = min; Maximum = max; Step = step; Value = value; AutoValue = autoValue; Inverted = inverted;
        Format = format ?? (v => CzText.Fmt(v, 4));
        Orientation = Orientation.Horizontal; Spacing = 6; HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, title); AutomationProperties.SetHelpText(this, detail);
        Rail = new CzSliderRail(this, title, detail);
        ValueText = new TextBlock { Width = 48, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11.5,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = CzTheme.Text(this, true) };
        ValueText.FontFeatures = new FontFeatureCollection { new FontFeature { Tag = "tnum", Value = 1 } };
        AutoButton = new CzAutoButton("Automatic " + title, IsAuto) { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(0) };
        AutoButton.Child = new TextBlock { Text = "Auto", FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11, FontWeight = FontWeight.Medium,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = CzTheme.Text(this) };
        AutoButton.Invoked += ToggleAuto;
        AutoButton.IsVisible = true; AutoButton.Opacity = autoValue.HasValue ? 1 : 0; AutoButton.IsEnabled = AutoButton.Focusable = autoValue.HasValue;
        Children.Add(Rail); Children.Add(ValueText); Children.Add(AutoButton);
        opacity = new CzNumber(new CzMotion(this), 1, v => Opacity = v); Refresh(false);
        ActualThemeVariantChanged += (_, _) => ValueText.Foreground = CzTheme.Text(this, true);
    }
    public void SetValue(double value, bool dragging = false)
    {
        if (Dimmed || !IsEffectivelyEnabled) return;
        Value = CzText.Snap(value, Minimum, Maximum, Step); Refresh(dragging); ValueChanged?.Invoke(Value);
    }
    public void Update(double? value, double? autoValue)
    {
        if (!value.HasValue && !autoValue.HasValue) throw new ArgumentException("No shown value.");
        Value = value; AutoValue = autoValue;
        AutoButton.Opacity = autoValue.HasValue ? 1 : 0; AutoButton.IsEnabled = AutoButton.Focusable = !Dimmed && autoValue.HasValue;
        Refresh(false);
    }
    public void ToggleAuto()
    {
        if (Dimmed || !AutoValue.HasValue) return;
        Value = IsAuto ? ShownValue : null; Refresh(false); ValueChanged?.Invoke(Value);
    }
    public void SetFromPointer(double x, double width)
    {
        var f = Math.Clamp((x - 9) / Math.Max(1, width - 18), 0, 1);
        if (Inverted) f = 1 - f; SetValue(Minimum + f * (Maximum - Minimum), true);
    }
    internal bool Key(Key key)
    {
        if (Dimmed) return false;
        var big = (Maximum - Minimum) / 10; var sign = Inverted ? -1 : 1;
        switch (key)
        {
            case Avalonia.Input.Key.Left: case Avalonia.Input.Key.Down: SetValue(ShownValue - sign * Step); break;
            case Avalonia.Input.Key.Right: case Avalonia.Input.Key.Up: SetValue(ShownValue + sign * Step); break;
            case Avalonia.Input.Key.PageDown: SetValue(ShownValue - sign * big); break;
            case Avalonia.Input.Key.PageUp: SetValue(ShownValue + sign * big); break;
            case Avalonia.Input.Key.Home: SetValue(Inverted ? Maximum : Minimum); break;
            case Avalonia.Input.Key.End: SetValue(Inverted ? Minimum : Maximum); break;
            default: return false;
        }
        return true;
    }
    private void Refresh(bool dragging)
    {
        ValueText.Text = Format(ShownValue); AutoButton.Checked = IsAuto; Rail.Move(Fraction, dragging);
    }
}

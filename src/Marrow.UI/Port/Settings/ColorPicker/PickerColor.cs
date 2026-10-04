namespace Marrow.UI.Port.Settings.ColorPicker;

/// <summary>The picker's colour as the design keeps it (lp-pickers.js): hue 0..360, saturation and value 0..1, alpha 0..1.</summary>
public readonly record struct PickerColor(double H, double S, double V, double A);

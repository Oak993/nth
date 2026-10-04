// OD-0099 Look Picks, Q-CZ-3 B: the hue and opacity strips as sliders (CZ-15: role slider, range, value text).
using System.Globalization;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Marrow.UI.Port.Settings.ColorPicker;

internal sealed class PickerRangePeer(PickerSurface owner) : ControlAutomationPeer(owner), IRangeValueProvider, IValueProvider
{
    private (double Minimum, double Maximum, double Small, double Large, double Value) Now => owner.Range!.Value;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    public bool IsReadOnly => false;
    public double Minimum => Now.Minimum;
    public double Maximum => Now.Maximum;
    public double SmallChange => Now.Small;
    public double LargeChange => Now.Large;
    public double Value => Now.Value;
    string? IValueProvider.Value => owner.ValueText;
    public void SetValue(double value) => owner.SetRange(value);
    void IValueProvider.SetValue(string? value) { if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) owner.SetRange(v); }
}

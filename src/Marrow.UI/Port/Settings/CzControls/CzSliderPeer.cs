// PORT-ONLY CZ-1: range/value text automation, CZ-12.2.8.
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzSliderPeer(CzSliderRail owner) : ControlAutomationPeer(owner), IRangeValueProvider, IValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    public bool IsReadOnly => owner.Slider.Dimmed;
    public double Minimum => owner.Slider.Minimum;
    public double Maximum => owner.Slider.Maximum;
    public double SmallChange => owner.Slider.Step;
    public double LargeChange => (Maximum - Minimum) / 10;
    public double Value => owner.Slider.ShownValue;
    string IValueProvider.Value => owner.Slider.Format(Value);
    public void SetValue(double value) => owner.Slider.SetValue(value);
    void IValueProvider.SetValue(string? value) => SetValue(double.Parse(value ?? throw new ArgumentNullException(nameof(value)), System.Globalization.CultureInfo.InvariantCulture));
}

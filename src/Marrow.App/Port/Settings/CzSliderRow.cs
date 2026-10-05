// PORT-ONLY CZ-4 (customization-SPEC CZ-12.2, CZ-13.5): a row holding a CzSlider with a value and an optional Auto capsule, wired to the setting it edits. The slider's own motion (CZ-1) eases the knob; this row
// forwards what the user does to the setting, takes back what the setting says (the value, the Auto value, the detail, whether the row is dimmed), and commits the write when a drag or a key is done.
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzSliderRow
{
    internal CzRow Row { get; }
    internal CzSlider Slider { get; }

    /// <param name="edited">The user moved the slider (a value) or switched Auto on (null).</param>
    internal CzSliderRow(CzLightEditor editor, string title, double min, double max, double step, bool inverted, Func<double, string> format, string detail,
        double? value, double? auto, Action<double?> edited)
    {
        Slider = new CzSlider(title, min, max, step, value, auto, inverted, format, detail);
        Row = new CzRow(title, detail, Slider, dimmed => Slider.Dimmed = dimmed);
        Slider.ValueChanged += edited;
        // a drag has ended, or a key has been pressed and lifted: the settings' coalesced write goes out now
        Slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => editor.Commit(), RoutingStrategies.Bubble, handledEventsToo: true);
        Slider.AddHandler(InputElement.KeyUpEvent, (_, _) => editor.Commit(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Shows what the setting says now.</summary>
    internal void Apply(double? value, double? auto, string detail, bool dimmed = false)
    {
        Slider.Update(value, auto); // also when the numbers are the same: the shown text may follow another setting (Colour drift's cap on Line)
        Row.SetDetail(detail);
        Row.Dimmed = dimmed;
    }
}

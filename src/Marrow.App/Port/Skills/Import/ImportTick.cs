// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The port's tick box as the Skills pages draw it (TermsBox's "I have read these terms"): a 15 point rounded square at the label colour's 0.12 with a check that
// eases in and out (OD-0076; under Reduce Motion it lands at once). A button, so the keyboard and the screen reader reach it.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportTick : Decorator
{
    private readonly Border box;
    private readonly Control mark;
    private readonly AnimatedScalar shown;
    private readonly Button button;
    private bool isChecked;

    public ImportTick(string name, string id, Action press)
    {
        mark = Pages.LibraryUi.Glyph("checkmark", 9, ChromeTokens.PrimaryText, SwiftFont.Weight.Bold);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        box = new Border { Width = 15, Height = 15, Child = mark, VerticalAlignment = VerticalAlignment.Center }.RoundedRectangle(4, RoundedCornerStyle.Continuous);
        box.Background = ChromeTokens.Overlay(0.12).Brush(box);
        shown = new AnimatedScalar(box, 0, value => mark.Opacity = value);
        mark.Opacity = 0;
        button = PlainControls.Button(box, press);
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        Pages.LibraryUi.Named(button, name, id);
        Child = button;
    }

    public bool Checked => isChecked;

    public Button Button => button;

    /// <summary>How far the check is drawn now (0 to 1).</summary>
    public double Drawn => shown.Value;

    public bool Enabled
    {
        get => button.IsEnabled;
        set { button.IsEnabled = value; box.Opacity = value ? 1 : 0.45; }
    }

    public void SetChecked(bool value)
    {
        if (isChecked == value) return;
        isChecked = value;
        shown.Set(value ? 1 : 0, Pages.LibraryUi.Ease(this, Pages.LibraryUi.Fade));
    }
}

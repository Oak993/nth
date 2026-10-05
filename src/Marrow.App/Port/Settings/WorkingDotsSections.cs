// PORT-ONLY CZ-5 (customization-SPEC CZ-10; OWNER OD-0089 amendment, OD-0099 Look picks; DESIGN cz.js:619-641): the Customization page's Working dots section: the Dot colour row (Accent, Model
// colour, Custom), the Custom dot colour row (the Classic and Signature swatches, folding open only for Custom) and the note. Every row reads and writes the app's workingDots setting;
// touching any control switches the preview to Running a turn and cancels the voice demo (CZ-10.2).
using Avalonia;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class WorkingDotsSections
{
    private static readonly string[] ModeIds = ["accent", "model", "custom"];

    private readonly Func<WorkingDotsSettings> read;
    private readonly Action<WorkingDotsSettings> write;
    private readonly Func<string> makerName;
    private bool showing;

    internal CzSection Section { get; }
    internal CzSegmentRow DotColour { get; }
    internal CzRowSlot CustomSlot { get; }
    internal WorkingDotsSwatches Swatches { get; }
    internal CzNoteRow Note { get; }

    /// <param name="read">The app's workingDots setting; read inside a tracked read by the page.</param>
    /// <param name="write">Puts a new workingDots setting in the app's settings.</param>
    /// <param name="makerName">The name of the preview's maker, for the Model colour detail.</param>
    /// <param name="touched">A control of the section was used: the preview goes to Running and the voice demo stops (CZ-10.2).</param>
    internal WorkingDotsSections(Func<WorkingDotsSettings> read, Action<WorkingDotsSettings> write, Func<string> makerName, Action touched)
    {
        this.read = read;
        this.write = write;
        this.makerName = makerName;
        var settings = read();
        DotColour = new CzSegmentRow(CzDotsText.DotColour, Detail(settings), [("accent", "Accent"), ("model", "Model colour"), ("custom", "Custom")], ModeIds[(int)settings.Mode],
            id => write(read() with { Mode = (WorkingDotsMode)Array.IndexOf(ModeIds, id) }));
        DotColour.Control.SelectionInvoked += _ => touched();
        Swatches = new WorkingDotsSwatches(settings.Custom);
        Swatches.Picked += id => { if (!showing) { touched(); write(read() with { Custom = id }); } };
        Swatches.Show(settings.Custom);
        // the Custom row has no visible title: its swatch lines are named for assistive technology
        var customRow = new Avalonia.Controls.Border { Padding = new Thickness(16, 2, Chrome.RowControlTrailingPadding, 11), Child = Swatches };
        Avalonia.Automation.AutomationProperties.SetName(customRow, CzDotsText.CustomDotColour);
        Note = new CzNoteRow(CzDotsText.Note);
        var rows = new CzRowStack();
        rows.Add(DotColour.Row);
        CustomSlot = rows.Add(customRow, open: settings.Mode == WorkingDotsMode.Custom);
        rows.Add(Note);
        Section = new CzSection(CzDotsText.Heading, rows, open: true);
    }

    /// <summary>Shows what the setting (and the preview's maker) say now, in every row. A change from elsewhere touches nothing of the preview.</summary>
    internal void Apply()
    {
        var settings = read();
        showing = true;
        try
        {
            DotColour.Apply(ModeIds[(int)settings.Mode], Detail(settings));
            Swatches.Show(settings.Custom);
        }
        finally { showing = false; }
        CustomSlot.Open = settings.Mode == WorkingDotsMode.Custom;
    }

    private string Detail(WorkingDotsSettings settings) =>
        CzDotsText.Detail(settings.Mode, makerName(), AccentPalette.All[Math.Max(0, AccentPalette.IndexOf(settings.Custom))].Name);
}

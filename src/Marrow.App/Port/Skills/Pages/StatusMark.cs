// PORT-ONLY: first-release feature unit SK-15 (DL-17). The end of a row or of a line of the dialog: a size or a word, a ring with the percent, a spinner, the green check, the update arrow or the warning, each with an
// optional word beside it. A change of what it shows crossfades and the box eases to the new width (OD-0076); a ring only moves its arc.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class StatusMark : Decorator
{
    internal enum Mark
    {
        Words,
        Ring,
        Spinner,
        Check,
        Update,
        Warning,
    }

    private readonly CrossfadeSlot slot = new();
    private (Mark Mark, string Label)? shown;
    private ProgressRing? ring;
    private TextBlock? percent;

    public StatusMark()
    {
        Child = slot;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>What the mark shows now.</summary>
    public Mark? Current => shown?.Mark;

    public string? Label => shown?.Label;

    public Control? Live => slot.Live;

    /// <summary>The ring's arc as drawn (0 to 1), when the mark is a ring.</summary>
    public double? RingValue => ring?.Value;

    public static Mark For(LibraryRow row) => row.State switch
    {
        ItemState.Available or ItemState.Queued => Mark.Words,
        ItemState.Downloading => Mark.Ring,
        ItemState.Checking or ItemState.Removing => Mark.Spinner,
        ItemState.Installed => Mark.Check,
        ItemState.UpdateAvailable => Mark.Update,
        _ => Mark.Warning,
    };

    /// <summary>A row: its size, "Waiting", the ring, the spinner, the check, the update arrow or the warning.</summary>
    public void Apply(LibraryRow row) =>
        Show(For(row), row.State switch
        {
            ItemState.Available => row.SizeMb is { } mb ? SizeText.Short(mb) : "",
            ItemState.Queued => LibraryWords.Waiting,
            _ => "",
        }, row.Progress, withWords: row.State is ItemState.Available or ItemState.Queued);

    /// <summary>
    /// Shows <paramref name="mark"/>. <paramref name="label"/> is the word of a Words mark and the word beside the glyph of the others when <paramref name="withWords"/> is set; a ring shows
    /// <paramref name="progress"/> as its arc and as its percent.
    /// </summary>
    public void Show(Mark mark, string label, double? progress, bool withWords = true)
    {
        var key = (mark, mark == Mark.Ring ? "" : withWords ? label : "");
        if (shown != key)
        {
            var first = shown is null;
            shown = key;
            ring = null;
            percent = null;
            slot.Show(Build(key.Item1, key.Item2, progress), first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (mark == Mark.Ring && ring is not null)
        {
            ring.Value = progress ?? 0;
            ring.IsDone = progress >= 1; // step 4: at 100 % the ring fills into the disc and its tick draws in, while the mark of the finished row takes over
            if (percent is not null) percent.Text = Percent(progress);
        }
    }

    private static string Percent(double? progress) => (int)Math.Floor((progress ?? 0) * 100) + "%";

    private Control Build(Mark mark, string label, double? progress)
    {
        if (mark == Mark.Words) return LibraryUi.Text(label, 11, secondary: true);
        Control glyph;
        switch (mark)
        {
            case Mark.Ring:
                ring = new ProgressRing();
                ring.Ring.Jump(progress ?? 0); // a row shown part way shows its arc in place
                percent = LibraryUi.Text(Percent(progress), 11, secondary: true);
                return Line(ring, percent);
            case Mark.Spinner:
                glyph = new NthSpinner { Width = 14, Height = 14 };
                break;
            case Mark.Check:
                glyph = LibraryUi.Glyph("checkmark.circle.fill", 13, ChromeTokens.Success);
                break;
            case Mark.Update:
                glyph = LibraryUi.Glyph("arrow.up.circle.fill", 13, ChromeTokens.Accent);
                break;
            default:
                glyph = LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning);
                break;
        }
        return label.Length == 0 ? glyph : Line(glyph, LibraryUi.Text(label, 11, secondary: true));
    }

    private static StackPanel Line(Control glyph, Control words)
    {
        glyph.VerticalAlignment = VerticalAlignment.Center;
        words.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { glyph, words } };
    }
}

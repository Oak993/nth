// PORT-ONLY: MA-3b-12, page out4 :4060-4077. Reuses the real sheet presenter with seam-provided record data.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
namespace Marrow.App.Port.MultiAccount.Cards;
internal sealed class LimitCardNote : StackPanel
{
    internal LimitCardNote(string note, MultiAccountCardContext? context)
    {
        Orientation = Orientation.Horizontal; Spacing = 7; this.Named(note, "limit-note");
        var waiting = note.StartsWith("Waiting for ", StringComparison.Ordinal);
        Children.Add(waiting ? new SystemSpinner { Width = 13, Height = 13 } : LibraryUi.Glyph("checkmark", 13, ChromeTokens.Accent));
        var link = waiting ? "Switch now" : "What was handed over";
        var suffix = " · " + link; var text = note.EndsWith(suffix, StringComparison.Ordinal) ? note[..^suffix.Length] : note;
        Children.Add(LibraryUi.Text(text, 12, secondary: true, wrap: true));
        Button button = null!; button = PlainControls.Button(LibraryUi.Text(link, 12), () => { if (waiting) Switch(); else Sheet(); });
        button.Focusable = true; button.Named(link, waiting ? "limit-switch-now" : "limit-handed-over"); button.IsEnabled = waiting ? context?.SwitchNow is not null : context?.HandedOver is not null;
        Children.Add(button);
        async void Switch() { if (context?.SwitchNow is not { } action || !button.IsEnabled) return; button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } }
        void Sheet()
        {
            if (context?.HandedOver?.Invoke() is not { } record || TopLevel.GetTopLevel(this) is null) return;
            ChatSheetPresentation.Show(this, dismiss => new HandedSheetLayer(new HandedSheetView(record, dismiss)), inWindow: true, still: LibraryUi.Still(this));
        }
    }
}

// PORT-ONLY: MA-3b-12, page out4 :4060-4077. Reuses the real sheet presenter with seam-provided record data.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Shared;
using Avalonia.Layout;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
namespace Marrow.App.Port.MultiAccount.Cards;
internal sealed class LimitCardNote : Decorator
{
    internal LimitCardNote(string note, MultiAccountCardContext? context)
    {
        HorizontalAlignment = HorizontalAlignment.Center; this.Named(note, "limit-note");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 7 };
        Child = new Border { Padding = new Thickness(11, 4), Background = CardColors.Tile.Brush(this), BorderBrush = CardColors.Rim.Brush(this), BorderThickness = new Thickness(.5), Child = row }.RoundedRectangle(999, RoundedCornerStyle.Circular);
        var waiting = note.StartsWith("Waiting for ", StringComparison.Ordinal);
        row.Children.Add(waiting ? new Marrow.UI.Port.Nth.NthSpinner { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center } : LibraryUi.Glyph("checkmark", 11, ChromeTokens.Accent));
        var link = waiting ? "Switch now" : "What was handed over";
        var suffix = " · " + link; var text = note.EndsWith(suffix, StringComparison.Ordinal) ? note[..^suffix.Length] : note;
        var words = MultiAccountText.Text(text, 11.5, secondary: !waiting, wrap: true); words.LineHeight = 16; Grid.SetColumn(words, 1); row.Children.Add(words);
        Button button = null!; button = PlainControls.Button(MultiAccountText.Text(link, 11.5), () => { if (waiting) Switch(); else Sheet(); });
        button.Focusable = true; button.Named(link, waiting ? "limit-switch-now" : "limit-handed-over"); button.IsEnabled = waiting ? context?.SwitchNow is not null : context?.HandedOver is not null;
        Grid.SetColumn(button, 2); row.Children.Add(button);
        async void Switch() { if (context?.SwitchNow is not { } action || !button.IsEnabled) return; button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } }
        void Sheet()
        {
            if (context?.HandedOver?.Invoke() is not { } record || TopLevel.GetTopLevel(this) is null) return;
            ChatSheetPresentation.Show(this, dismiss => new HandedSheetLayer(new HandedSheetView(record, dismiss)), inWindow: true, still: LibraryUi.Still(this));
        }
    }
}

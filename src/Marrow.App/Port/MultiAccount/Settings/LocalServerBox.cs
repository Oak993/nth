// PORT-ONLY: T11 step 2. "Add a local server" under the Local block, as the page draws it: a row that folds a box open (the address, Connect, one status line) and the page's plain note about local models. Connect looks for a server and says what it
// found ("Looking for a server…", then "Found a server on this PC · 1 model added"); the models it found appear in the block as rows. Nothing is downloaded: Marrow points to the server's own app (NOTES "Local LLM").
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class LocalServerBox : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly CzFoldRow fold;
    private readonly TextBox address;
    private readonly CzCrossfadeText status = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private bool busy;

    internal LocalServerBox(AccountsSettingsModel model)
    {
        this.model = model;
        var plus = LibraryUi.Glyph("plus", 12, ChromeTokens.Accent, SwiftFont.Weight.Medium);
        var text = LibraryUi.Text(MultiAccountWords.AddLocal, 13, weight: SwiftFont.Weight.Medium);
        text.Foreground = ChromeTokens.Accent.Brush(text);
        var add = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 10), Children = { plus, text } }, () => fold!.Open = !fold.Open);
        add.Focusable = true;
        add.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        add.Named(MultiAccountWords.AddLocal, "local-add");

        address = new TextBox { PlaceholderText = MultiAccountWords.LocalPlaceholder, MinWidth = 260, FontSize = 12.5, Padding = new Thickness(6, 4) };
        address.Named(MultiAccountWords.LocalAddress, "local-address");
        address.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Connect(); e.Handled = true; } };
        var connect = LibraryButtons.Make(MultiAccountWords.LocalConnect, Connect, ButtonLook.Primary, "local-connect");
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(connect, 1);
        line.Children.Add(address);
        line.Children.Add(connect);
        var note = LibraryUi.Text(MultiAccountWords.LocalNote, 12, secondary: true, wrap: true);
        fold = new CzFoldRow(new Border { Padding = new Thickness(16, 0, 16, 12), Child = new StackPanel { Spacing = 8, Children = { line, status, note } } }, false);
        Child = new StackPanel { Children = { add, fold } };
    }

    internal bool Open => fold.Open;

    internal string StatusShown => status.Text;

    internal TextBox Address => address;

    private async void Connect()
    {
        if (busy) return;
        busy = true;
        status.SetText(MultiAccountWords.LocalLooking);
        try
        {
            var message = await model.ConnectLocalAsync(address.Text ?? "");
            status.SetText(message);
            if (message.StartsWith("Found", StringComparison.Ordinal)) address.Text = "";
        }
        finally { busy = false; }
    }
}

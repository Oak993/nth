// PORT-ONLY: OD-0139 (ED-NEW-providersmerge-1). The box "Add another key" folds open in a service's card on Settings > Providers, in the look of "Add a local server" (LocalServerBox): the key, masked until its eye shows it,
// Add (or Return), one status line and the plain note on where the key is kept. A kept key becomes the last row of "Keys and order" and the box folds shut, empty; a key that was not kept says why and stays in the box.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class KeyAddBox : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly ProviderKind service;
    private readonly CzFoldRow fold;
    private readonly TextBox input;
    private readonly Border eyeLabel;
    private readonly Button eye;
    private readonly CzCrossfadeText status = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private bool busy;
    private bool shown;

    internal KeyAddBox(AccountsSettingsModel model, ProviderKind service)
    {
        this.model = model;
        this.service = service;
        input = new TextBox { PlaceholderText = MultiAccountWords.KeyPlaceholder, PasswordChar = '•', MinWidth = 220, FontSize = 12, Padding = new Thickness(6, 4) };
        SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(input);
        input.Named(MultiAccountWords.KeyInputLabel, "key-input-" + service.Id);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = Save(); e.Handled = true; } };
        eyeLabel = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 0) };
        eye = PlainControls.Button(eyeLabel, () => { shown = !shown; ShowEye(); });
        eye.Focusable = true;
        eye.Cursor = new Cursor(StandardCursorType.Hand);
        eye.VerticalAlignment = VerticalAlignment.Center;
        eye.Named(MultiAccountWords.ShowKey, "key-input-eye-" + service.Id);
        ShowEye();
        var add = LibraryButtons.Make(MultiAccountWords.KeySave, () => _ = Save(), ButtonLook.Primary, "key-save-" + service.Id);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(eye, 1);
        Grid.SetColumn(add, 2);
        line.Children.Add(input);
        line.Children.Add(eye);
        line.Children.Add(add);
        var note = LibraryUi.Text(MultiAccountWords.KeyNote, 12, secondary: true, wrap: true);
        fold = new CzFoldRow(new Border { Padding = new Thickness(16, 0, 16, 12), Child = new StackPanel { Spacing = 8, Children = { line, status, note } } }, false);
        Child = fold;
    }

    internal bool Open => fold.Open;

    internal string StatusShown => status.Text;

    internal TextBox Input => input;

    internal Button EyeButton => eye;

    /// <summary>"Add another key" folds the box open (the key box takes the focus) or shut.</summary>
    internal void Toggle()
    {
        fold.Open = !fold.Open;
        if (fold.Open) Avalonia.Threading.Dispatcher.UIThread.Post(() => input.Focus());
        else { input.Text = ""; status.SetText(""); }
    }

    private void ShowEye()
    {
        input.PasswordChar = shown ? '\0' : '•';
        eyeLabel.Child = LibraryUi.Glyph(shown ? "eye.slash" : "eye", 12, ChromeTokens.SecondaryText);
        var tip = shown ? MultiAccountWords.HideKey : MultiAccountWords.ShowKey;
        ToolTip.SetTip(eye, tip);
        AutomationProperties.SetName(eye, tip);
    }

    private async Task Save()
    {
        if (busy) return;
        busy = true;
        try
        {
            var problem = await model.AddKeyAsync(service, input.Text ?? "");
            if (problem is not null) { status.SetText(problem); return; }
            input.Text = "";
            status.SetText("");
            fold.Open = false;
            if (shown) { shown = false; ShowEye(); }
        }
        finally { busy = false; }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

// PORT-ONLY: web surfaces W10 (DESIGN.md 9 "Settings"; ED-NEW-web-w10-4). The rows the Visus and Tabula pages are made of, built from the
// Settings pages' own parts (ChromeSection, ChromeCard, ChromeRow, SettingsSwitch, ChromeSegmentedPicker, CzMenuButton, CzCapsuleButton), so
// the two pages stay small self-contained sections and carry nothing of the Settings window's own code. Every control has an accessible name,
// follows the setting when it changes from elsewhere (the row is tracked on what it reads), and writes only when the reader changes it.

namespace Marrow.App.Views.Settings;

internal static class WebSettingsRows
{
    /// <summary>A switch row.</summary>
    public static ChromeRow Toggle(string title, Func<bool> read, Action<bool> write, string? detail = null)
    {
        var toggle = new SettingsSwitch(new Binding<bool>(read, write));
        AutomationProperties.SetName(toggle, title);
        return new ChromeRow(title, () => toggle, detail);
    }

    /// <summary>A row with a strip of choices.</summary>
    public static ChromeRow Segments<T>(string title, (T Value, string Title)[] options, Func<T> read, Action<T> write, string? detail = null)
    {
        var picker = new ChromeSegmentedPicker<T>(options, new Binding<T>(read, write));
        AutomationProperties.SetName(picker, title);
        return new ChromeRow(title, () => picker, detail);
    }

    /// <summary>A row with a menu of choices; <paramref name="read"/> is followed when the setting changes elsewhere.</summary>
    public static ChromeRow Menu(string title, IReadOnlyList<(string Id, string Text)> options, Func<string> read, Action<string> write, string? detail = null)
    {
        var menu = new CzMenuButton(title, options, read());
        AutomationProperties.SetName(menu, title);
        menu.SelectionChanged += write;
        menu.Track(read, id => { if (menu.SelectedId != id && options.Any(o => o.Id == id)) menu.Show(id); });
        return new ChromeRow(title, () => menu, detail);
    }

    /// <summary>A row with one button (or several side by side).</summary>
    public static ChromeRow Buttons(string title, string? detail, params CzCapsuleButton[] buttons)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in buttons) line.Children.Add(button);
        return new ChromeRow(title, () => line, detail);
    }

    /// <summary>A capsule button with its name; null action dims it.</summary>
    public static CzCapsuleButton Capsule(string label, Action? run, string? name = null)
    {
        var button = new CzCapsuleButton(label);
        AutomationProperties.SetName(button, name ?? label);
        if (run is null) button.Dimmed = true;
        else button.Invoked += run;
        return button;
    }

    /// <summary>A quiet line of explanation or state inside a card.</summary>
    public static TextBlock Note(string text, bool wrap = true)
    {
        var line = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, Margin = new Thickness(16, 10) };
        SwiftFont.Footnote.Apply(line);
        line.Foreground = ChromeTokens.SecondaryText.Brush(line);
        return line;
    }

    /// <summary>A card of rows with a hairline between each.</summary>
    public static ChromeCard Card(params Control[] rows)
    {
        var panel = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) panel.Children.Add(new ChromeRowDivider());
            panel.Children.Add(rows[i]);
        }
        return new ChromeCard(() => panel);
    }

    /// <summary>A card whose rows are rebuilt on demand (a list that grows and shrinks).</summary>
    public static (ChromeCard Card, StackPanel Rows) ListCard()
    {
        var rows = new StackPanel();
        return (new ChromeCard(() => rows), rows);
    }

    /// <summary>A section: a heading over a card.</summary>
    public static ChromeSection Section(string title, Control card) => new(title, () => card);
}

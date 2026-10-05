// PORT-ONLY: first-release feature unit SK-15 (DL-17). The buttons of the pages, in the app's glass language: SwiftGlass's capsule (the Reveal and Show buttons of Settings) for a button, a plain glyph for the trash and the cross.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal enum ButtonLook
{
    /// <summary>A neutral glass capsule (Cancel, Close, Hide).</summary>
    Plain,

    /// <summary>The accent capsule with a white label (Continue, Download, Retry).</summary>
    Primary,

    /// <summary>A neutral glass capsule with an accent label (Pause, Resume, the Download all foot).</summary>
    Accent,

    /// <summary>A neutral glass capsule with a red label (Remove, Reset).</summary>
    Danger,
}

internal static class LibraryButtons
{
    public static Button Make(string text, Action action, ButtonLook look, string id)
    {
        var label = LibraryUi.Text(text, 12, weight: SwiftFont.Weight.Medium);
        var button = PlainControls.Button(label, action);
        if (look == ButtonLook.Primary) SwiftGlass.StyleAccentGlassButton(button, small: false);
        else
        {
            SwiftGlass.StyleButton(button);
            if (look == ButtonLook.Accent) label.Foreground = ChromeTokens.Accent.Brush(label);
            else if (look == ButtonLook.Danger) label.Foreground = ChromeTokens.Danger.Brush(label);
        }
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(text, id);
        return button;
    }

    /// <summary>The words on a button made by <see cref="Make"/>.</summary>
    public static string Words(Button button) => ((TextBlock)button.Content!).Text ?? "";

    public static void SetWords(Button button, string text)
    {
        ((TextBlock)button.Content!).Text = text;
        AutomationProperties.SetName(button, text);
    }

    /// <summary>A bare glyph that is a button (the trash on a row's detail, the cross of a cancel): 22 points square, brighter under the pointer.</summary>
    public static Button Glyph(string symbol, string name, string id, Action action, Func<Control, IBrush?>? tint = null)
    {
        var glyph = LibraryUi.Glyph(symbol, 12, ChromeTokens.SecondaryText, SwiftFont.Weight.Medium);
        var frame = new Border { Width = 22, Height = 22, Child = glyph, Background = Brushes.Transparent };
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var button = PlainControls.Button(frame, action);
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Opacity = 0.75;
        button.OnHover(over => button.Opacity = over ? 1 : 0.75);
        button.Named(name, id);
        return button;
    }
}

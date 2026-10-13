using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Marrow.Setup.Theme;

/// <summary>The window's kinds of text (the canvas's "Type" row).</summary>
public enum TextRole
{
    /// <summary>The caption's title: 12.</summary>
    Caption,

    /// <summary>A screen title: 20 semibold, Segoe UI Variable Display.</summary>
    Title,

    /// <summary>The hero title on Welcome and Ready: 28 semibold, Display.</summary>
    Hero,

    /// <summary>Body text and subtitles: 15.</summary>
    Body,

    /// <summary>A switch row's label: 15.</summary>
    Row,

    /// <summary>Welcome's three facts: 13.5.</summary>
    Facts,

    /// <summary>A second line under a subtitle (Ready's note, Problem's safe line): 14.</summary>
    Aside,

    /// <summary>Hints, the fine print and the Close-paused line: 12.5.</summary>
    Hint,

    /// <summary>Numbers (byte counts, percentages): Cascadia Mono 12, tabular figures.</summary>
    Numbers,

    /// <summary>A button's label: 15 semibold.</summary>
    Button,

    /// <summary>Welcome's version and size line: 13 (unit U2).</summary>
    Meta,

    /// <summary>A row's label in a glass group (Options, Uninstall): 15 on 21 (unit U2).</summary>
    RowLabel,

    /// <summary>A row's detail line: 12.5 on 17 (unit U2).</summary>
    RowHint,

    /// <summary>A notes card's heading ("What's new in 1.1.0", "Running in nth now"): 13 semibold (unit U2).</summary>
    NotesHeading,

    /// <summary>A notes card's lines: 14 on 21 (unit U2).</summary>
    Notes,

    /// <summary>The details log on Problem: Cascadia Mono 11 on 16 (unit U2).</summary>
    Log,

    /// <summary>An install path: Cascadia Mono 11.5 on the fine print's 18 (unit U2).</summary>
    Path,

    /// <summary>The uninstall warning: 13 on 19 (unit U2).</summary>
    Warning,

    /// <summary>A progress step's label: 15 (unit U2).</summary>
    Step,

    /// <summary>A small button's label (Options' Change…): 13.5 semibold (unit U2).</summary>
    SmallButton,
}

/// <summary>A text role at 100 % text size.</summary>
public sealed record TextStyle(double Size, double LineHeight, FontWeight Weight, string Family);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and the canvas's "Type" row; unit U1, ED-NEW-inst-u1-1). Type sizes are the canvas's
/// at 100 % Windows text size and grow with it (Settings › Accessibility › Text size, up to 225 %): sizes and line heights
/// are multiplied, spacing is not, as Windows does. Colours and sizes live in the window's resources, so a screen's text
/// follows a change of theme, contrast or text size while the window is open, without being built again.
/// </summary>
public static class SetupTheme
{
    public const string AppFamily = "Segoe UI Variable Text, Segoe UI";
    public const string DisplayFamily = "Segoe UI Variable Display, Segoe UI";
    public const string MonoFamily = "Cascadia Mono, Consolas";

    // Ink resource keys (brushes) and their colours (for colours mixed by animation).
    public const string Ink1 = "setup.ink1", Ink2 = "setup.ink2", Ink3 = "setup.ink3", Warn = "setup.warn", Bad = "setup.bad", Good = "setup.good";
    public const string Link = "setup.link", Focus = "setup.focus", Highlight = "setup.highlight", HighlightInk = "setup.highlightInk";

    public static string ColorKey(string ink) => ink + ".color";

    public static string SizeKey(TextRole role) => "setup.size." + Name(role);

    public static string LineKey(TextRole role) => "setup.line." + Name(role);

    public static TextStyle Style(TextRole role) => role switch
    {
        TextRole.Caption => new TextStyle(12, 16, FontWeight.Normal, AppFamily),
        TextRole.Title => new TextStyle(20, 28, FontWeight.SemiBold, DisplayFamily),
        TextRole.Hero => new TextStyle(28, 34, FontWeight.SemiBold, DisplayFamily),
        TextRole.Body => new TextStyle(15, 22, FontWeight.Normal, AppFamily),
        TextRole.Row => new TextStyle(15, 22, FontWeight.Normal, AppFamily),
        TextRole.Facts => new TextStyle(13.5, 20, FontWeight.Normal, AppFamily),
        TextRole.Aside => new TextStyle(14, 20, FontWeight.Normal, AppFamily),
        TextRole.Hint => new TextStyle(12.5, 18, FontWeight.Normal, AppFamily),
        TextRole.Numbers => new TextStyle(12, 16, FontWeight.Normal, MonoFamily),
        TextRole.Button => new TextStyle(15, 20, FontWeight.SemiBold, AppFamily),
        TextRole.Meta => new TextStyle(13, 19, FontWeight.Normal, AppFamily),
        TextRole.RowLabel => new TextStyle(15, 21, FontWeight.Normal, AppFamily),
        TextRole.RowHint => new TextStyle(12.5, 17, FontWeight.Normal, AppFamily),
        TextRole.NotesHeading => new TextStyle(13, 20, FontWeight.SemiBold, AppFamily),
        TextRole.Notes => new TextStyle(14, 21, FontWeight.Normal, AppFamily),
        TextRole.Log => new TextStyle(11, 16, FontWeight.Normal, MonoFamily),
        TextRole.Path => new TextStyle(11.5, 18, FontWeight.Normal, MonoFamily),
        TextRole.Warning => new TextStyle(13, 19, FontWeight.Normal, AppFamily),
        TextRole.Step => new TextStyle(15, 22, FontWeight.Normal, AppFamily),
        TextRole.SmallButton => new TextStyle(13.5, 18, FontWeight.SemiBold, AppFamily),
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Writes the palette's inks and the type sizes at <paramref name="textScale"/> into <paramref name="resources"/>.</summary>
    public static void Apply(IResourceDictionary resources, SetupPalette palette, double textScale)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(palette);
        Ink(resources, Ink1, palette.Ink1);
        Ink(resources, Ink2, palette.Ink2);
        Ink(resources, Ink3, palette.Ink3);
        Ink(resources, Warn, palette.Warn);
        Ink(resources, Bad, palette.Bad);
        Ink(resources, Good, palette.Good);
        Ink(resources, Link, palette.Link);
        Ink(resources, Focus, palette.Focus);
        Ink(resources, Highlight, palette.Highlight);
        Ink(resources, HighlightInk, palette.HighlightInk);
        foreach (var role in Enum.GetValues<TextRole>())
        {
            var style = Style(role);
            resources[SizeKey(role)] = style.Size * textScale;
            resources[LineKey(role)] = style.LineHeight * textScale;
        }
    }

    /// <summary>Gives <paramref name="block"/> a role's face and weight, and binds its size, line height and ink (unless null: the caller draws it) to the window's resources.</summary>
    public static T Text<T>(T block, TextRole role, string? ink = Ink1) where T : TextBlock
    {
        ArgumentNullException.ThrowIfNull(block);
        var style = Style(role);
        block.FontFamily = new FontFamily(style.Family);
        block.FontWeight = style.Weight;
        block.Bind(TextBlock.FontSizeProperty, block.GetResourceObservable(SizeKey(role)));
        block.Bind(TextBlock.LineHeightProperty, block.GetResourceObservable(LineKey(role)));
        if (ink is not null) block.Bind(TextElement.ForegroundProperty, block.GetResourceObservable(ink));
        return block;
    }

    /// <summary>Binds a colour property (for an ink mixed by animation) to an ink's colour in the window's resources.</summary>
    public static void BindColor(Control control, Avalonia.AvaloniaProperty<Color> property, string ink)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Bind(property, control.GetResourceObservable(ColorKey(ink)));
    }

    private static void Ink(IResourceDictionary resources, string key, Color color)
    {
        resources[key] = new ImmutableSolidColorBrush(color);
        resources[ColorKey(key)] = color;
    }

    private static string Name(TextRole role) => role switch
    {
        TextRole.Caption => "caption",
        TextRole.Title => "title",
        TextRole.Hero => "hero",
        TextRole.Body => "body",
        TextRole.Row => "row",
        TextRole.Facts => "facts",
        TextRole.Aside => "aside",
        TextRole.Hint => "hint",
        TextRole.Numbers => "numbers",
        TextRole.Button => "button",
        TextRole.Meta => "meta",
        TextRole.RowLabel => "rowLabel",
        TextRole.RowHint => "rowHint",
        TextRole.NotesHeading => "notesHeading",
        TextRole.Notes => "notes",
        TextRole.Log => "log",
        TextRole.Path => "path",
        TextRole.Warning => "warning",
        TextRole.Step => "step",
        TextRole.SmallButton => "smallButton",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, the canvas's type and blocks; unit U2, ED-NEW-inst-u2-1). The screens' lines:
/// the title (<c>.wt</c>, 20/28 semibold, 14 DIP above; the hero 28/34), the subtitle (<c>.ws</c>, 15/22, ink 2, 4 DIP above,
/// at most 420 DIP wide), the aside (<c>.ws.aside</c>, 14/20, ink 3, 6 DIP above), the meta line (13/19, ink 3), the fine
/// print (<c>.fine</c>, 12.5/18, ink 3, 12 DIP above, 4 under another), the warning (13/19, the bad ink, left-aligned in the
/// 420 DIP column). All centred and wrapping.
/// </summary>
public static class ScreenText
{
    public const double Column = 420;

    public static TextBlock Title(string words, bool hero = false) =>
        Line(words, hero ? TextRole.Hero : TextRole.Title, SetupTheme.Ink1, new Thickness(0, 14, 0, 0));

    public static TextBlock Subtitle(string words, double top = 4) =>
        Line(words, TextRole.Body, SetupTheme.Ink2, new Thickness(0, top, 0, 0), Column);

    public static TextBlock Aside(string words) =>
        Line(words, TextRole.Aside, SetupTheme.Ink3, new Thickness(0, 6, 0, 0), Column);

    public static TextBlock Meta(string words) =>
        Line(words, TextRole.Meta, SetupTheme.Ink3, new Thickness(0, 2, 0, 0));

    /// <summary>The block's inlines, emptied (its plain text cleared; the collection is made when it has none yet).</summary>
    public static InlineCollection Inlines(TextBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.Text = null;
        block.Inlines ??= new InlineCollection();
        block.Inlines.Clear();
        return block.Inlines;
    }

    public static TextBlock Fine(string words, double top = 12) =>
        Line(words, TextRole.Hint, SetupTheme.Ink3, new Thickness(0, top, 0, 0));

    public static TextBlock Warning(string words)
    {
        var block = Line(words, TextRole.Warning, SetupTheme.Bad, new Thickness(0, 10, 0, 0), Column);
        block.TextAlignment = TextAlignment.Left;
        block.HorizontalAlignment = HorizontalAlignment.Stretch;
        return block;
    }

    public static TextBlock Line(string words, TextRole role, string ink, Thickness margin, double maxWidth = double.PositiveInfinity) =>
        SetupTheme.Text(new TextBlock
        {
            Text = words,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = margin,
            MaxWidth = maxWidth,
        }, role, ink);

    /// <summary>A run in <paramref name="host"/>'s role sizes but another face (the monospace path).</summary>
    public static Run Run(TextBlock host, string words, TextRole role, string? ink = null, FontWeight? weight = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        var style = SetupTheme.Style(role);
        var run = new Run(words) { FontFamily = new FontFamily(style.Family), FontWeight = weight ?? style.Weight };
        run.Bind(TextElement.FontSizeProperty, host.GetResourceObservable(SetupTheme.SizeKey(role)));
        if (ink is not null) run.Bind(TextElement.ForegroundProperty, host.GetResourceObservable(ink));
        return run;
    }

    /// <summary>A link sitting in a line of text (it keeps the line's baseline).</summary>
    public static InlineUIContainer Inline(LinkButton link) => new(link) { BaselineAlignment = BaselineAlignment.Baseline };
}

/// <summary>
/// The canvas's <c>.facts</c>: a few short lines, each with its 15 DIP line icon (2.5 DIP down), 9 DIP from its text
/// (13.5/20, ink 2), 6 DIP apart, left-aligned in a block that is centred and at most 420 DIP wide, 18 DIP under the meta.
/// </summary>
public sealed class FactsList : StackPanel
{
    public FactsList()
    {
        Spacing = 6;
        Margin = new Thickness(0, 18, 0, 0);
        MaxWidth = ScreenText.Column;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    /// <summary>Adds a fact: its icon and its words.</summary>
    public FactsList Add(FactIcon.Kind icon, string words)
    {
        var text = SetupTheme.Text(new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap }, TextRole.Facts, SetupTheme.Ink2);
        var line = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var glyph = new FactIcon(icon) { Margin = new Thickness(0, 2.5, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        // the icon keeps its size and sits on the middle of the first line at every text size (2.5 DIP down a 20 DIP line)
        text.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.LineHeightProperty && text.LineHeight > 0) glyph.Margin = new Thickness(0, Math.Max(0, (text.LineHeight - 15) / 2), 9, 0);
        };
        DockPanel.SetDock(glyph, Dock.Left);
        line.Children.Add(glyph);
        line.Children.Add(text);
        Children.Add(line);
        return this;
    }

    /// <summary>Adds a control as a fact of its own (Welcome's signature line).</summary>
    public FactsList AddLine(Control line)
    {
        line.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(line);
        return this;
    }
}

/// <summary>The facts' 16-unit line icons (stroke 1.2 in the facts' ink), drawn 15 DIP.</summary>
public sealed class FactIcon : Control
{
    public enum Kind
    {
        Person,
        Package,
    }

    private static readonly Geometry Shoulders = Geometry.Parse("M2.8 14c.6-2.8 2.7-4.3 5.2-4.3s4.6 1.5 5.2 4.3");
    private static readonly Geometry Box = Geometry.Parse("M2.5 5.2L8 2.5l5.5 2.7v5.6L8 13.5l-5.5-2.7z");
    private static readonly Geometry BoxInside = Geometry.Parse("M2.5 5.2L8 7.9l5.5-2.7M8 7.9v5.6");
    private readonly Kind kind;
    private IBrush ink = Brushes.Gray;

    public FactIcon(Kind kind)
    {
        this.kind = kind;
        Width = 15;
        Height = 15;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        this.GetResourceObservable(SetupTheme.Ink2).Subscribe(new Observer(value =>
        {
            ink = value as IBrush ?? Brushes.Gray;
            InvalidateVisual();
        }));
    }

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(ink, 1.2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        using (context.PushTransform(Matrix.CreateScale(15 / 16.0, 15 / 16.0)))
        {
            if (kind == Kind.Person)
            {
                context.DrawEllipse(null, pen, new Point(8, 5.4), 2.6, 2.6);
                context.DrawGeometry(null, pen, Shoulders);
            }
            else
            {
                context.DrawGeometry(null, pen, Box);
                context.DrawGeometry(null, pen, BoxInside);
            }
        }
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(object? value) => next(value);
    }
}

/// <summary>
/// The canvas's glass group (<c>.group</c>): rows in one card, 420 DIP wide at most, 20 DIP under the subtitle, radius 10,
/// the card fill with a 1 DIP inner edge, each row after the first under a 1 DIP separator. The fill and the edge are drawn to
/// the card's <i>drawn</i> height (<see cref="HeightExtra"/>), so when a longer line makes the card taller or shorter at a
/// <see cref="Reveal"/> commit its edge glides with the rows instead of jumping (ED-NEW-inst-picks-1).
/// </summary>
public sealed class GlassGroup : Border, IGlidesHeight
{
    private readonly StackPanel rows = new();
    private readonly CardFill fill = new(10);
    private readonly CardEdge edge = new(10);
    private ScreenTokens tokens = ScreenTokens.Dark;
    private double extra;

    public GlassGroup()
    {
        Margin = new Thickness(0, 20, 0, 0);
        MaxWidth = ScreenText.Column;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        Child = new Panel { Children = { fill, rows, edge } };
        ScreenTokens.Watch(this, value => tokens = value);
    }

    /// <inheritdoc />
    public double HeightExtra
    {
        get => extra;
        set
        {
            if (extra == value) return;
            extra = value;
            fill.Extra = edge.Extra = value;
            ClipToBounds = value == 0; // the rows may be drawn below a card that has just got shorter
        }
    }

    /// <summary>The height the card's fill and edge are drawn to now (DIP).</summary>
    public double DrawnHeight => Math.Max(0, Bounds.Height + extra);

    public Controls Rows => rows.Children;

    /// <summary>The rows' panel, for Reveal to track (a row that a longer line above it moves glides there).</summary>
    public Panel RowsPanel => rows;

    public GlassGroup With(params Control[] children)
    {
        foreach (var child in children) rows.Children.Add(child);
        return this;
    }

    internal ScreenTokens Tokens => tokens;
}

/// <summary>A card's fill (the canvas's card colour), drawn behind its content to the card's drawn height and taking no place.</summary>
public sealed class CardFill : Control
{
    private readonly double radius;
    private IBrush? brush;
    private double extra;

    public CardFill(double radius)
    {
        this.radius = radius;
        IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ScreenTokens.Watch(this, value =>
        {
            brush = new ImmutableSolidColorBrush(value.Card);
            InvalidateVisual();
        });
    }

    /// <summary>The drawn height less the laid-out one (DIP).</summary>
    public double Extra
    {
        get => extra;
        set
        {
            if (extra == value) return;
            extra = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (brush is null) return;
        context.DrawRectangle(brush, null, new RoundedRect(new Rect(0, 0, Bounds.Width, Math.Max(0, Bounds.Height + extra)), radius));
    }
}

/// <summary>A card's 1 DIP inner edge (the canvas's inset shadow), drawn over its content and taking no place.</summary>
public sealed class CardEdge : Control
{
    private readonly double radius;
    private ScreenTokens tokens = ScreenTokens.Dark;
    private double extra;

    public CardEdge(double radius)
    {
        this.radius = radius;
        IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            InvalidateVisual();
        });
    }

    /// <summary>The drawn height less the laid-out one (DIP); 0 for a card whose edge is as laid out.</summary>
    public double Extra
    {
        get => extra;
        set
        {
            if (extra == value) return;
            extra = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context) =>
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(tokens.CardEdge), 1), new RoundedRect(new Rect(0, 0, Bounds.Width, Math.Max(0, Bounds.Height + extra)).Deflate(.5), radius - .5));
}

/// <summary>The 1 DIP separator over a glass row that follows another row.</summary>
public sealed class RowSeparator : Control
{
    private readonly Control row;
    private ScreenTokens tokens = ScreenTokens.Dark;

    public RowSeparator(Control row)
    {
        this.row = row;
        Height = 1;
        VerticalAlignment = VerticalAlignment.Top;
        IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            InvalidateVisual();
        });
    }

    /// <summary>True when the row follows another row in its group.</summary>
    public bool Shows => row.Parent is StackPanel stack && stack.Children.IndexOf(row) > 0;

    public override void Render(DrawingContext context)
    {
        if (Shows) context.DrawRectangle(new ImmutableSolidColorBrush(tokens.Separator), null, new Rect(0, 0, Bounds.Width, 1));
    }
}

/// <summary>
/// A row of a glass group (<c>.row</c>): at least 48 DIP, padding 8 14 8 16, its words (the label 15/21 in ink 1 and the
/// detail 12.5/17 in ink 3, or a monospace path) taking the room, and its control 14 DIP to the right, centred. A row after
/// the first draws its separator; <see cref="Below"/> puts content under the row inside the same row (Options' preview).
/// </summary>
public sealed class OptionRow : Border
{
    private readonly Grid line = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14, MinHeight = 32 };
    private readonly StackPanel words = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel whole = new();

    public OptionRow(string label, Control detail, Control? trailing)
    {
        ArgumentNullException.ThrowIfNull(detail);
        LabelBlock = SetupTheme.Text(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, TextRole.RowLabel, SetupTheme.Ink1);
        Detail = detail;
        words.Children.Add(LabelBlock);
        words.Children.Add(detail);
        line.Children.Add(words);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 1);
            line.Children.Add(trailing);
        }
        Trailing = trailing;
        whole.Children.Add(new Border { Padding = new Thickness(16, 8, 14, 8), MinHeight = 48, Child = line });
        Separator = new RowSeparator(this);
        Child = new Panel { Children = { whole, Separator } };
    }

    /// <summary>The separator drawn over the row when it follows another row.</summary>
    public RowSeparator Separator { get; }

    /// <summary>The detail line under the label (12.5/17, ink 3).</summary>
    public static TextBlock Hint(string words) =>
        SetupTheme.Text(new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap }, TextRole.RowHint, SetupTheme.Ink3);

    public TextBlock LabelBlock { get; }

    public Control Detail { get; }

    public Control? Trailing { get; }

    /// <summary>Puts <paramref name="content"/> under the row, inside it (padding 0 16 16), with the row's separator above the pair.</summary>
    public OptionRow Below(Control content)
    {
        whole.Children.Add(new Border { Padding = new Thickness(16, 0, 16, 16), Child = content });
        return this;
    }
}

/// <summary>
/// The canvas's notes card (<c>.notes</c>): what's new, or the threads running in nth. 420 DIP wide at most, 18 DIP under the
/// subtitle, radius 10, padding 12 16; a heading (13 semibold, ink 2, 6 DIP above the list) and bullet lines (14/21, ink 1,
/// indented 18 DIP).
/// </summary>
public sealed class NotesCard : Border
{
    private readonly StackPanel stack = new();

    public NotesCard(string heading, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Margin = new Thickness(0, 18, 0, 0);
        MaxWidth = ScreenText.Column;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        CornerRadius = new CornerRadius(10);
        stack.Margin = new Thickness(16, 12, 16, 12);
        Heading = SetupTheme.Text(new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }, TextRole.NotesHeading, SetupTheme.Ink2);
        stack.Children.Add(Heading);
        foreach (var words in lines) stack.Children.Add(Bullet(words));
        Child = new Panel { Children = { stack, new CardEdge(10) } };
        ScreenTokens.Watch(this, value =>
        {
            Background = new ImmutableSolidColorBrush(value.Card);
        });
    }

    public TextBlock Heading { get; }

    public IEnumerable<string> Lines => stack.Children.OfType<Grid>().Select(g => g.Children.OfType<TextBlock>().Last().Text ?? "");

    public void SetLines(string heading, IReadOnlyList<string> lines)
    {
        Heading.Text = heading;
        while (stack.Children.Count > 1) stack.Children.RemoveAt(1);
        foreach (var words in lines) stack.Children.Add(Bullet(words));
    }

    private static Grid Bullet(string words)
    {
        var dot = SetupTheme.Text(new TextBlock { Text = "•", TextAlignment = TextAlignment.Center }, TextRole.Notes, SetupTheme.Ink1);
        var text = SetupTheme.Text(new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap }, TextRole.Notes, SetupTheme.Ink1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
        AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        return grid;
    }
}

/// <summary>
/// Problem's details (<c>.log</c>): the log's last lines in monospace 11/16, ink 2, on the log fill with a 1 DIP edge,
/// radius 8, padding 10 12, 12 DIP under the lines above. It is always its full height (104 DIP) and scrolls inside, so
/// showing it never grows anything: it only fades. A polite live region.
/// </summary>
public sealed class LogBox : Border
{
    public const double FullHeight = 104;

    private readonly TextBlock text;

    public LogBox()
    {
        Margin = new Thickness(0, 12, 0, 0);
        MaxWidth = ScreenText.Column;
        Height = FullHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        CornerRadius = new CornerRadius(8);
        text = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 10, 12, 10) }, TextRole.Log, SetupTheme.Ink2);
        var scroll = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Child = new Panel { Children = { scroll, new CardEdge(8) } };
        AutomationProperties.SetName(this, "Details");
        Reveal.LiveRegion(this);
        ScreenTokens.Watch(this, value =>
        {
            Background = new ImmutableSolidColorBrush(value.LogFill);
        });
    }

    public TextBlock Text => text;
}

/// <summary>
/// Problem's mark badge (<c>.badge</c>): a 26 DIP amber disc with "!" at the mark's bottom right (6 DIP outside), ringed
/// 3 DIP in the window's tint, with a soft shadow. It is there with the screen (the canvas sets it with the mark, so its
/// transition never runs). A contrast theme drops the ring's shadow.
/// </summary>
public sealed class MarkBadge : Border
{
    public MarkBadge()
    {
        Width = 26;
        Height = 26;
        CornerRadius = new CornerRadius(13);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, -6, -6);
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(SetupPalette.Hex(0xf6c46f), 0), new GradientStop(SetupPalette.Hex(0xd89234), 1) },
        };
        Child = new TextBlock
        {
            Text = "!",
            FontFamily = new FontFamily(SetupTheme.AppFamily),
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            LineHeight = 15,
            Foreground = new ImmutableSolidColorBrush(SetupPalette.Hex(0x2a1a04)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ScreenTokens.Watch(this, value => BoxShadow = value.Contrast ? default : ShadowLayer.Many(new ShadowLayer(0, 0, 0, 3, Ring(this), false), new ShadowLayer(0, 4, 10, 0, SetupPalette.Rgba(0, 0, 0, .35))));
        AttachedToVisualTree += (_, _) => BoxShadow = ScreenTokens.Of(this).Contrast ? default : ShadowLayer.Many(new ShadowLayer(0, 0, 0, 3, Ring(this), false), new ShadowLayer(0, 4, 10, 0, SetupPalette.Rgba(0, 0, 0, .35)));
    }

    // the canvas's --win-tint2: the window's bottom tint
    private static Color Ring(Control control) =>
        TopLevel.GetTopLevel(control) is Shell.SetupWindow window ? window.Palette.TintBottom : SetupPalette.Rgba(22, 24, 29, .82);
}

// PORT-ONLY: multi-account step 3a (OD-0115, OD-0119). The cards of "Pill in the composer" (multi-account.html #czPick): one radio card per style with its live preview, name and first sentence, in the Customization page's own card manner (CzStyleCards);
// the glow card is wider and holds the two glow variants, the "Usage left" scrubber and the legend. One radio group across all twelve, so the arrow keys walk the cards and then the two variants.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Capsule;

/// <summary>One card of the grid as the tests and the section see it.</summary>
internal sealed record PillCard(string Id, bool HasDefaultTag);

internal sealed class PillStyleCards : CzRadioGroup
{
    private const double Gap = 8;
    private const double MinColumn = 160;
    private static readonly FontFamily Inter = new("fonts:Inter#Inter");

    private readonly List<Control> cells = [];
    private readonly List<Border> shells = [];
    private readonly List<CzNumber> rings = [];
    private readonly List<CzColor> fills = [];
    private readonly Border glowCard;
    private readonly Dictionary<string, PillPreview> previews = [];

    public PillStyleCards(string selected) : base("Pill in the composer", CapsuleStyles.Ids.Select(id => (id, CapsuleStyles.Name(id))).ToArray(), selected)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Clear();
        glowCard = new Border { Padding = new Thickness(8, 8, 8, 11), CornerRadius = new CornerRadius(12) };
        var glowStack = new StackPanel { Spacing = 3 };
        glowCard.Child = glowStack;
        var variants = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i];
            var preview = new PillPreview(button.Id);
            previews[button.Id] = preview;
            if (CapsuleStyles.IsGlow(button.Id))
            {
                BuildVariant(button, preview, variants, button.Id == "11" ? 0 : 1);
                continue;
            }
            BuildCard(button, preview);
            cells.Add(button);
        }
        glowStack.Children.Add(variants);
        glowStack.Children.Add(Title("Glow rim", false));
        glowStack.Children.Add(Line(CapsuleStyles.CardLine("11")));
        Scrubber = new UsageScrubber();
        glowStack.Children.Add(UsageRow(Scrubber));
        glowStack.Children.Add(Legend());
        cells.Add(glowCard);
        foreach (var cell in cells) Children.Add(cell);
        glowCard.Background = CzTheme.Brush(CzTheme.Token(this, "cardfill"));
        PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") Refresh(); };
        SelectionChanged += _ => Refresh();
        Refresh();
    }

    internal UsageScrubber Scrubber { get; }

    internal IReadOnlyList<PillCard> Cards { get; private set; } = [];

    /// <summary>The previews, in the styles' order (the ten cards, then the two glow variants).</summary>
    internal IReadOnlyList<AccountCapsule> Previews => CapsuleStyles.Ids.Select(id => previews[id].Capsule).ToArray();

    internal IReadOnlyList<AccountCapsule> GlowPreviews => [previews["11"].Capsule, previews["11o"].Capsule];

    private Control Title(string name, bool tag)
    {
        var text = new TextBlock { Text = name, FontFamily = Inter, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = CzTheme.Text(this), Margin = new Thickness(4, 5, 4, 0), LineHeight = 17 };
        if (!tag) return text;
        var dark = CzTheme.Dark(this);
        var good = dark ? Color.Parse("#6CCB5F") : Color.Parse("#0F7B3F");
        var soft = dark ? Color.FromArgb(36, 108, 203, 95) : Color.FromArgb(31, 15, 123, 63);
        var label = new TextBlock { Text = "Default", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(good), VerticalAlignment = VerticalAlignment.Center };
        var pill = new Border { Background = new SolidColorBrush(soft), CornerRadius = new CornerRadius(999), Padding = new Thickness(6, 1), Margin = new Thickness(4, 0, 0, 0), Child = label, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0), Children = { text, pill } };
    }

    private TextBlock Line(string text) => new()
    {
        Text = text, FontFamily = Inter, FontSize = 11, LineHeight = 15, Margin = new Thickness(4, 0), Foreground = CzTheme.Text(this, true), TextWrapping = TextWrapping.Wrap,
    };

    private void BuildCard(CzRadioButton button, PillPreview preview)
    {
        button.Height = double.NaN;
        button.Padding = new Thickness(0);
        button.PressScale = .98;
        button.CornerRadius = new CornerRadius(12);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(preview);
        stack.Children.Add(Title(CapsuleStyles.Name(button.Id), button.Id == CapsuleStyles.Default));
        stack.Children.Add(Line(CapsuleStyles.CardLine(button.Id)));
        var shell = new Border { Padding = new Thickness(8, 8, 8, 11), CornerRadius = new CornerRadius(12), Child = stack };
        button.Child = shell;
        shells.Add(shell);
        AutomationProperties.SetName(button, CapsuleStyles.Name(button.Id));
        AutomationProperties.SetHelpText(button, CapsuleStyles.Description(button.Id));
        var ring = new CzNumber(button.Motion, button.Picked ? 2 : 0, v => { shell.BorderThickness = new Thickness(v); shell.Padding = new Thickness(8 - v, 8 - v, 8 - v, 11 - v); });
        rings.Add(ring);
        ring.Jump(button.Picked ? 2 : 0);
        var colour = CzTheme.Token(this, button.Picked ? "tilesel" : "cardfill");
        shell.Background = CzTheme.Brush(colour);
        fills.Add(new CzColor(button.Motion, colour, c => shell.Background = CzTheme.Brush(c)));
        button.PointerEntered += (_, _) => Refresh();
        button.PointerExited += (_, _) => Refresh();
        cardsById[button.Id] = button;
    }

    private readonly Dictionary<string, CzRadioButton> cardsById = [];
    private readonly List<(CzRadioButton Button, Border Ring, TextBlock Label, CzNumber Fade)> variantRings = [];

    // a glow variant: its preview over its label, a 2 px ring round the preview when it is the pick
    private void BuildVariant(CzRadioButton button, PillPreview preview, Grid grid, int column)
    {
        button.Height = double.NaN;
        button.Padding = new Thickness(0);
        button.PressScale = .98;
        button.CornerRadius = new CornerRadius(9);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        var label = new TextBlock { Text = column == 0 ? "Usage light" : "Original colours", FontFamily = Inter, FontSize = 11, Foreground = CzTheme.Text(this, true), Margin = new Thickness(5, 0) };
        var ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), Height = 62, Margin = new Thickness(-2, -2, -2, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
        var column1 = new StackPanel { Spacing = 5, Children = { preview, label } };
        button.Child = new Grid { Children = { column1, ring } };
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        AutomationProperties.SetName(button, label.Text);
        AutomationProperties.SetHelpText(button, CapsuleStyles.Description(button.Id));
        variantRings.Add((button, ring, label, new CzNumber(button.Motion, 0, v => ring.Opacity = v)));
        cardsById[button.Id] = button;
    }

    private Control UsageRow(UsageScrubber scrubber)
    {
        var label = new TextBlock { Text = CapsuleStyles.UsageLeftLabel, FontFamily = Inter, FontSize = 11, Foreground = CzTheme.Text(this, true), VerticalAlignment = VerticalAlignment.Center };
        value = new TextBlock { FontFamily = Inter, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CzTheme.Text(this), MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(5, 8, 5, 2) };
        Grid.SetColumn(scrubber, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(label);
        grid.Children.Add(scrubber);
        grid.Children.Add(value);
        scrubber.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) { value.Text = scrubber.ValueText; AutomationProperties.SetHelpText(scrubber, scrubber.SpokenText); }
        };
        value.Text = scrubber.ValueText;
        AutomationProperties.SetHelpText(scrubber, scrubber.SpokenText);
        return grid;
    }

    private TextBlock? value;
    private readonly List<(Border Dot, TextBlock Label, double Left)> legend = [];

    private Control Legend()
    {
        var wrap = new WrapPanel { Margin = new Thickness(5, 6, 5, 0) };
        var lefts = new Dictionary<string, double> { ["uP"] = 1, ["uH"] = .5, ["uL"] = .25, ["uR"] = .05 };
        foreach (var (key, name) in CapsuleStyles.UsageLegend)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = name, FontFamily = Inter, FontSize = 10.5, Foreground = CzTheme.Text(this, true), VerticalAlignment = VerticalAlignment.Center };
            wrap.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 12, 4), Children = { dot, text } });
            legend.Add((dot, text, lefts[key]));
        }
        AutomationProperties.SetName(wrap, "The usage light");
        return wrap;
    }

    /// <summary>The legend's colours (plenty, half, low, almost out) in the theme shown.</summary>
    internal IReadOnlyList<(string Label, (byte R, byte G, byte B) Colour)> LegendColours =>
        legend.Select(l => (l.Label.Text ?? "", UsageLight.Color(l.Left, CzTheme.Dark(this)))).ToList();

    protected override void UpdateSelection()
    {
        base.UpdateSelection();
        Cards = CapsuleStyles.Cards.Select(id => new PillCard(id, id == CapsuleStyles.Default)).ToList();
    }

    private void Refresh()
    {
        if (cardsById.Count == 0) return;
        var normal = 0;
        foreach (var id in CapsuleStyles.Cards.Where(id => id != "11"))
        {
            var button = cardsById[id];
            var shell = shells[normal];
            var text = shell.Child is StackPanel s ? s.Children : null;
            fills[normal].To(CzTheme.Token(this, button.Picked ? "tilesel" : button.IsPointerOver ? "tile" : "cardfill"), CzMotion.Ease(.2));
            shell.BorderBrush = CzTheme.Brush(CzTheme.Token(this, "sel"));
            rings[normal].To(button.Picked ? 2 : 0, CzMotion.Ease(.2));
            if (text is not null && text.Count == 3 && text[2] is TextBlock line) line.Foreground = CzTheme.Text(this, true);
            normal++;
        }
        foreach (var (button, ring, label, fade) in variantRings)
        {
            ring.BorderBrush = new SolidColorBrush(CzTheme.Accent(this));
            fade.To(button.Picked ? 1 : 0, CzMotion.Ease(.25));
            label.Foreground = CzTheme.Text(this, !button.Picked);
        }
        foreach (var (dot, label, left) in legend)
        {
            var colour = CapsuleTokens.Of(UsageLight.Color(left, CzTheme.Dark(this)));
            dot.Background = new SolidColorBrush(colour);
            label.Foreground = CzTheme.Text(this, true);
        }
        glowCard.Background = CzTheme.Brush(CzTheme.Token(this, "cardfill"));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinColumn * 3 + Gap * 2;
        var columns = Math.Max(1, (int)Math.Floor((width + Gap) / (MinColumn + Gap)));
        var column = (width - Gap * (columns - 1)) / columns;
        double x = 0, y = 0, row = 0, total = 0;
        foreach (var cell in cells)
        {
            var span = cell == glowCard && columns >= 2 ? 2 : 1;
            if (x > 0 && x + span * column + (span - 1) * Gap > width + 0.01) { y += row + Gap; x = 0; row = 0; }
            var cellWidth = span * column + (span - 1) * Gap;
            cell.Measure(new Size(cellWidth, double.PositiveInfinity));
            row = Math.Max(row, cell.DesiredSize.Height);
            x += cellWidth + Gap;
            total = y + row;
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, (int)Math.Floor((finalSize.Width + Gap) / (MinColumn + Gap)));
        var column = (finalSize.Width - Gap * (columns - 1)) / columns;
        double x = 0, y = 0, row = 0;
        var line = new List<(Control Cell, double X, double Width)>();
        void Flush()
        {
            foreach (var (cell, cx, w) in line) cell.Arrange(new Rect(cx, y, w, row));
            line.Clear();
        }
        foreach (var cell in cells)
        {
            var span = cell == glowCard && columns >= 2 ? 2 : 1;
            var cellWidth = span * column + (span - 1) * Gap;
            if (x > 0 && x + cellWidth > finalSize.Width + 0.01) { Flush(); y += row + Gap; x = 0; row = 0; }
            line.Add((cell, x, cellWidth));
            row = Math.Max(row, cell.DesiredSize.Height);
            x += cellWidth + Gap;
        }
        Flush();
        return finalSize;
    }
}

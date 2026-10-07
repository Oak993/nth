// PORT-ONLY: first-release feature unit SK-15 (DL-17). The detail of one set or plugin (layout D): the header, the action row, the folds that say why an item failed and what removing does, the terms step, the
// "In every session" switches and the Needs, Source and Licence lines. Every part that comes and goes folds open and shut (OD-0076); text that changes crossfades.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.App.Views.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class DetailView : Decorator
{
    private readonly ActionArea action;
    private readonly CzFoldRow failure, removeWhy;
    private readonly CzFoldRow? terms;
    private readonly ManualBlock manual;
    private readonly CzCrossfadeText failureText = new("") { FontSize = 12, Wrap = true };
    private readonly SwitchesBox switches;
    private readonly TermsBox? termsBox;
    private readonly CrossfadeSlot needs = new();

    public DetailView(LibraryModel model, LibraryRow row)
    {
        Id = row.Id;
        var tile = LibraryUi.Tile(model.Tools ? "wrench.and.screwdriver" : row.Plugins ? "powerplug" : "wand.and.sparkles", 48, 13);
        var title = LibraryUi.Text(row.Title, 18, weight: SwiftFont.Weight.Semibold, trim: true);
        var summary = LibraryUi.Text(row.Summary, 12, secondary: true, trim: true);
        var meta = LibraryUi.Text(row.Meta, 12, secondary: true, trim: true);
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, summary, meta } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), ColumnSpacing = 14 };
        Grid.SetColumn(words, 1);
        header.Children.Add(tile);
        header.Children.Add(words);

        action = new ActionArea(model, row.Id);

        var warning = LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning);
        warning.VerticalAlignment = VerticalAlignment.Top;
        warning.Margin = new Thickness(0, 1, 0, 0);
        var why = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        Grid.SetColumn(failureText, 1);
        why.Children.Add(warning);
        why.Children.Add(failureText);
        var warnPanel = LibraryUi.Panel(why, 10, 0.06, new Thickness(12, 9));
        manual = new ManualBlock(model, row.Id);
        failure = new CzFoldRow(new StackPanel { Spacing = 8, Children = { warnPanel, manual } }, false);

        var removeText = LibraryUi.Text(LibraryWords.RemoveWhy, 12, secondary: true, wrap: true);
        removeWhy = new CzFoldRow(LibraryUi.Panel(removeText, 10, 0.06, new Thickness(12, 9)), false);

        // only an item under a vendor's terms has a terms box (its label is fixed by the catalogue)
        if (row.TermsLabel is not null)
        {
            termsBox = new TermsBox(model, row);
            terms = new CzFoldRow(termsBox, true);
        }

        switches = new SwitchesBox(model, row);

        var detail = new Grid { ColumnDefinitions = new ColumnDefinitions("76,*"), ColumnSpacing = 10, RowSpacing = 4 };
        void Line(string key, Control value, int at)
        {
            detail.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = LibraryUi.Text(key, 12, secondary: true);
            label.Opacity = 0.8;
            label.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(label, at);
            Grid.SetRow(value, at);
            Grid.SetColumn(value, 1);
            detail.Children.Add(label);
            detail.Children.Add(value);
        }
        var at = 0;
        if (row.Needs.Count > 0) Line(LibraryWords.Needs, needs, at++);
        Line(LibraryWords.Source, LibraryUi.Text(row.Source, 12, wrap: true), at++);
        if (row.Licence is { } licence) Line(LibraryWords.Licence, LibraryUi.Text(licence, 12, wrap: true), at++);

        var stack = new StackPanel { Spacing = 12, Children = { header, action, failure, removeWhy } };
        if (terms is not null) stack.Children.Add(terms);
        if (!model.Tools) stack.Children.Add(switches);
        stack.Children.Add(LibraryUi.Panel(detail));
        Child = stack;
        Apply(row, false);
    }

    public string Id { get; }

    // The detail takes the pane's whole width, so a short title or a short list never leaves it narrower than the pane the crossfade slot centres it in.
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return double.IsInfinity(availableSize.Width) ? size : new Size(availableSize.Width, size.Height);
    }

    public ActionArea Action => action;

    public SwitchesBox Switches => switches;

    /// <summary>The terms step, for an item under a vendor's terms; null for an open item.</summary>
    public TermsBox? Terms => termsBox;

    public bool FailureOpen => failure.Open;

    public bool RemoveWhyOpen => removeWhy.Open;

    public string FailureShown => failureText.Text;

    public IReadOnlyList<string> NeedsShown => needs.Live is StackPanel group ? group.Children.OfType<StackPanel>().Select(line => string.Join(" ", line.Children.OfType<TextBlock>().Select(t => t.Text))).ToList() : [];

    public void Apply(LibraryRow row, bool removeAsked)
    {
        action.Apply(row, removeAsked);
        if (row.Failure is { } message) failureText.SetText(message);
        manual.Apply(row);
        failure.Open = row.State == ItemState.Failed || manual.Checking; // Checking… of the manual files stays in view (the design keeps the fold open while it checks)
        removeWhy.Open = removeAsked;
        termsBox?.Apply(row);
        switches.Apply(row);
        ApplyNeeds(row);
    }

    private string needsKey = "";

    private void ApplyNeeds(LibraryRow row)
    {
        var key = string.Join("|", row.Needs.Select(n => n.Id + ":" + n.FoundPath));
        if (key == needsKey) return;
        needsKey = key;
        var group = new StackPanel { Spacing = 2 };
        foreach (var need in row.Needs)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(LibraryUi.Text(need.Title, 12));
            if (need.FoundOnThisPc)
            {
                line.Children.Add(LibraryUi.Text(LibraryWords.FoundOnThisPc, 11.5, secondary: true));
                ToolTip.SetTip(line, need.FoundPath);
            }
            group.Children.Add(line);
        }
        needs.Show(group, LibraryUi.Ease(this, LibraryUi.Fade));
    }
}

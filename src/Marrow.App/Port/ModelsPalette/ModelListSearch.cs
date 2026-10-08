// PORT-ONLY: owner task T46 adds model/maker search to the source's non-router list; rows and guards stay source-owned.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.ModelsPalette;

internal sealed class ModelListSearch
{
    private readonly List<(ModelListRow Row, string Text, AnimatedScalar Fold)> rows = [];
    private readonly ScrollViewer scroll;
    private readonly AnimatedScalar height;
    private readonly AnimatedScalar offset;
    private readonly Dictionary<ModelListRow, AnimatedColor> highlights = [];
    private readonly Action<ModelCatalog.Entry> choose;
    private readonly List<(ModelListRow Row, ModelCatalog.Entry Entry)> matches = [];
    private int active;
    private bool keyboardActive;
    private readonly double rowHeight, pairsHeight;
    internal TextBox Field { get; }
    internal Control Control { get; }

    internal ModelListSearch(ScrollViewer scroll, IReadOnlyList<ModelCatalog.Entry> entries,
        IReadOnlyList<ModelListRow> modelRows, double rowHeight, double pairsHeight, Action<ModelCatalog.Entry> choose)
    {
        this.scroll = scroll; this.choose = choose; this.rowHeight = rowHeight; this.pairsHeight = pairsHeight;
        height = new AnimatedScalar(scroll, scroll.Height, v => scroll.Height = v);
        offset = new AnimatedScalar(scroll, 0, v => scroll.Offset = new Vector(0, v));
        Field = new TextBox { PlaceholderText = "Search models", FontSize = 12.5, MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        PlainControls.PlainTextField(Field);
        Field.Foreground = Chrome.PrimaryText.Brush(Field);
        Field.PlaceholderForeground = Chrome.SecondaryText.Brush(Field);
        AutomationProperties.SetName(Field, "Search models");
        Control = new Border { Height = 28, Margin = new Thickness(4, 4, 4, 8), Padding = new Thickness(10, 0),
            CornerRadius = new CornerRadius(14), Background = Chrome.PrimaryText.Opacity(.08).Brush(Field),
            BorderBrush = Chrome.PrimaryText.Opacity(.12).Brush(Field), BorderThickness = new Thickness(1), Child = Field };
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var row = modelRows[i];
            row.ClipToBounds = true;
            if (row.Child is Button { Content: Border plate } button)
            {
                var fill = new AnimatedColor(plate); plate.Background = fill.Brush; highlights.Add(row, fill);
                button.OnHover(hovered => fill.Set(hovered && button.IsEnabled || IsActive(row) ? Chrome.Overlay(.1) : SwiftColor.Clear, Chrome.Hover));
            }
            var maker = Makers.Resolve(entry.Provider.Id, entry.Option.Id, PaletteSourcePack.Default.Baseline, entry.Option.ShortName).Maker;
            var text = $"{entry.Option.ShortName} {entry.Option.Id} {entry.Provider.DisplayName} {maker} {ModelPickerSeam.MakerName(maker)}";
            var fold = new AnimatedScalar(row, 1, v =>
            {
                row.Height = rowHeight * v; row.Opacity = v;
                if (v == 0) row.IsVisible = false;
            });
            rows.Add((row, text, fold)); matches.Add((row, entry));
        }
        Field.TextChanged += (_, _) => Query(entries);
        Field.KeyDown += OnKey;
    }

    private void Query(IReadOnlyList<ModelCatalog.Entry> entries)
    {
        matches.Clear(); active = 0; keyboardActive = false;
        var words = (Field.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < rows.Count; i++)
        {
            var item = rows[i]; var shown = words.All(w => item.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (shown) { item.Row.IsVisible = true; matches.Add((item.Row, entries[i])); }
            item.Row.IsHitTestVisible = shown;
            item.Fold.Set(shown ? 1 : 0, Animation.TimingCurve(.25, .1, .25, 1, .34));
        }
        offset.Set(0, null);
        height.Set(Math.Min(pairsHeight + Math.Max(matches.Count, 1) * (rowHeight + 1), 440), Animation.TimingCurve(.25, .1, .25, 1, .34));
        Highlight();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (matches.Count == 0) return;
        if (e.Key is Key.Down or Key.Up)
        {
            keyboardActive = true;
            active = Math.Clamp(active + (e.Key == Key.Down ? 1 : -1), 0, matches.Count - 1);
            Highlight(); BringNearest(matches[active].Row); e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if ((keyboardActive || !string.IsNullOrWhiteSpace(Field.Text)) && matches[active].Row.Child is Button { IsEnabled: true }) choose(matches[active].Entry);
            e.Handled = true;
        }
    }

    private void Highlight()
    {
        foreach (var item in rows)
            if (highlights.TryGetValue(item.Row, out var fill))
                fill.Set(IsActive(item.Row)
                    ? Chrome.Overlay(.1) : SwiftColor.Clear, Chrome.Hover);
    }

    private bool IsActive(ModelListRow row) => matches.Count > 0 && matches[active].Row == row && (keyboardActive || !string.IsNullOrWhiteSpace(Field.Text));

    private void BringNearest(ModelListRow row)
    {
        var y = row.TranslatePoint(default, (Visual)scroll.Content!)?.Y ?? row.Bounds.Y;
        var at = scroll.Offset.Y;
        var target = y < at ? y : y + row.Bounds.Height > at + scroll.Viewport.Height ? y + row.Bounds.Height - scroll.Viewport.Height : at;
        target = Math.Clamp(target, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        offset.Set(at, null);
        offset.Set(target, Animation.TimingCurve(.25, .1, .25, 1, .34));
    }
}

// PORT-ONLY: owner task T46 adds model/maker search to the source's non-router list; rows and guards stay source-owned.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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
            Background = Chrome.PrimaryText.Opacity(.08).Brush(Field),
            BorderBrush = Chrome.PrimaryText.Opacity(.12).Brush(Field), BorderThickness = new Thickness(1), Child = Field }.Capsule(RoundedCornerStyle.Continuous);
        this.entries = entries;
        foreach (var row in modelRows) Add(row);
        Field.TextChanged += (_, _) => Query(entries);
        Field.KeyDown += OnKey;
        scroll.ScrollChanged += (_, e) => { if (held is not null && e.OffsetDelta.Y != 0 && scroll.Offset.Y > 0) Materialise(); }; // ED-NEW-stutterfix-8
    }

    private readonly IReadOnlyList<ModelCatalog.Entry> entries;

    /// <summary>ED-NEW-stutterfix-2: makes the list's rows still to come (DeferredRows); true when there were any.</summary>
    internal Func<bool>? RealiseAll { get; set; }

    /// <summary>The list's next row, for the next entry: searched, highlighted and folded as the rows made with the list.</summary>
    internal void Add(ModelListRow row)
    {
        var entry = entries[rows.Count];
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

    private void Query(IReadOnlyList<ModelCatalog.Entry> entries)
    {
        RealiseAll?.Invoke(); // ED-NEW-stutterfix-2: a search reads every row
        Release(); // ED-NEW-stutterfix-8: rows held by the last search take the values they have reached, and fold on from there
        matches.Clear(); active = 0; keyboardActive = false;
        var words = (Field.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var above = 0.0; // the least height of the rows above this one at any frame of the fold (pairs left out)
        for (var i = 0; i < rows.Count; i++)
        {
            var item = rows[i]; var shown = words.All(w => item.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
            var from = item.Fold.Value; var to = shown ? 1.0 : 0.0;
            item.Row.IsHitTestVisible = shown;
            if (HoldUnseenRows && from != to && above >= UnseenThreshold)
            {
                // ED-NEW-stutterfix-8: never in the viewport while the fold runs; it takes its end state when the fold ends
                (held ??= []).Add((i, from, to, false));
                item.Fold.Set(from, null); // a fold the last search started stops where it is
                item.Row.IsVisible = false;
                if (shown) matches.Add((item.Row, entries[i]));
            }
            else
            {
                if (shown) { item.Row.IsVisible = true; matches.Add((item.Row, entries[i])); }
                item.Fold.Set(to, Animation.TimingCurve(.25, .1, .25, 1, .34));
            }
            if (to > 0) above += rowHeight * Math.Min(from, to) + RowSpacing;
        }
        if (held is not null) Hold();
        offset.Set(0, null);
        height.Set(Math.Min(pairsHeight + Math.Max(matches.Count, 1) * (rowHeight + 1), 440), Animation.TimingCurve(.25, .1, .25, 1, .34));
        Highlight();
    }

    // ED-NEW-stutterfix-8: a search folds the rows the viewport can show while the fold runs, as before. A row whose least distance from the
    // list's top at any frame of the fold is past the tallest viewport (440) and two rows is never seen: it is hidden at once and takes its end
    // state when the fold ends, and one spacer at the list's end stands for what those rows add to the list's height at each frame (each row's
    // height rounded as layout rounds it, and the spacing of a row shown), so the extent and the scrollbar are those of the full fold. A key
    // press or another search while it runs gives every held row the value it would have reached, and the fold goes on from there as before.
    internal static bool HoldUnseenRows = true; // tests compare with the plain fold
    private const double RowSpacing = 1, UnseenBelow = 440;
    private List<(int Index, double From, double To, bool Driven)>? held;
    private AnimatedScalar? heldProgress;
    private Control? heldSpacer;

    private double UnseenThreshold => UnseenBelow + 2 * rowHeight;

    private void Hold()
    {
        var panel = (Panel)scroll.Content!;
        heldSpacer = new Control { IsHitTestVisible = false };
        panel.Children.Add(heldSpacer);
        heldProgress = new AnimatedScalar(scroll, 0, HeldFrame);
        heldProgress.Set(1, Animation.TimingCurve(.25, .1, .25, 1, .34));
    }

    private void HeldFrame(double progress)
    {
        if (held is null || heldSpacer is null) return;
        if (progress >= 1) { EndHold(); return; }
        var scale = LayoutHelper.GetLayoutScale(heldSpacer);
        var pending = 0.0; var any = false;
        for (var k = 0; k < held.Count; k++)
        {
            var (index, from, to, driven) = held[k];
            var value = from + (to - from) * progress;
            if (driven) { var row = rows[index]; row.Fold.Set(value, null); if (to > 0) row.Row.IsVisible = true; continue; }
            var height = rowHeight * value;
            pending += (rows[index].Row.UseLayoutRounding ? LayoutHelper.RoundLayoutValueUp(height, scale) : height) + RowSpacing;
            any = true;
        }
        heldSpacer.IsVisible = any;
        if (any) heldSpacer.Height = pending - RowSpacing;
    }

    private void EndHold()
    {
        var done = held!; held = null;
        foreach (var (index, _, to, _) in done)
        {
            var row = rows[index];
            if (to > 0) row.Row.IsVisible = true;
            row.Fold.Set(to, null);
        }
        if (heldSpacer is { } spacer) ((Panel)scroll.Content!).Children.Remove(spacer);
        heldSpacer = null; heldProgress = null;
    }

    // Every held row takes the value the plain fold has reached; driven from here by the same progress (a key press), or left there (a search).
    private void Materialise()
    {
        if (held is null || heldProgress is null) return;
        var progress = heldProgress.Value;
        for (var k = 0; k < held.Count; k++)
        {
            var (index, from, to, driven) = held[k];
            if (driven) continue;
            var row = rows[index];
            row.Row.IsVisible = true; // as the plain fold has it until it reaches 0
            row.Fold.Set(from + (to - from) * progress, null);
            if (to > 0) row.Row.IsVisible = true;
            held[k] = (index, from, to, true);
        }
        HeldFrame(progress);
    }

    private void Release()
    {
        if (held is null) return;
        Materialise();
        var stopping = heldProgress; held = null; heldProgress = null;
        if (heldSpacer is { } spacer) ((Panel)scroll.Content!).Children.Remove(spacer);
        heldSpacer = null;
        stopping?.Finish(); // its run ends; with no held rows left, its last frame does nothing
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (held is not null && e.Key is Key.Down or Key.Up or Key.Enter) { Materialise(); scroll.UpdateLayout(); }
        // ED-NEW-stutterfix-2: a key press moves through every row; ones just made are laid out before one is scrolled to
        if (e.Key is Key.Down or Key.Up or Key.Enter && RealiseAll?.Invoke() == true) scroll.UpdateLayout();
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

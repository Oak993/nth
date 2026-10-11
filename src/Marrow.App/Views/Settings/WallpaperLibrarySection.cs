using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.App.Views.Settings;

// PORT-ONLY WL-2 (OD-0109, OD-0110 and its amendments; ED-NEW-wltwo-1): the built-in wallpaper library in Settings > Appearance,
// under the upstream picture card (WallpaperCard, "your picture"). A summary card names what is chosen and offers None; then two
// sub-sections, Static wallpapers (90) and Animated wallpapers (72), each its three tiers (Vibrant, Balanced, Minimal) of live
// thumbnails drawn by the real engine, each tier one row until "Show all" opens it. The chosen wallpaper's options open under its
// collection's grid: colour packs by swatch and name (Match accent included), Speed (animated), the wallpaper's own options, Focus,
// Dynamic and Perspective, and Reset to defaults. Names, tier blurbs, option labels and end words are the approved pages' (the
// design snapshot at porting/first-release/design-src/wallpaper-library).

/// <summary>WL-2: the library of built-in wallpapers, in the Appearance page.</summary>
internal sealed class WallpaperLibrarySection : Decorator
{
    private readonly WallpaperLibraryStore store;

    public WallpaperLibrarySection(WallpaperLibraryStore? store = null)
    {
        this.store = store ?? WallpaperLibraryStore.Shared.Current;
        Summary = new WallpaperLibrarySummary(this.store);
        Static = new WallpaperCollection(this.store, WallpaperKind.Static, 0);
        Animated = new WallpaperCollection(this.store, WallpaperKind.Animated, 1000);
        Static.Margin = Animated.Margin = new Thickness(0, Chrome.SectionSpacing - Chrome.SectionHeaderSpacing, 0, 0);
        Child = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { Summary, Static, Animated } };
    }

    public WallpaperLibrarySummary Summary { get; }
    public WallpaperCollection Static { get; }
    public WallpaperCollection Animated { get; }

    // The pages' tier names and blurbs: the static page's data (wallpapers.json "tiers"), the animated page's TIERS (lib-page.js).
    internal static readonly string[] Tiers = ["vibrant", "balanced", "minimal"];
    internal static string TierName(string tier) => tier switch { "vibrant" => "Vibrant", "balanced" => "Balanced", _ => "Minimal" };
    internal static string TierBlurb(WallpaperKind kind, string tier) => (kind, tier) switch
    {
        (WallpaperKind.Static, "vibrant") => "Rich and bold: saturated colour, strong light and deep shadow.",
        (WallpaperKind.Static, "balanced") => "Colourful but restrained: real materials and soft light, moderate colour.",
        (WallpaperKind.Static, _) => "Calm and low in contrast: one quiet idea, soft colour, nothing that pulls the eye.",
        (_, "vibrant") => "Rich, saturated, bold colour, for those who want the window to glow.",
        (_, "balanced") => "Colourful but restrained: the colour is there, the drama is not.",
        _ => "Calm, slow and low in contrast, yet still colourful: soft hue drifts, quiet gradients, gentle light.",
    };

    /// <summary>The wallpaper's packs as the pages offer them: its five named packs, then Match accent built from the Windows accent.</summary>
    internal static IReadOnlyList<WallpaperPack> PacksOf(WallpaperDefinition wall)
    {
        var accent = WallpaperMatchPack.Normalize(SystemWallpaperAccent.Shared.Hex);
        var match = wall.Static is { } data ? StaticWallpaperPalette.MatchAccent(data, accent) : WallpaperMatchPack.Build(wall, accent);
        return [.. wall.Packs, match];
    }

    /// <summary>The animated page's swatch: a pack's three most colourful colours, in their order.</summary>
    internal static Color[] Vivid(WallpaperPack pack, bool dark)
    {
        var hexes = dark ? pack.Dark : pack.Light;
        return hexes.Select((hex, i) => (hex, i, lab: WallpaperPalette.Lab(hex))).OrderByDescending(x => Math.Sqrt(x.lab[1] * x.lab[1] + x.lab[2] * x.lab[2]))
            .Take(3).OrderBy(x => x.i).Select(x => Color.Parse(x.hex)).ToArray();
    }

    internal static bool SameChoices(WallpaperSelection a, WallpaperSelection b) =>
        a.Kind == b.Kind && a.Id == b.Id && a.Pack == b.Pack && Math.Abs(a.Speed - b.Speed) < 1e-9 && a.Dynamic == b.Dynamic && a.Persp == b.Persp
        && Math.Abs((a.Focus ?? 0) - (b.Focus ?? 0)) < 1e-9
        && (a.Options ?? new Dictionary<string, double>()).All(o => b.Options is not null && b.Options.TryGetValue(o.Key, out var v) && Math.Abs(v - o.Value) < 1e-9);

    /// <summary>A small glass capsule text button, as WallpaperCard's Remove and Choose (<c>.buttonStyle(.glass).controlSize(.small)</c>).</summary>
    internal static Button GlassTextButton(string text, Action action) => GlassTextButton(text, action, out _);

    internal static Button GlassTextButton(string text, Action action, out TextBlock label)
    {
        label = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(label);
        var padded = new Border { Child = label, Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var button = PlainControls.Button(padded.ChromeGlassCapsule(), action).KeyboardFocus();
        AutomationProperties.SetName(button, text);
        return button;
    }

    internal static TextBlock Secondary(string text, SwiftFont.TextStyle style = SwiftFont.TextStyle.Subheadline)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(style).Apply(block); block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }
}

/// <summary>The first card: what is chosen, and None (the desktop, or the user's own picture, shows again).</summary>
internal sealed class WallpaperLibrarySummary : Decorator
{
    public WallpaperLibrarySummary(WallpaperLibraryStore store)
    {
        None = WallpaperLibrarySection.GlassTextButton("None", () => store.Selection = new());
        AutomationProperties.SetName(None, "No built-in wallpaper");
        var picture = WallpaperStore.Shared.Current;
        string Detail()
        {
            var s = store.Selection;
            if (s.Kind == WallpaperKind.None) return picture.HasWallpaper ? "None: your picture shows behind the glass" : "None: the desktop shows through the glass";
            var wall = WallpaperCatalogue.Get(s.Kind, s.Id!);
            return $"{wall.Name} · {WallpaperLibrarySection.TierName(wall.Tier)} · {(s.Kind == WallpaperKind.Static ? "Static" : "Animated")}";
        }
        Row = new ChromeRow("Built-in wallpaper", () => None, Detail());
        var dim = new AnimatedScalar(None, store.Selection.Kind == WallpaperKind.None ? .5 : 1, v => None.Opacity = v);
        Row.Track(() => (store.Selection.Kind, store.Selection.Id, picture.HasWallpaper), _ =>
        {
            SettingsRowDetails.Update(Row, Detail());
            var none = store.Selection.Kind == WallpaperKind.None;
            None.IsEnabled = !none; dim.Set(none ? .5 : 1, Animation.EaseOut(.2));
        });
        None.IsEnabled = store.Selection.Kind != WallpaperKind.None;
        Child = new ChromeCard(() => Row);
    }

    public ChromeRow Row { get; }
    public Button None { get; }
}

/// <summary>One collection, Static or Animated: its heading and count, a card of its three tiers, and the chosen one's options under it.</summary>
internal sealed class WallpaperCollection : Decorator
{
    private readonly WallpaperLibraryStore store;
    private readonly ChromeRevealLayout reveal = new(0) { ClipToBounds = true };
    private readonly Panel optionsHost = new();
    private readonly AnimatedScalar revealed;
    private WallpaperOptionsCard? options;

    public WallpaperCollection(WallpaperLibraryStore store, WallpaperKind kind, int orderBase)
    {
        this.store = store; Kind = kind;
        Wallpapers = WallpaperCatalogue.All.Where(w => w.Kind == kind).ToArray();
        Title = kind == WallpaperKind.Static ? "Static wallpapers" : "Animated wallpapers";
        var groups = new StackPanel();
        var order = orderBase;
        foreach (var tier in WallpaperLibrarySection.Tiers)
        {
            var walls = Wallpapers.Where(w => w.Tier == tier).ToArray();
            if (walls.Length == 0) continue;
            if (groups.Children.Count > 0) groups.Children.Add(new ChromeRowDivider());
            var group = new WallpaperTierGroup(store, kind, tier, walls, order); order += walls.Length;
            TierGroups.Add(group); groups.Children.Add(group);
        }
        reveal.Children.Add(optionsHost);
        revealed = new AnimatedScalar(reveal, 0, v => { reveal.Fraction = v; if (v == 0 && store.Selection.Kind != kind) Clear(); });
        var count = WallpaperLibrarySection.Secondary(Wallpapers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var content = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { new ChromeCard(() => groups), reveal } };
        Child = new ChromeSection(Title, () => content, trailing: count);
        var primed = false;
        this.Track(() => (store.Selection.Kind, store.Selection.Id), choice => { Show(choice.Kind == kind ? choice.Id : null, primed); primed = true; });
    }

    public WallpaperKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<WallpaperDefinition> Wallpapers { get; }
    public List<WallpaperTierGroup> TierGroups { get; } = [];
    /// <summary>The chosen wallpaper's options, when the choice is in this collection.</summary>
    public WallpaperOptionsCard? Options => options;

    private void Show(string? id, bool animate)
    {
        var motion = animate ? Animation.Smooth(.4) : null;
        if (id is null) { revealed.Set(0, motion); if (!animate) Clear(); return; }
        if (options?.Wallpaper.Id != id)
        {
            var next = new WallpaperOptionsCard(store, WallpaperCatalogue.Get(Kind, id));
            next.Transition(SwiftTransition.Opacity.Animation(Animation.EaseOut(.25)));
            if (options is { } old) SwiftTransition.Remove(optionsHost, old);
            if (animate) SwiftTransition.Insert(optionsHost, optionsHost.Children.Count, next); else optionsHost.Children.Add(next);
            options = next;
        }
        revealed.Set(1, motion);
    }

    private void Clear()
    {
        if (options is null) return;
        optionsHost.Children.Clear(); options = null;
    }
}

/// <summary>A tier of a collection: its name, count and blurb, "Show all", and its grid of thumbnails.</summary>
internal sealed class WallpaperTierGroup : Decorator
{
    public WallpaperTierGroup(WallpaperLibraryStore store, WallpaperKind kind, string tier, IReadOnlyList<WallpaperDefinition> walls, int orderBase)
    {
        Tier = tier;
        Grid = new WallpaperTileGrid(walls, (wall, index) => new WallpaperTile(store, wall, orderBase + index));
        var name = new TextBlock { Text = WallpaperLibrarySection.TierName(tier), VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(name); name.Foreground = Chrome.PrimaryText.Brush(name);
        var count = WallpaperLibrarySection.Secondary(walls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)); count.VerticalAlignment = VerticalAlignment.Center;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name, count } };
        var blurb = WallpaperLibrarySection.Secondary(WallpaperLibrarySection.TierBlurb(kind, tier));
        var label = $"Show all {walls.Count}";
        var which = $"{WallpaperLibrarySection.TierName(tier)} {(kind == WallpaperKind.Static ? "static" : "animated")} wallpapers";
        TextBlock? text = null;
        ShowAll = WallpaperLibrarySection.GlassTextButton(label, () =>
        {
            Grid.SetExpanded(!Grid.Expanded);
            text!.Text = Grid.Expanded ? "Show fewer" : label;
            AutomationProperties.SetName(ShowAll!, $"{text.Text}, {which}");
        }, out text);
        AutomationProperties.SetName(ShowAll, $"{label}, {which}");
        ShowAll.VerticalAlignment = VerticalAlignment.Top;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        var words = new StackPanel { Spacing = 2, Children = { title, blurb } };
        Avalonia.Controls.Grid.SetColumn(ShowAll, 2); head.Children.Add(words); head.Children.Add(ShowAll);
        Child = new StackPanel
        {
            Children =
            {
                new Border { Padding = new Thickness(16, 12, Chrome.RowControlTrailingPadding, 10), Child = head },
                new Border { Padding = new Thickness(14, 0, 14, 16), Child = Grid },
            },
        };
    }

    public string Tier { get; }
    public WallpaperTileGrid Grid { get; }
    public Button ShowAll { get; }
}

/// <summary>
/// A tier's thumbnails, laid out in equal columns at the pages' 16:10 with the name under each (gaps 12 across, 14 down). One row
/// shows until it is opened; opening and closing ease the height. Tiles are made only for the rows on screen (and half a screen
/// either side) and let go of once they are a screen and a half away, so a page of 162 wallpapers holds only what is seen.
/// </summary>
internal sealed class WallpaperTileGrid : Panel
{
    public const double Gap = 12, RowGap = 14, MinTileWidth = 132, CaptionHeight = 26;
    private readonly Func<WallpaperDefinition, int, WallpaperTile> make;
    private readonly Dictionary<int, WallpaperTile> realized = [];
    private readonly AnimatedScalar reveal;
    private double revealValue, tileWidth, tileHeight;
    private Rect viewport;
    private bool hasViewport;
    private (int First, int Last) wanted = (0, -1);

    public WallpaperTileGrid(IReadOnlyList<WallpaperDefinition> wallpapers, Func<WallpaperDefinition, int, WallpaperTile> make)
    {
        Wallpapers = wallpapers; this.make = make; ClipToBounds = true;
        reveal = new AnimatedScalar(this, 0, v => { revealValue = v; InvalidateMeasure(); });
        EffectiveViewportChanged += (_, e) =>
        {
            viewport = e.EffectiveViewport; hasViewport = true;
            if (Rows(Bounds.Height) != wanted) InvalidateMeasure();
        };
    }

    public IReadOnlyList<WallpaperDefinition> Wallpapers { get; }
    public bool Expanded { get; private set; }
    public int Columns { get; private set; } = 1;
    public int RowCount => (Wallpapers.Count + Columns - 1) / Columns;
    /// <summary>The tiles made now, by their index in the tier.</summary>
    public IReadOnlyDictionary<int, WallpaperTile> Realized => realized;

    public void SetExpanded(bool expanded)
    {
        if (Expanded == expanded) return;
        Expanded = expanded; reveal.Set(expanded ? 1 : 0, Animation.Smooth(.45));
    }

    private double Pitch => tileHeight + RowGap;

    private (int First, int Last) Rows(double height)
    {
        if (tileHeight <= 0) return (0, -1);
        double top, bottom;
        if (!hasViewport) { top = 0; bottom = tileHeight; }
        else
        {
            if (viewport.Width <= 0 || viewport.Height <= 0) return (0, -1);
            top = viewport.Top - viewport.Height / 2; bottom = viewport.Bottom + viewport.Height / 2;
        }
        top = Math.Max(0, top); bottom = Math.Min(height, bottom);
        if (bottom <= top) return (0, -1);
        return ((int)Math.Floor(top / Pitch), Math.Min(RowCount - 1, (int)Math.Floor((bottom - 1e-6) / Pitch)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 3 * MinTileWidth + 2 * Gap;
        Columns = Math.Max(2, (int)Math.Floor((width + Gap) / (MinTileWidth + Gap)));
        tileWidth = Math.Max(1, (width - Gap * (Columns - 1)) / Columns);
        var thumb = Math.Round(tileWidth * 10 / 16);
        tileHeight = thumb + CaptionHeight;
        var full = RowCount * tileHeight + (RowCount - 1) * RowGap;
        var height = tileHeight + (full - tileHeight) * revealValue;
        wanted = Rows(height);
        // keep what is within a screen and a half; make what is within half a screen
        var keepTop = hasViewport ? viewport.Top - viewport.Height * 1.5 : 0;
        var keepBottom = hasViewport ? viewport.Bottom + viewport.Height * 1.5 : tileHeight;
        foreach (var (index, tile) in realized.ToArray())
        {
            var rowTop = index / Columns * Pitch;
            if (rowTop + tileHeight >= keepTop && rowTop <= Math.Min(height, keepBottom)) continue;
            realized.Remove(index); Children.Remove(tile);
        }
        for (var row = wanted.First; row <= wanted.Last; row++)
            for (var col = 0; col < Columns; col++)
            {
                var index = row * Columns + col;
                if (index >= Wallpapers.Count || realized.ContainsKey(index)) continue;
                var tile = make(Wallpapers[index], index); realized[index] = tile; Children.Add(tile);
            }
        foreach (var tile in realized.Values) { tile.ThumbHeight = thumb; tile.Measure(new Size(tileWidth, tileHeight)); }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, tile) in realized)
            tile.Arrange(new Rect(index % Columns * (tileWidth + Gap), index / Columns * Pitch, tileWidth, tileHeight));
        return finalSize;
    }
}

/// <summary>
/// One wallpaper: its live thumbnail (16:10, rounded), its name and its pack's colours as three dots (the static page's caption);
/// the chosen one is ringed in the accent, 2 points out (the pages' selected tile); a hover lifts it 2 points. A press chooses it.
/// </summary>
internal sealed class WallpaperTile : Decorator
{
    private readonly WallpaperLibraryStore store;
    private readonly Border ring, frame;
    private readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly AnimatedScalar ringShown, lift;
    private bool chosen;

    public WallpaperTile(WallpaperLibraryStore store, WallpaperDefinition wall, int order)
    {
        this.store = store; Wallpaper = wall;
        var selection = store.Selection;
        chosen = selection.Kind == wall.Kind && selection.Id == wall.Id;
        Thumbnail = new WallpaperThumbnail(wall, chosen ? selection : store.Recall(wall.Kind, wall.Id)) { Order = order, IsChosen = chosen };
        var hairline = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(WallpaperThumbnail.Radius), IsHitTestVisible = false };
        hairline.BorderBrush = Chrome.Overlay(0.10).Brush(hairline);
        frame = new Border { Child = new Panel { Children = { Thumbnail, hairline } } };
        ring = new Border { Margin = new Thickness(-4), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(WallpaperThumbnail.Radius + 4), IsHitTestVisible = false };
        ring.BorderBrush = Chrome.Accent.Brush(ring);
        var thumb = new Panel { Children = { ring, frame } };
        var name = new TextBlock { Text = wall.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(name); name.Foreground = Chrome.PrimaryText.Brush(name);
        var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), Margin = new Thickness(2, 0) };
        Avalonia.Controls.Grid.SetColumn(dots, 2); caption.Children.Add(name); caption.Children.Add(dots);
        var body = new StackPanel { Spacing = 7, Background = Brushes.Transparent, Children = { thumb, caption } };
        Button = PlainControls.Button(body, () => { if (!chosen) store.Select(wall.Kind, wall.Id); }).KeyboardFocus(WallpaperThumbnail.Radius);
        AutomationProperties.SetName(Button, $"{wall.Name}, {WallpaperLibrarySection.TierName(wall.Tier)} {(wall.Kind == WallpaperKind.Static ? "static" : "animated")} wallpaper");
        ToolTip.SetTip(Button, wall.Desc);
        Child = Button;
        ringShown = new AnimatedScalar(ring, chosen ? 1 : 0, v => ring.Opacity = v);
        var shift = new TranslateTransform(); frame.RenderTransform = shift; ring.RenderTransform = shift;
        lift = new AnimatedScalar(frame, 0, v => shift.Y = -2 * v);
        Button.PointerEntered += (_, _) => lift.Set(1, Animation.EaseOut(.2));
        Button.PointerExited += (_, _) => lift.Set(0, Animation.EaseOut(.2));
        ActualThemeVariantChanged += (_, _) => Dots();
        Dots(); Status();
        this.Track(() => store.Selection, Update);
    }

    public WallpaperDefinition Wallpaper { get; }
    public WallpaperThumbnail Thumbnail { get; }
    public Button Button { get; }
    public bool Chosen => chosen;

    /// <summary>Set by the grid: the thumbnail's height at this column width.</summary>
    public double ThumbHeight { get => frame.Height; set { if (frame.Height != value) frame.Height = value; } }

    private void Update(WallpaperSelection selection)
    {
        var now = selection.Kind == Wallpaper.Kind && selection.Id == Wallpaper.Id;
        if (!now && !chosen) return; // another wallpaper's change: nothing here moves
        if (now != chosen) { chosen = now; ringShown.Set(now ? 1 : 0, Animation.EaseOut(.25)); Thumbnail.IsChosen = now; Status(); }
        Thumbnail.Selection = now ? selection : store.Recall(Wallpaper.Kind, Wallpaper.Id);
        Dots();
    }

    private void Status() => AutomationProperties.SetItemStatus(Button, chosen ? "Chosen" : null);

    private void Dots()
    {
        var pack = WallpaperLibrarySection.PacksOf(Wallpaper).FirstOrDefault(p => p.Id == Thumbnail.Selection.Pack) ?? Wallpaper.Packs[0];
        var colours = WallpaperLibrarySection.Vivid(pack, ActualThemeVariant != ThemeVariant.Light);
        while (dots.Children.Count < colours.Length)
        {
            var dot = new Ellipse { Width = 7, Height = 7 }; dots.Children.Add(dot);
        }
        for (var i = 0; i < colours.Length; i++) ((Ellipse)dots.Children[i]).Fill = new SolidColorBrush(colours[i]);
    }
}

/// <summary>
/// The chosen wallpaper's options, under its collection's grid (the pages' large-view panel in the Settings card language): its name
/// and line with Reset to defaults; Colour pack, swatches with names; Speed (animated); its own options; Focus; Dynamic; Perspective.
/// Every change is written to the library store at once, so the backdrop eases to it and it is kept for the next launch.
/// </summary>
internal sealed class WallpaperOptionsCard : Decorator
{
    private readonly WallpaperLibraryStore store;

    public WallpaperOptionsCard(WallpaperLibraryStore store, WallpaperDefinition wall)
    {
        this.store = store; Wallpaper = wall;
        var animated = wall.Kind == WallpaperKind.Animated;
        Reset = WallpaperLibrarySection.GlassTextButton("Reset to defaults", () => Write(_ => new(wall.Kind, wall.Id)));
        var header = new ChromeRow(wall.Name, () => Reset, wall.Desc);
        Reset.Track(() => Current, s => Reset.IsEnabled = !WallpaperLibrarySection.SameChoices(s, WallpaperLibraryStore.Normalize(new(wall.Kind, wall.Id))));
        Packs = new WallpaperPackPicker(wall, () => Current.Pack ?? wall.DefaultPack, id => Write(s => s with { Pack = id }));
        var rows = new List<Control> { header, Packs };
        if (animated)
            rows.Add(Scalar("Speed", "0.25x", "2.5x", .25, 2.5, .05, () => Current.Speed, v => Write(s => s with { Speed = v }), v => $"{v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}x"));
        foreach (var option in wall.Options)
        {
            var id = option.Id;
            rows.Add(Scalar(option.Label, option.Lo, option.Hi, option.Min, option.Max, .01, () => Current.Options is { } o && o.TryGetValue(id, out var v) ? v : option.Default,
                v => Write(s => s with { Options = With(s.Options, id, v) }), Percent));
        }
        rows.Add(Scalar("Focus", "Sharp", "Dreamy", 0, 1, .01, () => Current.Focus ?? 0, v => Write(s => s with { Focus = v }), v => v < .005 ? "Sharp" : Percent(v)));
        rows.Add(new ChromeRow("Dynamic", () => new SettingsSwitch(new Binding<bool>(() => Current.Dynamic == true, v => Write(s => s with { Dynamic = v }))),
            "Follows the time of day: the colours and the light ease through dawn, day, dusk and night"));
        var perspective = new ChromeRow("Perspective", () => new SettingsSwitch(new Binding<bool>(() => Current.Persp == true, v => Write(s => s with { Persp = v }))), PerspectiveNote(false));
        perspective.Track(() => Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion, reduced => SettingsRowDetails.Update(perspective, PerspectiveNote(reduced)));
        rows.Add(perspective);
        Rows = rows;
        var stack = new StackPanel();
        foreach (var row in rows) { if (stack.Children.Count > 0) stack.Children.Add(new ChromeRowDivider()); stack.Children.Add(row); }
        Child = new ChromeCard(() => stack);
    }

    public WallpaperDefinition Wallpaper { get; }
    public Button Reset { get; }
    public WallpaperPackPicker Packs { get; }
    public IReadOnlyList<Control> Rows { get; }

    /// <summary>This wallpaper's choices: the store's selection while it is chosen, else what it remembers.</summary>
    private WallpaperSelection Current
    {
        get
        {
            var s = store.Selection;
            return s.Kind == Wallpaper.Kind && s.Id == Wallpaper.Id ? s : store.Recall(Wallpaper.Kind, Wallpaper.Id);
        }
    }

    private void Write(Func<WallpaperSelection, WallpaperSelection> change) => store.Selection = change(Current);

    private string PerspectiveNote(bool reduced) => reduced ? "Off while Reduce Motion is on"
        : Wallpaper.Premium?.Perspective.Mode == "layers" ? "The layers shift a few pixels with the window's place on the screen"
        : "The whole picture pans a few pixels with the window's place on the screen";

    private static string Percent(double v) => $"{Math.Round(v * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}%";

    private static IReadOnlyDictionary<string, double> With(IReadOnlyDictionary<string, double>? options, string id, double value)
    {
        var next = options?.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal) ?? new Dictionary<string, double>(StringComparer.Ordinal);
        next[id] = value; return next;
    }

    /// <summary>A row with the app's slider (140 wide, as Transparency), the page's end words either side and the value after it.</summary>
    private static ChromeRow Scalar(string title, string? lo, string? hi, double min, double max, double step, Func<double> read, Action<double> write, Func<double, string> format)
    {
        return new ChromeRow(title, () =>
        {
            var slider = new Slider { Minimum = min, Maximum = max, Width = 140, Value = read(), SmallChange = step, LargeChange = (max - min) / 10, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, title);
            var value = new TextBlock { MinWidth = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit());
            value.Foreground = Chrome.SecondaryText.Brush(value);
            var updating = false; var dragging = false;
            var thumb = new SettingsScalarAnimation(slider, slider.Value, v => { updating = true; try { slider.Value = v; } finally { updating = false; } });
            slider.PropertyChanged += (_, change) =>
            {
                if (change.Property != Slider.ValueProperty) return;
                value.Text = format(slider.Value);
                if (!updating && Math.Abs(read() - slider.Value) > 1e-9) { dragging = true; try { write(slider.Value); } finally { dragging = false; } }
            };
            // a change from elsewhere (Reset to defaults, a recalled choice) moves the thumb along a short ease; the slider's own drag does not
            slider.Track(read, v => { if (Math.Abs(slider.Value - v) > 1e-9) thumb.Set(v, dragging ? null : Animation.Snappy(.3)); value.Text = format(v); });
            value.Text = format(slider.Value);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (lo is not null) { var l = WallpaperLibrarySection.Secondary(lo, SwiftFont.TextStyle.Caption); l.TextWrapping = TextWrapping.NoWrap; l.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(l); }
            row.Children.Add(slider);
            if (hi is not null) { var h = WallpaperLibrarySection.Secondary(hi, SwiftFont.TextStyle.Caption); h.TextWrapping = TextWrapping.NoWrap; h.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(h); }
            row.Children.Add(value);
            return row;
        });
    }
}

/// <summary>Colour pack: the pages' round swatches (the pack's three most colourful colours, lit from the top left), each with its name under it.</summary>
internal sealed class WallpaperPackPicker : Decorator
{
    public const double Swatch = 34;

    public WallpaperPackPicker(WallpaperDefinition wall, Func<string> read, Action<string> write)
    {
        var title = new TextBlock { Text = "Colour pack" };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(title); title.Foreground = Chrome.PrimaryText.Brush(title);
        var current = WallpaperLibrarySection.Secondary("");
        current.TextWrapping = TextWrapping.NoWrap; current.VerticalAlignment = VerticalAlignment.Center;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        Avalonia.Controls.Grid.SetColumn(current, 2); head.Children.Add(title); head.Children.Add(current);
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var pack in WallpaperLibrarySection.PacksOf(wall))
        {
            var item = new PackSwatch(pack, () => write(pack.Id));
            Swatches.Add(item); wrap.Children.Add(item);
        }
        Child = new Border { Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 12), Child = new StackPanel { Spacing = 10, Children = { head, wrap } } };
        this.Track(read, id =>
        {
            var name = Swatches.FirstOrDefault(s => s.Pack.Id == id)?.Pack.Name ?? "";
            current.Text = id == wall.DefaultPack ? $"{name} · default" : name;
            foreach (var s in Swatches) s.SetChosen(s.Pack.Id == id);
        });
    }

    public List<PackSwatch> Swatches { get; } = [];

    internal sealed class PackSwatch : Decorator
    {
        private readonly Border disc;
        private readonly AnimatedScalar ring;
        private bool chosen;

        public PackSwatch(WallpaperPack pack, Action choose)
        {
            Pack = pack;
            disc = new Border { Width = Swatch, Height = Swatch, CornerRadius = new CornerRadius(Swatch / 2), HorizontalAlignment = HorizontalAlignment.Center };
            var shine = new Border
            {
                Width = Swatch, Height = Swatch, CornerRadius = new CornerRadius(Swatch / 2), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center,
                Background = new RadialGradientBrush
                {
                    Center = new RelativePoint(.32, .30, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.32, .30, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(.42, RelativeUnit.Relative), RadiusY = new RelativeScalar(.42, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0x59, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
                },
            };
            var edge = new Border { Width = Swatch, Height = Swatch, CornerRadius = new CornerRadius(Swatch / 2), BorderThickness = new Thickness(1), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
            edge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0));
            var halo = new Border { Width = Swatch + 8, Height = Swatch + 8, CornerRadius = new CornerRadius(Swatch / 2 + 4), BorderThickness = new Thickness(2), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
            halo.BorderBrush = Chrome.Accent.Brush(halo);
            var discs = new Panel { Width = Swatch + 8, Height = Swatch + 8, Children = { disc, shine, edge, halo } };
            disc.VerticalAlignment = shine.VerticalAlignment = edge.VerticalAlignment = VerticalAlignment.Center;
            var name = new TextBlock { Text = pack.Name, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 64, HorizontalAlignment = HorizontalAlignment.Center };
            SwiftFont.System(SwiftFont.TextStyle.Caption).Apply(name); name.Foreground = Chrome.SecondaryText.Brush(name);
            var body = new StackPanel { Width = 66, Spacing = 3, Background = Brushes.Transparent, Children = { discs, name } };
            Button = PlainControls.Button(body, choose).KeyboardFocus(10);
            AutomationProperties.SetName(Button, pack.Name);
            ToolTip.SetTip(Button, pack.Name);
            Child = Button;
            ring = new AnimatedScalar(halo, 0, v => halo.Opacity = v);
            ActualThemeVariantChanged += (_, _) => Paint();
            Paint();
        }

        public WallpaperPack Pack { get; }
        public Button Button { get; }
        public bool Chosen => chosen;

        public void SetChosen(bool value)
        {
            if (chosen == value) return;
            chosen = value; ring.Set(value ? 1 : 0, Animation.EaseOut(.25));
            AutomationProperties.SetItemStatus(Button, value ? "Chosen" : null);
        }

        private void Paint()
        {
            var c = WallpaperLibrarySection.Vivid(Pack, ActualThemeVariant != ThemeVariant.Light);
            var stops = new GradientStops();
            for (var i = 0; i <= c.Length; i++) stops.Add(new GradientStop(c[i % c.Length], (double)i / c.Length));
            disc.Background = new ConicGradientBrush { Angle = 200, GradientStops = stops };
        }
    }
}

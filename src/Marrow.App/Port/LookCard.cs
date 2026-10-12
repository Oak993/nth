using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Port;

// PORT-ONLY (OD-0088, OD-0081, OD-0076, OD-0075, OD-0069; ED-250): the accent picker, variant D3 "Look card" of the
// locked board E:/Droppy/work/design/accent-d3-locked.dc.html, built to E:/Droppy/work/design/accent-build/SPEC.md. It
// stands at the top of Settings > Appearance in place of the source's Theme row (SettingsView.swift:424-426, the
// ThemePickerButton capsule :1155-1212, an owner-ruled deviation) and holds, top to bottom:
//   - a mini window (SPEC 2.2), a still picture of a chat that re-tints with the theme and the accent;
//   - the Theme row: System | Light | Dark | "More themes" or the named theme's name, the last opening the menu of the
//     23 named themes (SPEC 2.3, 2.4; the source's menu, AppTheme order, sectioned Dark then Light);
//   - the Accent row: the ten Classic swatches and More, which folds open the twelve Signature swatches (2.5, 2.6).
// The card's shell (fill, corners, row paddings, title and detail type, the divider) is the port's ChromeCard, ChromeRow and
// ChromeRowDivider; everything new inside it takes the board's numbers. Every change transitions on the frame clock as the
// board's CSS does (LookMotion.cs); Reduce Motion stills them. ENGINEERING DECISIONS (SPEC section 9): the mini window
// sits flush with the card's top as the locked frames draw it (9.1); the detail lines carry names only, no hex (9.2,
// OD-0088 "Names only"); the menu lists the 23 named themes as the board does (9.3); Escape and a click outside close the
// menu however it was opened (9.4); each segment is its own tab stop with no arrow keys, as the board (9.5); the Windows
// accent is lifted app-wide (9.6, ED-250); System's detail reads "System · <accent>" (9.7); a swatch's release eases back
// to the hover scale in 0.12 s as the board's frames measure it (scenario 20), not the 0.08 s of SPEC 6.9's text.
// ED-254 (amends ED-250): the segments are SPEC 2.3's table (the board's label widths + 22, + 11 for the chevron), the
// labels drawn in the port's type (Inter, OD-0009) and centred with the chevron, at least 6 clear of each end; the rows
// keep the board's 28 between text and control, so the Accent detail has the board's column (156 here) and every line of
// SPEC 4.3 fits whole; the window turns over with the appearance (AppearanceTurn, SPEC 6.5).

/// <summary>The Look card (SPEC section 1): the first card of Settings &gt; Appearance.</summary>
public sealed class LookCard : Decorator
{
    public const double MiniHeight = 128, FoldOpen = 36, Pitch = 26, SwatchSize = 24, LineWidth = 310, MenuWidth = 264, MenuGap = 15, MenuListMax = 260;

    /// <summary>SPEC 2.1: the gap between a row's text column and its control (the board's 28; ChromeRow's is 44).</summary>
    public const double RowGap = 28;

    /// <summary>
    /// LEAD RULING (rec-linebox, 2026-10-04; ED-316 (6)): the rows are Swift ChromeRows, which the Mac draws 58 tall (ED-290 (1),
    /// 1-settings-tour.mov S 23.5 and S 194.0), so the rows are 58 and the card follows its content; the owner-approved board was drawn with
    /// 54-point rows, so its other geometry is kept and only the row height changes: each control keeps the board's offset from its own row
    /// top, the segments 13 (SPEC 2.3) and the Classic line 15 (SPEC 2.5), and the 4 extra points fall below them. At the Settings
    /// sizes of ED-NEW-textsize-2 the rows are 18 + 17 + 2 + 16 + 18 = 71 and the offsets hold the same way.
    /// </summary>
    public const double SegmentsTop = 13, LineTop = 15;

    /// <summary>
    /// SPEC 2.3: each segment label's width on the board (Segoe UI Variable Text 12/500, canvas measure, rounded up). A
    /// segment is that + 22, and the fourth + 11 more for its chevron, so the control is the board's at every theme (298 at
    /// its widest, Catppuccin Mocha). The label is drawn in the port's type (Inter, OD-0009), centred with the chevron.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, double> BoardLabelWidths = new Dictionary<string, double>
    {
        ["System"] = 39, ["Light"] = 28, ["Dark"] = 26, ["More themes"] = 73,
        ["Catppuccin Mocha"] = 102, ["Catppuccin Latte"] = 92, ["Dracula"] = 42, ["Tokyo Night"] = 67, ["Nord"] = 29,
        ["Gruvbox"] = 47, ["Gruvbox Light"] = 78, ["One Dark"] = 52, ["Everforest"] = 55, ["Kanagawa"] = 56,
        ["Ros\u00E9 Pine"] = 53, ["Solarized Dark"] = 79, ["Solarized Light"] = 81, ["GitHub Dark"] = 68, ["GitHub Light"] = 70,
        ["Ayu"] = 21, ["Night Owl"] = 56, ["Monokai"] = 49, ["Claude"] = 38, ["Claude Light"] = 69, ["Codex"] = 35,
        ["Cursor"] = 36, ["Matrix"] = 36,
    };

    /// <summary>SPEC 2.3: a segment's width for its label (the board's label width, else the port's measure).</summary>
    internal static double SegmentWidthFor(string label, bool chevron) =>
        (BoardLabelWidths.TryGetValue(label, out var width) ? width : LookDraw.Width(label, SegmentFont)) + 22 + (chevron ? 11 : 0);

    internal static readonly SwiftFont SegmentFont = SwiftFont.System(12, SwiftFont.Weight.Medium);
    internal static readonly SwiftFont MoreFont = SwiftFont.System(11, SwiftFont.Weight.Medium);
    internal static readonly SwiftFont SignatureFont = SwiftFont.System(12);
    internal static readonly SwiftFont DetailFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline);
    internal static readonly SwiftFont HeaderFont = SwiftFont.System(11, SwiftFont.Weight.Semibold);
    internal static readonly SwiftFont RowFont = SwiftFont.System(13);

    private readonly AppSettings settings;
    private readonly Dictionary<string, (bool On, double X, bool Jump)> rings = [];
    private bool primed;
    private bool suppressBringIntoView;
    private IPlatformSettings? platform;

    public LookCard(AppSettings settings)
    {
        this.settings = settings;
        Motion = new LookMotion(this);
        Motion.Frame += Refresh;
        Turn = new LookNumber(Motion, 1);
        Accent = new LookColor(Motion, Colors.Transparent);
        OnAccent = new LookColor(Motion, Colors.White);
        Surface = new LookColor(Motion, Colors.Transparent);

        Mini = new MiniWindow(this) { Margin = new Thickness(12, 0, 12, 0) };
        Segments = new ThemeSegments(this);
        ThemeDetail = new CrossfadeText(Motion, DetailFont);
        AccentDetail = new CrossfadeText(Motion, DetailFont);
        Classic = new SwatchLine(this, signature: false);
        Signature = new SwatchLine(this, signature: true);
        More = Classic.More!;
        Fold = new SignatureFold(this, Signature);
        ThemeRow = Row("Theme", Segments, ThemeDetail, SegmentsTop);
        AccentRow = Row("Accent", Classic, AccentDetail, LineTop);
        var content = new StackPanel { Orientation = Orientation.Vertical, Children = { Mini, ThemeRow, new ChromeRowDivider(), AccentRow, Fold } };
        Card = new ChromeCard(() => content);
        Child = Card;
        Menu = new ThemeMenu(this);
        VisualChildren.Add(Menu);
        LogicalChildren.Add(Menu);
        AutomationProperties.SetName(Classic, "Accent");
        AutomationProperties.SetName(Segments, "Theme");

        this.Track(() => (settings.Theme, settings.Accent), _ => Apply());
        ActualThemeVariantChanged += (_, _) => { if (primed) Apply(); };
        AttachedToVisualTree += (_, _) =>
        {
            platform = Application.Current?.PlatformSettings;
            if (platform is not null) platform.ColorValuesChanged += OnColorValuesChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (platform is not null) platform.ColorValuesChanged -= OnColorValuesChanged;
            platform = null;
            Menu.Close(immediately: true);
        };
        // SPEC 7: focus moves between swatches without scrolling the page.
        AddHandler(RequestBringIntoViewEvent, (_, e) => { if (suppressBringIntoView) e.Handled = true; });
    }

    // MARK: Parts (internal for the tests)

    internal LookMotion Motion { get; }
    internal ChromeCard Card { get; }
    internal MiniWindow Mini { get; }
    internal ThemeSegments Segments { get; }
    internal ChromeRow ThemeRow { get; }
    internal ChromeRow AccentRow { get; }
    internal CrossfadeText ThemeDetail { get; }
    internal CrossfadeText AccentDetail { get; }
    internal SwatchLine Classic { get; }
    internal SwatchLine Signature { get; }
    internal MoreButton More { get; }
    internal SignatureFold Fold { get; }
    internal ThemeMenu Menu { get; }

    /// <summary>0 in Light, 1 in Dark; every window colour of the card is the mix of its two by this (SPEC 6.5).</summary>
    internal LookNumber Turn { get; }

    /// <summary>The effective accent as drawn (<c>--ac</c>, <c>--pac</c>).</summary>
    internal LookColor Accent { get; }

    /// <summary><c>--aon</c>: the arrow on the mini window's send button.</summary>
    internal LookColor OnAccent { get; }

    /// <summary><c>--surf</c>: the mini window's surface.</summary>
    internal LookColor Surface { get; }

    /// <summary>The Signature line is folded open.</summary>
    internal bool Open { get; private set; }

    /// <summary>What the pointer is on: a swatch id, "more", or nothing.</summary>
    internal string? Hover { get; private set; }

    internal AppSettings Settings => settings;

    internal AppTheme CardTheme => settings.Theme;

    internal bool IsNamed => settings.Theme.Spec().Accent is not null;

    internal int Picked => Math.Max(0, AccentPalette.IndexOf(settings.Accent));

    internal bool Held => !Open && Picked >= AccentPalette.ClassicCount;

    // MARK: Tokens (SPEC 3.3), each a pair mixed by Turn

    internal Color Mix(Color light, Color dark) => SwiftColor.PremultipliedMix(light, dark, Turn.Value);

    internal Color Mix(SwiftColor token) => Mix(token.Resolve(ThemeVariant.Light), token.Resolve(ThemeVariant.Dark));

    internal Color Tx => Mix(Chrome.PrimaryText);
    internal Color Tx2 => Mix(Chrome.SecondaryText);
    internal Color Cap => Mix(Color.FromArgb(140, 255, 255, 255), Color.FromArgb(51, 0, 0, 0)); // rgba(255,255,255,.55) / rgba(0,0,0,.2)
    internal Color CapHover => Mix(Color.FromArgb(209, 255, 255, 255), Color.FromArgb(77, 0, 0, 0)); // .82 / .3
    internal Color PillFill => Mix(Colors.White, Color.FromArgb(41, 255, 255, 255)); // #FFFFFF / rgba(255,255,255,.16)
    internal Color Rim => Mix(SwiftColor.Primary.Opacity(0.22)); // the theme swatch's rim (AppTheme.swift:152)
    internal Color RingColor => Mix(Color.FromArgb(199, 0, 0, 0), Color.FromArgb(235, 255, 255, 255)); // rgba(0,0,0,.78) / rgba(255,255,255,.92)
    internal Color Divider => Mix(Chrome.Overlay(0.12)); // ChromeRowDivider's hairline (UI-PROVREG-009: measured 0.10 alpha, Overlay(0.12))
    internal Color PopFill => Mix(Colors.White, Color.FromRgb(0x1E, 0x1E, 0x1E)); // PopoverHost's panel (ED-238)
    internal Color PopEdge => Mix(Color.FromArgb(20, 0, 0, 0), Color.FromArgb(51, 255, 255, 255)); // rgba(0,0,0,.08) / rgba(255,255,255,.2)
    internal Color RowHover => Mix(Chrome.Overlay(0.1)); // PopoverItem's hover (Popovers.swift)
    internal Color RowPress => Mix(Color.FromArgb(26, 0, 0, 0), Color.FromArgb(36, 255, 255, 255)); // rgba(0,0,0,.1) / rgba(255,255,255,.14)
    internal Color Ink => Mix(Color.FromRgb(0x1C, 0x1C, 0x1E), Color.FromRgb(0xEC, 0xEC, 0xEC));
    internal Color Track => Mix(Color.FromArgb(20, 0, 0, 0), Color.FromArgb(31, 255, 255, 255)); // rgba(0,0,0,.08) / rgba(255,255,255,.12)

    // MARK: Words (SPEC 4.3)

    private static string Dot(string a, string b) => a + " \u00B7 " + b;

    private string OwnWord => AccentPalette.OwnWord(CardTheme) ?? "";

    /// <summary>A swatch's accessible name: its colour, or the theme's own ("Dracula purple").</summary>
    internal string AccessibleName(int index) =>
        index == 0 && IsNamed ? CardTheme.DisplayName() + " " + OwnWord.ToLowerInvariant() : AccentPalette.All[index].Name;

    private string ShortName(int index) => index == 0 && IsNamed ? OwnWord : AccentPalette.All[index].Name;

    internal string AccentLine(int index)
    {
        if (index == 0) return IsNamed ? OwnLine : Dot("Windows accent", "default");
        var name = AccentPalette.All[index].Name;
        if (!IsNamed) return name;
        return Fit(Dot(name, "over " + CardTheme.DisplayName()), Dot(name, "over the theme"), name);
    }

    // "<word> · the theme's own" (SPEC 4.3), the word alone in the unlikely case the column cannot hold it.
    private string OwnLine => Fit(Dot(OwnWord, "the theme's own"), OwnWord);

    // The first words that fit the text column whole (OD-0069: a line is never cut), else the last.
    private string Fit(params string[] candidates)
    {
        foreach (var words in candidates.SkipLast(1))
            if (LookDraw.Width(words, DetailFont) <= DetailRoom) return words;
        return candidates[^1];
    }

    // The text column beside the Accent row's swatches: 520 - 16 - 10 - 28 - 310 = 156 (the board's 155, SPEC 4.3, with the
    // port's 10 trailing padding).
    internal double DetailRoom => AccentDetail.Bounds.Width > 0 ? AccentDetail.Bounds.Width : 520 - 16 - Chrome.RowControlTrailingPadding - RowGap - LineWidth;

    internal string ThemeLine
    {
        get
        {
            var appearance = CardTheme == AppTheme.System ? "System" : CardTheme.Scheme() == ThemeVariant.Dark ? "Dark" : "Light";
            return Dot(appearance, ShortName(Picked));
        }
    }

    private string AccentText => Hover switch
    {
        "more" => AccentPalette.All.Count - AccentPalette.ClassicCount + " Signature accents",
        { } id when AccentPalette.IndexOf(id) is var i and >= 0 => i == 0 && IsNamed ? OwnLine : ShortName(i),
        _ => AccentLine(Picked),
    };

    // MARK: Colours

    internal bool Dark => LookDraw.IsDark(ActualThemeVariant);

    /// <summary>The colour a swatch is drawn in for the appearance (SPEC 4.2 "first swatch shows").</summary>
    internal Color SwatchColor(int index, bool dark)
    {
        if (index > 0) return Color.Parse(AccentPalette.All[index].Hex(dark));
        var own = CardTheme.Spec().Accent;
        return own is not null
            ? AccentPalette.Lift(own.Resolve(ThemeVariant.Light), dark)
            : SwiftColor.ControlAccentColor.Resolve(dark ? ThemeVariant.Dark : ThemeVariant.Light);
    }

    private Color EffectiveAccent => AccentPalette.Effective(settings.Accent, CardTheme).Resolve(ActualThemeVariant);

    private Color MiniSurface
    {
        get
        {
            var spec = CardTheme.Spec();
            if (spec.Accent is not null) return spec.Surface.Resolve(ThemeVariant.Light); // a named theme's surface hex
            return Dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;
        }
    }

    // MARK: State

    private void OnColorValuesChanged(object? sender, PlatformColorValues values)
    {
        if (CheckAccess()) Apply();
        else Dispatcher.Post(Apply);
    }

    /// <summary>Hover on a swatch or More (null when the pointer leaves).</summary>
    internal void SetHover(string? hover)
    {
        if (Hover == hover) return;
        Hover = hover;
        Apply();
    }

    internal void ClearHover(string hover)
    {
        if (Hover == hover) SetHover(null);
    }

    /// <summary>More: folds the Signature line open or shut (SPEC 6.2, 6.3).</summary>
    internal void ToggleFold()
    {
        Open = !Open;
        Hover = null;
        Apply();
    }

    /// <summary>A swatch picked: the setting is written at once (SPEC 4.1).</summary>
    internal void Pick(int index)
    {
        if (index >= AccentPalette.ClassicCount && !Open) Open = true; // SPEC 7: moving onto a shut line opens it
        settings.Accent = AccentPalette.All[index].Id;
        Apply();
    }

    internal void PickTheme(AppTheme theme)
    {
        Menu.Close();
        settings.Theme = theme;
    }

    /// <summary>The ids each line's ring is on, and the transitions SPEC 6.1 gives it.</summary>
    private void Ring(string line, RingMark ring, bool on, double x, bool animate)
    {
        var memo = rings.TryGetValue(line, out var m) ? m : (On: on, X: on ? x : 0, Jump: true);
        if (on)
        {
            if (!memo.On) memo.Jump = true;
            else if (x != memo.X) memo.Jump = false;
            memo.X = x;
        }
        memo.On = on;
        rings[line] = memo;
        if (!animate || memo.Jump) ring.X.Jump(memo.X);
        else ring.X.To(memo.X, LookCurve.Ez(0.3));
        if (!animate)
        {
            ring.Fade.Jump(on ? 1 : 0);
            ring.Scale.Jump(on ? 1 : 0.8);
            return;
        }
        ring.Fade.To(on ? 1 : 0, LookCurve.Ease(0.18));
        ring.Scale.To(on ? 1 : 0.8, LookCurve.Ez(0.24));
    }

    /// <summary>Sets every value to what the state asks for, transitioned (from the first apply on).</summary>
    internal void Apply()
    {
        var animate = primed;
        var dark = Dark;
        void Color(LookColor value, Color target, Animation animation)
        {
            if (animate) value.To(target, animation); else value.Jump(target);
        }
        void Number(LookNumber value, double target, Animation animation, double delay = 0)
        {
            if (animate) value.To(target, animation, delay); else value.Jump(target);
        }

        // SPEC 6.4, 6.5: the appearance and the accent, .32 s ease.
        Number(Turn, dark ? 1 : 0, LookCurve.Ease(0.32));
        var accent = EffectiveAccent;
        Color(Accent, accent, LookCurve.Ease(0.32));
        Color(OnAccent, AccentPalette.OnColor(accent), LookCurve.Ease(0.32));
        Color(Surface, MiniSurface, LookCurve.Ease(0.32));

        // The swatches (SPEC 2.5, 5, 6.1, 6.4).
        var picked = Picked;
        var open = Open;
        var held = Held;
        foreach (var swatch in Classic.Swatches.Concat(Signature.Swatches))
        {
            Color(swatch.Fill, SwatchColor(swatch.Index, dark), LookCurve.Ease(0.32));
            if (swatch.Index == 0) Number(swatch.WindowsDot, IsNamed ? 0 : 1, LookCurve.Ease(0.2));
            AutomationProperties.SetName(swatch, AccessibleName(swatch.Index));
            swatch.Picked = swatch.Index == picked;
            var visible = swatch.Index < AccentPalette.ClassicCount || open;
            swatch.Focusable = visible;
            swatch.IsTabStop = visible && (swatch.Index == picked || (held && swatch.Index == 0));
        }
        Ring("classic", Classic.Ring, picked < AccentPalette.ClassicCount, picked * Pitch, animate);
        Ring("signature", Signature.Ring, open && picked >= AccentPalette.ClassicCount, (picked - AccentPalette.ClassicCount) * Pitch, animate);

        // The fold (SPEC 6.2).
        Number(Fold.FoldHeight, open ? FoldOpen : 0, LookCurve.Ease(0.3));
        if (open) Number(Fold.LineOpacity, 1, LookCurve.Ease(0.17), 0.13);
        else Number(Fold.LineOpacity, 0, LookCurve.Ease(0.08));
        Number(Fold.LineY, open ? 0 : -6, LookCurve.Ease(0.3));
        Signature.IsHitTestVisible = open;

        // More (SPEC 2.5, 6.2, 6.3).
        More.Apply(animate, open, held, held ? SwatchColor(picked, dark) : null);

        // The Theme control (SPEC 2.3, 6.6, 6.7).
        Segments.Apply(animate);
        Menu.Apply(animate);

        // The detail lines (SPEC 4.3, 6.6).
        ThemeDetail.Set(ThemeLine, animate);
        AccentDetail.Set(AccentText, animate);

        primed = true;
        Refresh();
    }

    /// <summary>Draws what the values hold now (each frame, and after a change).</summary>
    internal void Refresh()
    {
        Mini.Refresh();
        Segments.Refresh();
        Classic.Refresh();
        Signature.Refresh();
        Fold.Refresh();
        ThemeDetail.Refresh(Tx2);
        AccentDetail.Refresh(Tx2);
        Menu.Refresh();
    }

    // MARK: Keyboard (SPEC 7)

    internal void Arrow(Key key)
    {
        var n = AccentPalette.All.Count;
        var classic = AccentPalette.ClassicCount;
        var i = Picked;
        i = key switch
        {
            Key.Right => (i + 1) % n,
            Key.Left => (i - 1 + n) % n,
            Key.Down or Key.Up => i < classic ? classic + Math.Min(n - classic - 1, i) : Math.Max(0, Math.Min(classic - 1, i - classic)),
            Key.Home => 0,
            Key.End => n - 1,
            _ => -1,
        };
        if (i < 0) return;
        Pick(i);
        var target = i < classic ? Classic.Swatches[i] : Signature.Swatches[i - classic];
        suppressBringIntoView = true;
        try { target.Focus(NavigationMethod.Directional); }
        finally { suppressBringIntoView = false; }
    }

    // MARK: Layout: the menu hangs below the Theme control, over what follows (SPEC 2.4)

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        Menu.Measure(new Size(MenuWidth, double.PositiveInfinity));
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        var origin = Segments.TranslatePoint(default, this) ?? default;
        var right = origin.X + Segments.Bounds.Width;
        Menu.Arrange(new Rect(right - MenuWidth, origin.Y + Segments.Bounds.Height + MenuGap, MenuWidth, Menu.DesiredSize.Height));
        return size;
    }

    private static ChromeRow Row(string title, Control control, CrossfadeText detail, double controlTop)
    {
        var row = new ChromeRow(title, () => control, " ");
        var shell = (Border)row.Child!;
        // The control at the board's offset from the row's top (LEAD RULING above), not centred in the taller row: measured from
        // the row's own top padding (the source's 11; the Settings rows' 18, ED-NEW-textsize-2), so the segments and the Classic
        // line sit beside the title's line and the taller text falls below them.
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Margin = new Thickness(0, controlTop - shell.Padding.Top, 0, 0);
        // The row's own detail slot (ChromeRow: Border > Grid > the title and detail stack) takes the crossfading line.
        var grid = (Grid)shell.Child!;
        var labels = (StackPanel)grid.Children[0];
        labels.Children[1] = detail;
        // SPEC 2.1: the board's 28 between the text column and the control (ChromeRow's HStack gives 16 + 12 + 16), so the
        // Accent detail has the board's column (SPEC 4.3) and no line of 4.3 is cut.
        grid.ColumnDefinitions[1].Width = new GridLength(RowGap);
        grid.ColumnDefinitions[2].Width = new GridLength(0);
        grid.ColumnDefinitions[3].Width = new GridLength(0);
        return row;
    }
}

/// <summary>A line of text that crossfades to new words, keeping the old mounted until it has faded (SPEC 6.6).</summary>
internal sealed class CrossfadeText : Panel
{
    private readonly LookMotion motion;
    private readonly SwiftFont font;
    private readonly List<(TextBlock Text, LookNumber Opacity)> lines = [];

    public CrossfadeText(LookMotion motion, SwiftFont font)
    {
        this.motion = motion;
        this.font = font;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>The words shown (fading in or in).</summary>
    public string Text { get; private set; } = "";

    /// <summary>Every mounted line with its opacity now, oldest first.</summary>
    internal IEnumerable<(string Text, double Opacity)> Lines => lines.Select(l => (l.Text.Text ?? "", l.Opacity.Value));

    public void Set(string text, bool animate)
    {
        if (text == Text && lines.Count > 0) return;
        Text = text;
        if (!lines.Any(l => l.Text.Text == text))
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap, Opacity = 0 };
            font.Apply(block);
            Children.Add(block);
            lines.Add((block, new LookNumber(motion, 0)));
        }
        foreach (var (block, opacity) in lines)
        {
            var target = block.Text == text ? 1 : 0;
            if (animate) opacity.To(target, LookCurve.Ease(0.2)); else opacity.Jump(target);
        }
    }

    public void Refresh(Color color)
    {
        var brush = LookDraw.Brush(color);
        foreach (var (block, opacity) in lines.ToArray())
        {
            if (block.Text != Text && !opacity.IsMoving && opacity.Value == 0)
            {
                Children.Remove(block);
                lines.RemoveAll(l => l.Text == block);
                continue;
            }
            block.Opacity = opacity.Value;
            block.Foreground = brush;
        }
    }
}

/// <summary>SPEC 2.2: the mini window, a still picture that re-tints with the theme and the accent.</summary>
internal sealed class MiniWindow : Border
{
    private readonly LookCard card;
    private readonly MiniScene scene;

    public MiniWindow(LookCard card)
    {
        this.card = card;
        Height = LookCard.MiniHeight;
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        BoxShadow = BoxShadows.Parse("0 4 14 0 #1F000000"); // 0 4 14 rgba(0,0,0,.12)
        Child = scene = new MiniScene(card);
    }

    public void Refresh()
    {
        Background = LookDraw.Brush(card.Surface.Value);
        scene.InvalidateVisual();
    }
}

internal sealed class MiniScene(LookCard card) : Control
{
    private static Rect R(double x, double y, double w, double h) => new(x, y, w, h);

    public override void Render(DrawingContext context)
    {
        var ink = card.Ink;
        var accent = card.Accent.Value;
        IBrush Ink(double alpha) => LookDraw.Brush(LookDraw.WithAlpha(ink, alpha));
        void Bar(double x, double y, double w, IBrush fill) => context.DrawRectangle(fill, null, R(x, y, w, 5), 3, 3);

        // Sidebar: 112 wide, ink 5 %, its right edge 1 px ink 8 %.
        context.DrawRectangle(Ink(0.05), null, R(0, 0, 112, 128));
        context.DrawRectangle(Ink(0.08), null, R(111, 0, 1, 128));
        // Items 96 x 17 at y 10, 31, 52, 73, 94; the current one (2) filled ink 10 %.
        context.DrawRectangle(Ink(0.10), null, R(8, 31, 96, 17), 5, 5);
        context.DrawRectangle(Ink(0.55), null, R(14, 14.5, 8, 8), 2.5, 2.5);
        Bar(27, 16, 40, Ink(0.46));
        Bar(14, 37, 60, Ink(0.46));
        Bar(14, 58, 48, Ink(0.24));
        Bar(14, 79, 66, Ink(0.24));
        Bar(14, 100, 36, Ink(0.24));
        // The user's bubble: right 12, top 12, 84 x 18, accent 24 %, a 64 bar inside in ink 34 %.
        context.DrawRectangle(LookDraw.Brush(LookDraw.WithAlpha(accent, 0.24)), null, R(400, 12, 84, 18), 9, 9);
        Bar(410, 18.5, 64, Ink(0.34));
        // The reply: from (126, 42), 356 wide, lines 7 apart.
        Bar(126, 42, 356 * 0.94, Ink(0.20));
        Bar(126, 54, 356 * 0.34, Ink(0.20));
        Bar(126 + 356 * 0.34 + 4, 54, 356 * 0.22, LookDraw.Brush(accent));
        Bar(126 + 356 * 0.56 + 8, 54, 356 * 0.30, Ink(0.20));
        Bar(126, 66, 356 * 0.56, Ink(0.20));
        // The composer: (124, 84) 360 x 34, radius 12, ink 6 % with an inner 1 px ink 11 %.
        context.DrawRectangle(Ink(0.06), null, R(124, 84, 360, 34), 12, 12);
        context.DrawRectangle(null, new Pen(Ink(0.11), 1), R(124.5, 84.5, 359, 33), 11.5, 11.5);
        Bar(135, 98.5, 247 * 0.46, Ink(0.20));
        // The effort tube: 58 x 10 at (390, 96), the track and its .5 edge, filled 74 % from accent 62 % to accent.
        context.DrawRectangle(LookDraw.Brush(card.Track), null, R(390, 96, 58, 10), 5, 5);
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(LookDraw.WithAlpha(accent, 0.62), 0), new GradientStop(accent, 1) },
        };
        context.DrawRectangle(fill, null, R(390, 96, 58 * 0.74, 10), 5, 5);
        context.DrawRectangle(null, new Pen(Ink(0.18), 0.5), R(390.25, 96.25, 57.5, 9.5), 4.75, 4.75);
        // The send button: a 22 circle at (456, 90) and its up arrow in --aon.
        context.DrawEllipse(LookDraw.Brush(accent), null, new Point(467, 101), 11, 11);
        using (context.PushTransform(Matrix.CreateTranslation(462, 96)))
            context.DrawGeometry(null, LookDraw.Pen(card.OnAccent.Value, 1.5), Arrow);
        // The window's inner 1 px edge in the window divider colour.
        context.DrawRectangle(null, new Pen(LookDraw.Brush(card.Divider), 1), R(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1), 9.5, 9.5);
    }

    private static readonly Geometry Arrow = StreamGeometry.Parse("M5,8.5 V1.8 M2.2,4.4 L5,1.6 L7.8,4.4");
}

/// <summary>SPEC 2.3: System | Light | Dark | the named theme or "More themes", on a 28 tall track.</summary>
internal sealed class ThemeSegments : Panel
{
    private static readonly AppTheme[] Bases = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    private readonly LookCard card;

    public ThemeSegments(LookCard card)
    {
        this.card = card;
        Height = 28;
        HorizontalAlignment = HorizontalAlignment.Right;
        Buttons = [new SegmentButton(card, 0, "System"), new SegmentButton(card, 1, "Light"), new SegmentButton(card, 2, "Dark"), new SegmentButton(card, 3, "More themes")];
        Track = new SegmentTrack(card, this);
        Children.Add(Track);
        foreach (var button in Buttons) Children.Add(button);
        // The control's width moves with a segment's (SPEC 6.6, 6.7) and its right edge stays put: laid out unrounded, so it
        // glides there to the last frame instead of settling with a whole-pixel step (layout rounding takes a size up).
        UseLayoutRounding = false;
        Track.UseLayoutRounding = false;
        foreach (var button in Buttons) button.UseLayoutRounding = false;
        PillLeft = new LookNumber(card.Motion, 2);
        PillWidth = new LookNumber(card.Motion, 0);
    }

    internal SegmentButton[] Buttons { get; }

    internal SegmentTrack Track { get; }

    internal LookNumber PillLeft { get; }

    internal LookNumber PillWidth { get; }

    internal int Selected => card.IsNamed ? 3 : Array.IndexOf(Bases, card.CardTheme);

    public void Apply(bool animate)
    {
        var named = card.IsNamed;
        Buttons[3].SetLabel(named ? card.CardTheme.DisplayName() : "More themes", animate);
        var selected = Selected;
        for (var i = 0; i < Buttons.Length; i++) Buttons[i].Apply(animate, i == selected);
        double left = 2;
        for (var i = 0; i < selected; i++) left += Buttons[i].SegmentWidth.Target;
        var width = Buttons[selected].SegmentWidth.Target;
        if (animate) { PillLeft.To(left, LookCurve.Ez(0.3)); PillWidth.To(width, LookCurve.Ez(0.3)); }
        else { PillLeft.Jump(left); PillWidth.Jump(width); }
    }

    public void Refresh()
    {
        InvalidateMeasure();
        Track.InvalidateVisual();
        foreach (var button in Buttons) button.InvalidateVisual();
    }

    internal void Activate(int index)
    {
        if (index == 3) card.Menu.Toggle(keyboard: false);
        else card.PickTheme(Bases[index]);
    }

    /// <summary>The control's drawn width now: the segments and the track's 2 each side.</summary>
    internal double ContentWidth => 4 + Buttons.Sum(b => b.SegmentWidth.Value);

    // The panel asks for whole pixels (its row's grid rounds the column it sits in, and a column narrower than the control
    // would squeeze it) and draws the track and segments against its right edge, unrounded.
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ContentWidth;
        foreach (var button in Buttons) button.Measure(new Size(button.SegmentWidth.Value, 24));
        Track.Measure(new Size(width, 28));
        return new Size(Math.Ceiling(width - 1e-6), 28);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = ContentWidth;
        var left = finalSize.Width - width;
        Track.Arrange(new Rect(left, 0, width, 28));
        var x = left + 2;
        foreach (var button in Buttons)
        {
            button.Arrange(new Rect(x, 2, button.SegmentWidth.Value, 24));
            x += button.SegmentWidth.Value;
        }
        return finalSize;
    }

}

/// <summary>The segmented control's track and selection pill, drawn under the segments.</summary>
internal sealed class SegmentTrack(LookCard card, ThemeSegments segments) : Control
{
    private static readonly BoxShadows PillShadow = BoxShadows.Parse("0 1 2 0 #2E000000");

    public override void Render(DrawingContext context)
    {
        // The track: radius 14, --cap.
        context.DrawRectangle(LookDraw.Brush(card.Cap), null, new Rect(Bounds.Size), 14, 14);
        // The pill: 24 tall, radius 12, --pill, shadow 0 1 2 rgba(0,0,0,.18) and an inner .5 --rim.
        var pill = new Rect(segments.PillLeft.Value, 2, segments.PillWidth.Value, 24);
        if (pill.Width <= 0) return;
        context.DrawRectangle(LookDraw.Brush(card.PillFill), null, new RoundedRect(pill, 12), PillShadow);
        context.DrawRectangle(null, new Pen(LookDraw.Brush(card.Rim), 0.5), pill.Deflate(0.25), 11.75, 11.75);
    }
}

/// <summary>One segment: its label (crossfading for the fourth), the fourth's chevron, hover, press and keyboard focus.</summary>
internal sealed class SegmentButton : Control, ICustomHitTest
{
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    private readonly LookCard card;
    private readonly List<(string Text, LookNumber Opacity)> labels = [];
    private bool pressed;
    private bool selected;

    public SegmentButton(LookCard card, int index, string label)
    {
        this.card = card;
        Index = index;
        Focusable = true;
        IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        SegmentWidth = new LookNumber(card.Motion, 0);
        LabelWidth = new LookNumber(card.Motion, 0);
        Lit = new LookNumber(card.Motion, 0);
        Scale = new LookNumber(card.Motion, 1);
        Outline = new LookNumber(card.Motion, 0);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.RadioButton);
        SetLabel(label, animate: false);
        Classes.CollectionChanged += (_, _) => Outline.To(Classes.Contains(":focus-visible") ? 1 : 0, LookCurve.Ease(0.12));
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty) ApplyLit(true); };
    }

    internal int Index { get; }

    internal LookNumber SegmentWidth { get; }

    internal LookNumber LabelWidth { get; }

    /// <summary>0 at --tx2, 1 at --tx (hovered or selected).</summary>
    internal LookNumber Lit { get; }

    internal LookNumber Scale { get; }

    internal LookNumber Outline { get; }

    internal string Label => labels.Count == 0 ? "" : labels.FirstOrDefault(l => l.Opacity.Target == 1).Text ?? labels[^1].Text;

    internal IEnumerable<(string Text, double Opacity)> Labels => labels.Select(l => (l.Text, l.Opacity.Value));

    internal bool IsSelected => selected;

    public void SetLabel(string text, bool animate)
    {
        if (!labels.Any(l => l.Text == text)) labels.Add((text, new LookNumber(card.Motion, 0)));
        foreach (var (t, opacity) in labels)
        {
            var target = t == text ? 1 : 0;
            if (animate) opacity.To(target, LookCurve.Ease(0.2)); else opacity.Jump(target);
        }
        labels.RemoveAll(l => l.Text != text && l.Opacity.Value == 0 && !l.Opacity.IsMoving);
        var labelWidth = LookDraw.Width(text, LookCard.SegmentFont); // the drawn label's box, centred with the chevron
        var width = LookCard.SegmentWidthFor(text, chevron: Index == 3); // the board's segment
        if (animate) { LabelWidth.To(labelWidth, LookCurve.Ez(0.3)); SegmentWidth.To(width, LookCurve.Ez(0.3)); }
        else { LabelWidth.Jump(labelWidth); SegmentWidth.Jump(width); }
        AutomationProperties.SetName(this, Index == 3 && card.IsNamed ? text : text);
    }

    public void Apply(bool animate, bool isSelected)
    {
        selected = isSelected;
        ApplyLit(animate);
    }

    private void ApplyLit(bool animate)
    {
        var target = selected || IsPointerOver ? 1 : 0;
        if (animate) Lit.To(target, LookCurve.Ease(0.15)); else Lit.Jump(target);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        e.Pointer.Capture(this);
        Scale.To(0.96, LookCurve.Ease(0.12));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed) return;
        pressed = false;
        e.Pointer.Capture(null);
        Scale.To(1, LookCurve.Ease(0.12));
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) card.Segments.Activate(Index);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Space or Key.Enter)) return;
        e.Handled = true;
        if (Index == 3) card.Menu.Toggle(keyboard: true);
        else card.Segments.Activate(Index);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var transform = Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateScale(Scale.Value, Scale.Value) * Matrix.CreateTranslation(centre.X, centre.Y);
        using var scaled = context.PushTransform(transform);
        var color = SwiftColor.PremultipliedMix(card.Tx2, card.Tx, Lit.Value);
        // Content: the label box, then (the fourth) 4 and the 7 x 5 chevron, centred in the segment.
        var box = LabelWidth.Value;
        var content = box + (Index == 3 ? 11 : 0);
        var x0 = (size.Width - content) / 2;
        using (context.PushClip(new Rect(size)))
        {
            foreach (var (text, opacity) in labels)
                LookDraw.Centred(context, text, LookCard.SegmentFont, LookDraw.WithAlpha(color, opacity.Value), new Point(x0 + box / 2, size.Height / 2));
            if (Index == 3)
                using (context.PushTransform(Matrix.CreateTranslation(x0 + box + 4, (size.Height - 5) / 2)))
                    context.DrawGeometry(null, LookDraw.Pen(LookDraw.WithAlpha(color, 0.7), 1.3), LookDraw.Chevron);
        }
        // Keyboard focus: 2 px --pac, 1 px outside, the segment's radius.
        if (Outline.Value > 0)
            context.DrawRectangle(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.Accent.Value, Outline.Value)), 2), new Rect(size).Inflate(2), 14, 14);
    }
}

/// <summary>One line of swatches (310 x 24): its ring, the swatches on the 26 pitch and, on the Classic line, More.</summary>
internal sealed class SwatchLine : Panel
{
    private readonly LookCard card;

    public SwatchLine(LookCard card, bool signature)
    {
        this.card = card;
        IsSignature = signature;
        Width = LookCard.LineWidth;
        Height = LookCard.SwatchSize;
        HorizontalAlignment = HorizontalAlignment.Right;
        Ring = new RingMark(card);
        Children.Add(Ring);
        var first = signature ? AccentPalette.ClassicCount : 0;
        var count = signature ? AccentPalette.All.Count - AccentPalette.ClassicCount : AccentPalette.ClassicCount;
        Swatches = Enumerable.Range(first, count).Select(i => new SwatchButton(card, i)).ToArray();
        foreach (var swatch in Swatches) Children.Add(swatch);
        if (!signature)
        {
            More = new MoreButton(card);
            Children.Add(More);
        }
    }

    internal bool IsSignature { get; }

    internal RingMark Ring { get; }

    internal SwatchButton[] Swatches { get; }

    internal MoreButton? More { get; }

    public void Refresh()
    {
        Ring.InvalidateVisual();
        foreach (var swatch in Swatches) swatch.InvalidateVisual();
        More?.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(LookCard.LineWidth, LookCard.SwatchSize));
        return new Size(LookCard.LineWidth, LookCard.SwatchSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Ring.Arrange(new Rect(0, 0, LookCard.LineWidth, LookCard.SwatchSize));
        for (var j = 0; j < Swatches.Length; j++) Swatches[j].Arrange(new Rect(j * LookCard.Pitch, 0, LookCard.SwatchSize, LookCard.SwatchSize));
        More?.Arrange(new Rect(Swatches.Length * LookCard.Pitch, 0, 2 * LookCard.Pitch - 2, LookCard.SwatchSize));
        return finalSize;
    }
}

/// <summary>A line's selection ring (SPEC 2.5, 6.1): 24, a 1.5 inner stroke in --ring, at 26 x its index.</summary>
internal sealed class RingMark : Control
{
    private readonly LookCard card;

    public RingMark(LookCard card)
    {
        this.card = card;
        IsHitTestVisible = false;
        X = new LookNumber(card.Motion, 0);
        Fade = new LookNumber(card.Motion, 0);
        Scale = new LookNumber(card.Motion, 0.8);
    }

    internal LookNumber X { get; }

    internal LookNumber Fade { get; }

    internal LookNumber Scale { get; }

    public override void Render(DrawingContext context)
    {
        if (Fade.Value <= 0) return;
        var centre = new Point(X.Value + 12, 12);
        var color = LookDraw.WithAlpha(card.RingColor, Fade.Value);
        context.DrawEllipse(null, new Pen(LookDraw.Brush(color), 1.5 * Scale.Value), centre, (12 - 0.75) * Scale.Value, (12 - 0.75) * Scale.Value);
    }
}

/// <summary>A swatch (SPEC 2.5, 5): a 24 hit area, the 16 dot, the Windows dot, hover, press and keyboard focus.</summary>
internal sealed class SwatchButton : Control, ICustomHitTest
{
    // The 24 circle (the board's button, border-radius 50 %).
    public bool HitTest(Point point) => Math.Sqrt((point.X - 12) * (point.X - 12) + (point.Y - 12) * (point.Y - 12)) <= 12;

    private readonly LookCard card;
    private bool pressed;
    private bool picked;

    public SwatchButton(LookCard card, int index)
    {
        this.card = card;
        Index = index;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Fill = new LookColor(card.Motion, Colors.Transparent);
        WindowsDot = new LookNumber(card.Motion, 0);
        Scale = new LookNumber(card.Motion, 1);
        Outline = new LookNumber(card.Motion, 0);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.RadioButton);
        Classes.CollectionChanged += (_, _) => Outline.To(Classes.Contains(":focus-visible") ? 1 : 0, LookCurve.Ease(0.12));
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsPointerOverProperty) return;
            if (IsPointerOver) card.SetHover(AccentPalette.All[Index].Id); else card.ClearHover(AccentPalette.All[Index].Id);
            if (!pressed) Scale.To(IsPointerOver ? 1.12 : 1, LookCurve.Ease(0.12));
        };
    }

    internal int Index { get; }

    internal LookColor Fill { get; }

    internal LookNumber WindowsDot { get; }

    internal LookNumber Scale { get; }

    internal LookNumber Outline { get; }

    internal bool Picked
    {
        get => picked;
        set => picked = value;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwatchPeer(this);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        e.Pointer.Capture(this);
        Scale.To(0.86, LookCurve.Ease(0.08));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed) return;
        pressed = false;
        e.Pointer.Capture(null);
        var inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        Scale.To(inside ? 1.12 : 1, LookCurve.Ease(0.12)); // back to the hover scale (the board's 0.12 s)
        if (inside) card.Pick(Index);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Right or Key.Left or Key.Up or Key.Down or Key.Home or Key.End)
        {
            e.Handled = true;
            card.Arrow(e.Key);
        }
        else if (e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            card.Pick(Index);
        }
    }

    public override void Render(DrawingContext context)
    {
        var centre = new Point(12, 12);
        var r = 8 * Scale.Value;
        var fill = Fill.Value;
        context.DrawEllipse(LookDraw.Brush(fill), null, centre, r, r);
        context.DrawEllipse(null, new Pen(LookDraw.Brush(card.Rim), 0.5 * Scale.Value), centre, r - 0.25 * Scale.Value, r - 0.25 * Scale.Value);
        if (WindowsDot.Value > 0)
            context.DrawEllipse(LookDraw.Brush(Color.FromArgb((byte)Math.Round(240 * WindowsDot.Value), 255, 255, 255)), null, centre, 2.5 * Scale.Value, 2.5 * Scale.Value);
        if (Outline.Value > 0)
            context.DrawEllipse(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.Accent.Value, Outline.Value)), 2), centre, 14, 14);
    }

    private sealed class SwatchPeer(SwatchButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;

        public bool IsSelected => owner.Picked;

        public ISelectionProvider? SelectionContainer => null;

        public void AddToSelection() => Select();

        public void RemoveFromSelection() { }

        public void Select() => owner.card.Pick(owner.Index);

        public void Invoke() => Select();
    }
}

/// <summary>SPEC 2.5, 6.2, 6.3: More, which folds the Signature line, and holds a Signature pick while the line is shut.</summary>
internal sealed class MoreButton : Control, ICustomHitTest
{
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    private readonly LookCard card;
    private readonly List<(string Text, LookNumber Opacity)> labels = [];
    private bool pressed;

    public MoreButton(LookCard card)
    {
        this.card = card;
        Focusable = true;
        IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        var m = card.Motion;
        LabelWidth = new LookNumber(m, 0);
        Chevron = new LookNumber(m, 0);
        RingOpacity = new LookNumber(m, 0);
        RingScale = new LookNumber(m, 0.9);
        DotWidth = new LookNumber(m, 0);
        DotMargin = new LookNumber(m, -3);
        DotOpacity = new LookNumber(m, 0);
        DotScale = new LookNumber(m, 0.4);
        DotColor = new LookColor(m, Colors.Transparent);
        ChevronOpacity = new LookNumber(m, 0.8);
        ChevronWidth = new LookNumber(m, 7);
        ChevronMargin = new LookNumber(m, 0);
        Hovered = new LookNumber(m, 0);
        Lit = new LookNumber(m, 0);
        Scale = new LookNumber(m, 1);
        Outline = new LookNumber(m, 0);
        AutomationProperties.SetName(this, "Signature accents");
        Classes.CollectionChanged += (_, _) => Outline.To(Classes.Contains(":focus-visible") ? 1 : 0, LookCurve.Ease(0.12));
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsPointerOverProperty) return;
            Hovered.To(IsPointerOver ? 1 : 0, LookCurve.Ease(0.12));
            ApplyLit(true);
            if (IsPointerOver) { if (!card.Open) card.SetHover("more"); }
            else card.ClearHover("more");
        };
    }

    internal LookNumber LabelWidth { get; }
    internal LookNumber Chevron { get; }
    internal LookNumber RingOpacity { get; }
    internal LookNumber RingScale { get; }
    internal LookNumber DotWidth { get; }
    internal LookNumber DotMargin { get; }
    internal LookNumber DotOpacity { get; }
    internal LookNumber DotScale { get; }
    internal LookColor DotColor { get; }
    internal LookNumber ChevronOpacity { get; }
    internal LookNumber ChevronWidth { get; }
    internal LookNumber ChevronMargin { get; }
    internal LookNumber Hovered { get; }
    internal LookNumber Lit { get; }
    internal LookNumber Scale { get; }
    internal LookNumber Outline { get; }

    internal IEnumerable<(string Text, double Opacity)> Labels => labels.Select(l => (l.Text, l.Opacity.Value));

    private bool held;

    public void Apply(bool animate, bool open, bool isHeld, Color? dot)
    {
        held = isHeld;
        var text = open ? "Less" : "More";
        if (!labels.Any(l => l.Text == text)) labels.Add((text, new LookNumber(card.Motion, 0)));
        foreach (var (t, opacity) in labels)
        {
            // In sequence: the old label out over 0-100 ms, the new one in over 80-200 ms.
            if (!animate) opacity.Jump(t == text ? 1 : 0);
            else if (t == text) opacity.To(1, LookCurve.Ease(0.12), 0.08);
            else opacity.To(0, LookCurve.Ease(0.1));
        }
        void N(LookNumber value, double target, Animation animation) { if (animate) value.To(target, animation); else value.Jump(target); }
        N(LabelWidth, LookDraw.Width(text, LookCard.MoreFont), LookCurve.Ez(0.3));
        N(Chevron, open ? 180 : 0, LookCurve.Ez(0.3));
        N(RingOpacity, isHeld ? 1 : 0, LookCurve.Ease(0.3));
        N(RingScale, isHeld ? 1 : 0.9, LookCurve.Ez(0.3));
        N(DotWidth, isHeld ? 6 : 0, LookCurve.Ez(0.3));
        N(DotMargin, isHeld ? 0 : -3, LookCurve.Ez(0.3));
        N(DotOpacity, isHeld ? 1 : 0, LookCurve.Ease(0.2));
        N(DotScale, isHeld ? 1 : 0.4, LookCurve.Ez(0.24));
        N(ChevronOpacity, isHeld ? 0 : 0.8, LookCurve.Ease(0.2));
        N(ChevronWidth, isHeld ? 0 : 7, LookCurve.Ez(0.3));
        N(ChevronMargin, isHeld ? -3 : 0, LookCurve.Ez(0.3));
        // The dot keeps the last picked colour while it fades out.
        if (dot is { } color) { if (animate) DotColor.To(color, LookCurve.Ease(0.32)); else DotColor.Jump(color); }
        ApplyLit(animate);
    }

    private void ApplyLit(bool animate)
    {
        var target = IsPointerOver || held ? 1 : 0;
        if (animate) Lit.To(target, LookCurve.Ease(0.12)); else Lit.Jump(target);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MorePeer(this);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        e.Pointer.Capture(this);
        Scale.To(0.94, LookCurve.Ease(0.08));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed) return;
        pressed = false;
        e.Pointer.Capture(null);
        Scale.To(1, LookCurve.Ease(0.12));
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) card.ToggleFold();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Space or Key.Enter)) return;
        e.Handled = true;
        card.ToggleFold();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size; // 50 x 24
        // The held ring: 54 x 24 (2 beyond each side), radius 12, 1.5 inner stroke --ring, scaling about its centre.
        if (RingOpacity.Value > 0)
        {
            var s = RingScale.Value;
            var ring = new Rect(-2, 0, size.Width + 4, size.Height);
            var c = ring.Center;
            using var t = context.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(c.X, c.Y));
            context.DrawRectangle(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.RingColor, RingOpacity.Value)), 1.5), ring.Deflate(0.75), 11.25, 11.25);
        }
        // Keyboard focus: 2 px --pac, 1 px outside, radius 12.
        if (Outline.Value > 0)
            context.DrawRectangle(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.Accent.Value, Outline.Value)), 2), new Rect(size).Inflate(2), 14, 14);
        // The capsule: 46 x 16 centred, radius 8, --cap or --caph, pressed to .94.
        var capsule = new Rect((size.Width - 46) / 2, (size.Height - 16) / 2, 46, 16);
        var centre = capsule.Center;
        using var scale = context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateScale(Scale.Value, Scale.Value) * Matrix.CreateTranslation(centre.X, centre.Y));
        context.DrawRectangle(LookDraw.Brush(SwiftColor.PremultipliedMix(card.Cap, card.CapHover, Hovered.Value)), null, capsule, 8, 8);
        // Its content, centred, gap 3: [dot slot] [label box] [chevron].
        var dotW = DotWidth.Value;
        var label = LabelWidth.Value;
        var chevronW = ChevronWidth.Value;
        var content = dotW + DotMargin.Value + 3 + label + 3 + ChevronMargin.Value + chevronW;
        var x = centre.X - content / 2;
        var color = SwiftColor.PremultipliedMix(card.Tx2, card.Tx, Lit.Value);
        if (DotOpacity.Value > 0)
        {
            var d = DotScale.Value * 3;
            var dc = new Point(x + dotW / 2, centre.Y);
            var dotColor = LookDraw.WithAlpha(DotColor.Value, DotOpacity.Value);
            context.DrawEllipse(LookDraw.Brush(dotColor), null, dc, d, d);
            context.DrawEllipse(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.Rim, DotOpacity.Value)), 0.5 * DotScale.Value), dc, d - 0.25 * DotScale.Value, d - 0.25 * DotScale.Value);
        }
        x += dotW + DotMargin.Value + 3;
        foreach (var (text, opacity) in labels)
            LookDraw.Centred(context, text, LookCard.MoreFont, LookDraw.WithAlpha(color, opacity.Value), new Point(x + label / 2, centre.Y));
        x += label + 3 + ChevronMargin.Value;
        if (ChevronOpacity.Value > 0 && chevronW > 0)
        {
            using var clip = context.PushClip(new Rect(x, 0, chevronW, size.Height));
            var cc = new Point(x + 3.5, centre.Y);
            using var turn = context.PushTransform(Matrix.CreateTranslation(-3.5, -2.5) * Matrix.CreateRotation(Chevron.Value * Math.PI / 180) * Matrix.CreateTranslation(cc.X, cc.Y));
            context.DrawGeometry(null, LookDraw.Pen(LookDraw.WithAlpha(color, ChevronOpacity.Value), 1.3), LookDraw.Chevron);
        }
    }

    private sealed class MorePeer(MoreButton owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        public ExpandCollapseState ExpandCollapseState => owner.card.Open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => false;

        public void Expand() { if (!owner.card.Open) owner.card.ToggleFold(); }

        public void Collapse() { if (owner.card.Open) owner.card.ToggleFold(); }

        public void Invoke() => owner.card.ToggleFold();
    }
}

/// <summary>SPEC 2.6, 6.2: the Signature line's fold, 0 tall shut and 36 open; the line inside never shrinks.</summary>
internal sealed class SignatureFold : Panel
{
    private readonly Grid line;

    public SignatureFold(LookCard card, SwatchLine swatches)
    {
        ClipToBounds = true;
        FoldHeight = new LookNumber(card.Motion, 0);
        LineOpacity = new LookNumber(card.Motion, 0);
        LineY = new LookNumber(card.Motion, -6);
        Label = new TextBlock { Text = "Signature", VerticalAlignment = VerticalAlignment.Center };
        LookCard.SignatureFont.Apply(Label);
        swatches.VerticalAlignment = VerticalAlignment.Top;
        line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 0, Chrome.RowControlTrailingPadding, 12),
            Height = LookCard.FoldOpen,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var labelBox = new Border { Height = LookCard.SwatchSize, VerticalAlignment = VerticalAlignment.Top, Child = Label };
        Grid.SetColumn(swatches, 1);
        line.Children.Add(labelBox);
        line.Children.Add(swatches);
        Children.Add(line);
        this.card = card;
    }

    private readonly LookCard card;

    internal LookNumber FoldHeight { get; }

    internal LookNumber LineOpacity { get; }

    internal LookNumber LineY { get; }

    internal TextBlock Label { get; }

    internal Control Line => line;

    public void Refresh()
    {
        line.Opacity = LineOpacity.Value;
        line.RenderTransform = new TranslateTransform(0, LineY.Value);
        Label.Foreground = LookDraw.Brush(card.Tx2);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        line.Measure(new Size(availableSize.Width, LookCard.FoldOpen + 12));
        // ED-253: the board lays the fold out unrounded and its frames draw what follows it at the nearest pixel (+1, +4, +8,
        // +12 for 1.38, 4.05, 7.94, 12.45, canvas-frames 02); Avalonia's layout rounding takes a desired size up, so the
        // height is given already rounded to the nearest device pixel.
        var scale = LayoutHelper.GetLayoutScale(this);
        return new Size(line.DesiredSize.Width, Math.Round(FoldHeight.Value * scale, MidpointRounding.AwayFromZero) / scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        line.Arrange(new Rect(0, 0, finalSize.Width, LookCard.FoldOpen));
        return finalSize;
    }
}

/// <summary>SPEC 2.4, 6.8: the menu of the 23 named themes under segment 4.</summary>
internal sealed class ThemeMenu : Border
{
    private readonly LookCard card;
    private readonly ScrollViewer list;
    private readonly StackPanel items = new();
    private TopLevel? watched;
    private bool isOpen;

    public ThemeMenu(LookCard card)
    {
        this.card = card;
        Width = LookCard.MenuWidth;
        Padding = new Thickness(6);
        CornerRadius = new CornerRadius(12);
        IsVisible = false;
        Opacity = 0;
        RenderTransformOrigin = new RelativePoint(LookCard.MenuWidth - 44, 0, RelativeUnit.Absolute);
        OpacityValue = new LookNumber(card.Motion, 0);
        Lift = new LookNumber(card.Motion, -4);
        Grow = new LookNumber(card.Motion, 0.96);
        TopFade = new LookNumber(card.Motion, 0);
        BottomFade = new LookNumber(card.Motion, 0);
        AutomationProperties.SetName(this, "Theme");
        foreach (var scheme in new[] { (Title: "Dark", Variant: ThemeVariant.Dark), (Title: "Light", Variant: ThemeVariant.Light) })
        {
            var header = new TextBlock { Text = scheme.Title, Margin = new Thickness(8, 6, 8, 3), Height = 14 };
            LookCard.HeaderFont.Apply(header);
            items.Children.Add(header);
            foreach (var theme in SwiftEnum<AppTheme>.AllCases.Where(t => t.Spec().Accent is not null && t.Scheme() == scheme.Variant))
                items.Children.Add(new MenuRow(card, theme));
        }
        list = new ScrollViewer
        {
            Template = new FuncControlTemplate<ScrollViewer>((viewer, scope) => new ScrollContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] }.RegisterInNameScope(scope)),
            Content = items,
            MaxHeight = LookCard.MenuListMax,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        };
        list.ScrollChanged += (_, _) => Fades(animate: true);
        Child = list;
        AddHandler(KeyDownEvent, OnMenuKey, RoutingStrategies.Bubble);
    }

    internal LookNumber OpacityValue { get; }
    internal LookNumber Lift { get; }
    internal LookNumber Grow { get; }
    internal LookNumber TopFade { get; }
    internal LookNumber BottomFade { get; }

    internal ScrollViewer List => list;

    internal MenuRow[] Rows => items.Children.OfType<MenuRow>().ToArray();

    internal TextBlock[] Headers => items.Children.OfType<TextBlock>().ToArray();

    public bool IsOpen => isOpen;

    public void Toggle(bool keyboard)
    {
        if (isOpen) Close();
        else OpenMenu(keyboard);
    }

    private void OpenMenu(bool keyboard)
    {
        isOpen = true;
        IsVisible = true;
        OpacityValue.To(1, LookCurve.Ease(0.2));
        Lift.To(0, LookCurve.PopIn(0.22));
        Grow.To(1, LookCurve.PopIn(0.22));
        Apply(animate: true);
        card.Refresh();
        // On open the list jumps so the current theme's row is 64 below its top (SPEC 2.4).
        UpdateLayout();
        var current = Rows.FirstOrDefault(r => r.RowTheme == card.CardTheme);
        var top = current is null ? 0 : Math.Max(0, (current.TranslatePoint(default, items) ?? default).Y - 64);
        list.Offset = new Vector(0, Math.Min(top, Math.Max(0, list.Extent.Height - list.Viewport.Height)));
        Fades(animate: false);
        watched = TopLevel.GetTopLevel(this);
        if (watched is not null)
        {
            watched.AddHandler(PointerPressedEvent, OnOutsidePress, RoutingStrategies.Tunnel, handledEventsToo: true);
            watched.AddHandler(KeyDownEvent, OnEscape, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        // ED-253: the Settings panel's own Escape (SettingsOverlay, ahead of every handler here) closes the menu, not itself.
        WindowTransients.Show(this, this, EscapeClose);
        if (keyboard) (current ?? Rows[0]).Focus(NavigationMethod.Directional);
    }

    public void Close(bool immediately = false)
    {
        if (!isOpen && !immediately) return;
        isOpen = false;
        WindowTransients.Hide(this);
        if (watched is not null)
        {
            watched.RemoveHandler(PointerPressedEvent, OnOutsidePress);
            watched.RemoveHandler(KeyDownEvent, OnEscape);
            watched = null;
        }
        if (immediately || card.Motion.Still)
        {
            OpacityValue.Jump(0); Lift.Jump(-4); Grow.Jump(0.96);
        }
        else
        {
            OpacityValue.To(0, LookCurve.Ease(0.15));
            Lift.To(-4, LookCurve.Ease(0.15));
            Grow.To(0.96, LookCurve.Ease(0.15));
        }
        card.Refresh();
    }

    private void OnOutsidePress(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (this.IsVisualAncestorOf(source) || source == this || card.Segments.Buttons[3].IsVisualAncestorOf(source) || source == card.Segments.Buttons[3])) return;
        Close();
    }

    private void OnEscape(object? sender, KeyEventArgs e)
    {
        if (!isOpen) return; // closed by the window's Escape already (ED-253)
        if (e.Key == Key.Tab)
        {
            // Tab closes the menu (focus never wanders off while it hangs open); from inside it, focus goes back to segment
            // 4, the way Escape returns it, and the next Tab goes on from there.
            var inside = Rows.Any(r => r.IsFocused);
            Close();
            if (!inside) return;
            e.Handled = true;
            card.Segments.Buttons[3].Focus(NavigationMethod.Tab);
            return;
        }
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        EscapeClose();
    }

    // SPEC 7: Escape closes the menu and returns focus to segment 4.
    private void EscapeClose()
    {
        Close();
        card.Segments.Buttons[3].Focus(NavigationMethod.Directional);
    }

    private void OnMenuKey(object? sender, KeyEventArgs e)
    {
        var rows = Rows;
        var index = Array.FindIndex(rows, r => r.IsFocused);
        if (index < 0) return;
        switch (e.Key)
        {
            case Key.Down: e.Handled = true; Focus(rows[Math.Min(index + 1, rows.Length - 1)]); break;
            case Key.Up: e.Handled = true; Focus(rows[Math.Max(index - 1, 0)]); break;
            case Key.Enter or Key.Space: e.Handled = true; card.PickTheme(rows[index].RowTheme); card.Segments.Buttons[3].Focus(NavigationMethod.Directional); break;
        }
    }

    // A focused row comes into view (the board's scrollIntoView, nearest, smooth: here over 0.2 s, CSS ease).
    private void Focus(MenuRow row)
    {
        row.Focus(NavigationMethod.Directional);
        var top = (row.TranslatePoint(default, items) ?? default).Y;
        var offset = list.Offset.Y;
        var view = list.Viewport.Height;
        var target = top < offset ? top : top + row.Bounds.Height > offset + view ? top + row.Bounds.Height - view : offset;
        if (target == offset) return;
        scroll ??= new LookNumber(card.Motion, offset);
        scroll.Jump(offset);
        scroll.To(target, LookCurve.Ease(0.2));
        card.Refresh();
    }

    private LookNumber? scroll;

    private void Fades(bool animate)
    {
        var top = list.Offset.Y > 1 ? 16 : 0;
        var bottom = list.Offset.Y + list.Viewport.Height >= list.Extent.Height - 1 ? 0 : 22;
        if (animate && !card.Motion.Still) { TopFade.To(top, LookCurve.Ease(0.2)); BottomFade.To(bottom, LookCurve.Ease(0.2)); }
        else { TopFade.Jump(top); BottomFade.Jump(bottom); }
        card.Refresh();
    }

    public void Apply(bool animate)
    {
        foreach (var row in Rows) row.Apply(animate, row.RowTheme == card.CardTheme);
    }

    public void Refresh()
    {
        Opacity = OpacityValue.Value;
        if (!isOpen && OpacityValue.Value <= 0 && !OpacityValue.IsMoving) IsVisible = false;
        IsHitTestVisible = isOpen;
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(Grow.Value, Grow.Value), new TranslateTransform(0, Lift.Value) } };
        Background = LookDraw.Brush(card.PopFill);
        BoxShadow = new BoxShadows(new BoxShadow { Spread = 1, Color = card.PopEdge },
            [new BoxShadow { OffsetY = 16, Blur = 40, Color = Color.FromArgb(82, 0, 0, 0) }, new BoxShadow { OffsetY = 2, Blur = 8, Color = Color.FromArgb(41, 0, 0, 0) }]);
        var height = Math.Max(1, list.Bounds.Height);
        list.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Black, Math.Clamp(TopFade.Value / height, 0, 1)),
                new GradientStop(Colors.Black, Math.Clamp(1 - BottomFade.Value / height, 0, 1)),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        if (scroll is { IsMoving: true } s) list.Offset = new Vector(0, s.Value);
        foreach (var header in Headers) header.Foreground = LookDraw.Brush(card.Tx2);
        foreach (var row in Rows) row.Refresh();
    }
}

/// <summary>A row of the theme menu (SPEC 2.4, 5): the check, the theme's swatch (the source's, AppTheme.swift:146-176), its name.</summary>
internal sealed class MenuRow : Panel
{
    private readonly LookCard card;
    private bool pressed;

    public MenuRow(LookCard card, AppTheme theme)
    {
        this.card = card;
        RowTheme = theme;
        Height = 28;
        Focusable = true;
        IsTabStop = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        Background = Brushes.Transparent;
        Face = new MenuRowFace(card, this);
        Children.Add(Face);
        var swatch = theme.Swatch();
        swatch.Width = 16;
        swatch.Height = 16;
        Children.Add(swatch);
        RenderTransformOrigin = RelativePoint.Center;
        Hover = new LookNumber(card.Motion, 0);
        Press = new LookNumber(card.Motion, 0);
        Check = new LookNumber(card.Motion, 0);
        CheckScale = new LookNumber(card.Motion, 0.6);
        Outline = new LookNumber(card.Motion, 0);
        AutomationProperties.SetName(this, theme.DisplayName());
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.MenuItem);
        Classes.CollectionChanged += (_, _) => Outline.To(Classes.Contains(":focus-visible") ? 1 : 0, LookCurve.Ease(0.12));
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty) Hover.To(IsPointerOver ? 1 : 0, LookCurve.Ease(0.1)); };
    }

    internal AppTheme RowTheme { get; }
    internal MenuRowFace Face { get; }
    internal LookNumber Hover { get; }
    internal LookNumber Press { get; }
    internal LookNumber Check { get; }
    internal LookNumber CheckScale { get; }
    internal LookNumber Outline { get; }

    public void Apply(bool animate, bool current)
    {
        if (animate) { Check.To(current ? 1 : 0, LookCurve.Ease(0.16)); CheckScale.To(current ? 1 : 0.6, LookCurve.Ez(0.24)); }
        else { Check.Jump(current ? 1 : 0); CheckScale.Jump(current ? 1 : 0.6); }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 252 : availableSize.Width;
        Face.Measure(new Size(width, 28));
        Children[1].Measure(new Size(16, 16));
        return new Size(width, 28);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Face.Arrange(new Rect(finalSize));
        Children[1].Arrange(new Rect(8 + 14 + 8, 6, 16, 16));
        return finalSize;
    }

    /// <summary>Draws what the values hold (the press scales the whole row, .99).</summary>
    public void Refresh()
    {
        var s = 1 - 0.01 * Press.Value;
        RenderTransform = s == 1 ? null : new ScaleTransform(s, s);
        Face.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        e.Pointer.Capture(this);
        Press.To(1, LookCurve.Ease(0.1));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed) return;
        pressed = false;
        e.Pointer.Capture(null);
        Press.To(0, LookCurve.Ease(0.1));
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) card.PickTheme(RowTheme);
        e.Handled = true;
    }

}

/// <summary>A menu row's fill, check, name and focus outline, under its swatch.</summary>
internal sealed class MenuRowFace(LookCard card, MenuRow row) : Control
{
    private LookNumber Hover => row.Hover;
    private LookNumber Press => row.Press;
    private LookNumber Check => row.Check;
    private LookNumber CheckScale => row.CheckScale;
    private LookNumber Outline => row.Outline;
    private AppTheme RowTheme => row.RowTheme;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var rect = new Rect(size);
        var fill = SwiftColor.PremultipliedMix(LookDraw.WithAlpha(card.RowHover, Hover.Value), card.RowPress, Press.Value);
        if (fill.A > 0) context.DrawRectangle(LookDraw.Brush(fill), null, rect, 7, 7);
        if (Check.Value > 0)
        {
            var c = new Point(8 + 7, 14);
            var cs = CheckScale.Value;
            using var t = context.PushTransform(Matrix.CreateTranslation(-7, -6) * Matrix.CreateScale(cs, cs) * Matrix.CreateTranslation(c.X, c.Y));
            context.DrawGeometry(null, LookDraw.Pen(LookDraw.WithAlpha(card.Tx, Check.Value), 1.8), CheckMark);
        }
        var resolved = LookCard.RowFont.Resolve();
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(RowTheme.DisplayName(), LookDraw.Face(LookCard.RowFont), resolved.FontSize, LookDraw.Brush(card.Tx));
        layout.Draw(context, new Point(8 + 14 + 8 + 16 + 8, (size.Height - layout.Height) / 2));
        if (Outline.Value > 0)
            context.DrawRectangle(null, new Pen(LookDraw.Brush(LookDraw.WithAlpha(card.Accent.Value, Outline.Value)), 2), rect.Deflate(1), 6, 6);
    }

    private static readonly Geometry CheckMark = StreamGeometry.Parse("M2.6,6.3 L5.5,9.2 L11.4,2.8");
}

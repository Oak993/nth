using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source: DroppyCode/UI/Chrome/ChromeSegmentedPicker.swift (B2 1fbf81e), :1-82.
// Records: PR-THEME-184, UI-THEME-054, UI-THEME-055. Every expected value is transcribed from the Swift and the
// records, never from a C# implementation:
//   ChromeSegmentedPicker.swift:8-13   the option: value, title, symbol defaulting to nil
//   :20-27                             two initialisers; the (value, title) pairs become options with no symbol
//   :30-35                             HStack(spacing: 2), one segment per option in order, isSelected is
//                                      option.value == selection, the tap runs withAnimation(Chrome.hover) { selection = option.value }
//   :37-43                             .padding(.horizontal, 3), .frame(height: Chrome.capsuleContentHeight),
//                                      .chromeGlassCapsule(), .fixedSize(), .accessibilityElement(children: .contain),
//                                      .background { NoWindowDragArea() }
//   :56-67                             segment: callout, semibold when chosen else medium; primaryText when chosen else
//                                      secondaryText; lineLimit(1); horizontal padding 9; minWidth 26; height 22; a
//                                      continuous capsule filled with accent.opacity(0.22) when chosen, else
//                                      Chrome.overlay(isHovering ? 0.08 : 0); the capsule is the content shape
//   :69-73                             hover animated with Chrome.hover; selected trait when chosen; accessibility
//                                      label Text(verbatim: title)
//   :74-81                             Image(systemName: symbol) when a symbol is given, else Text(verbatim: title)
//   Chrome.swift:33 capsuleContentHeight = 28; :71-73 overlay(o) = primaryText.opacity(o); :79 secondaryText =
//   primaryText.opacity(0.7) (ED-062); :98 hover = .easeOut(duration: 0.1).
//   Text size: .callout is 12 pt (ED-059, B1:DroppyCode/UI/Common/ChatZoom.swift:52), face Inter (OD-0009, ED-091).
//
// Port shape these tests fix (R-NAME-1, R-NAME-5, R-VAL-1, ED-095, R-VIEW-1, R-VIEW-7):
//   ChromeSegmentedOption<TValue>: an immutable record, constructor (value, title, symbol = null), properties Value,
//     Title, Symbol. TValue must admit int? (the Hydra call site, HydraSettingsPage.swift:66-67, binds Int?).
//   ChromeSegmentedPicker<TValue>: a Decorator (ED-095) with constructors (options, selection) taking a list of
//     ChromeSegmentedOption<TValue>, or a list of (TValue, string) pairs, and a Binding<TValue>.
//   The private segment is found through UI Automation, not by its C# type: a segment is a control whose automation
//     peer is a Button (Swift Button, :55) named by its title (:72). The selected trait (:71) is UIA SelectionItem
//     IsSelected, the Windows form of .isSelected. The capsule a segment draws is the Border of the segment's own
//     size whose corner radius is half its height (a capsule), and its fill is that Border's Background.
// Animated changes are driven by the F.FrameClock manual frame source (ED-098 (1)), never by the wall clock.
// Glass material (OD-0005) and the symbol's Lucide glyph (OD-0010) are not asserted here: see the unit report.
[Collection("Headless")]
public sealed class ChromeSegmentedPickerTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:51,63,67-69
    public void Review_selection_changes_preserve_hover_until_pointer_exit() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var clockSource = SwiftClock.Provider.Override(clock);
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(HydraOptions(), new Binding<int?>(() => cell.Value, v => cell.Value = v));
        var window = Show(picker);
        var two = Segment(picker, "2");
        window.MouseMove(Centre(two, window));
        Dispatcher.UIThread.RunJobs();
        Frames(frames, 20, clock: clock);
        cell.Value = 2;
        Dispatcher.UIThread.RunJobs();
        cell.Value = 1;
        Dispatcher.UIThread.RunJobs();
        Frames(frames, 20, clock: clock);
        AssertColor(Chrome.PrimaryText.Opacity(0.08).Resolve(window.ActualThemeVariant), Fill(two));
        window.Close();
    });

    private const double StripHeight = 28;       // Chrome.swift:33 capsuleContentHeight
    private const double StripPadding = 3;       // ChromeSegmentedPicker.swift:37
    private const double SegmentSpacing = 2;     // :30
    private const double SegmentHeight = 22;     // :62
    private const double SegmentMinWidth = 26;   // :61
    private const double SegmentPadding = 9;     // :60
    private const double LayoutRounding = 0.5;   // UseLayoutRounding at scale 1 rounds each size to a whole pixel

    private static Window Show(Control content, ThemeVariant? theme = null)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        if (theme is not null)
            window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // clock is the ManualTimeProvider bound to SwiftClock.Provider, when a test needs elapsed time to move in
    // lockstep with the frames (AnimationClocks.Frame's pattern, Fixtures/AnimationFixtures.cs): AnimatedColor.Set
    // (Port/PlainControls.cs:207-233) measures a span's progress on SwiftClock.Now, never on the frame callback's own
    // TimeSpan (Port/FrameClock.cs:7-10), so a test that asserts a specific mid-animation progress must move both
    // clocks together or the real wall clock (SwiftClock's default TimeProvider.System) decides the progress instead.
    private static void Frames(ManualFrameSource frames, int count, int everyMilliseconds = 10, ManualTimeProvider? clock = null)
    {
        for (var i = 0; i < count; i++)
        {
            var span = TimeSpan.FromMilliseconds(everyMilliseconds);
            clock?.Advance(span);
            frames.Advance(span);
            Dispatcher.UIThread.RunJobs();
        }
    }

    // The Hydra "Heads at once" options (HydraSettingsPage.swift:66-67, HydraPair.maxHeadsRange = 1...8 at Hydra.swift:74).
    private static ChromeSegmentedOption<int?>[] HydraOptions() =>
        Enumerable.Range(1, 8)
            .Select(n => new ChromeSegmentedOption<int?>(value: n, title: n.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Prepend(new ChromeSegmentedOption<int?>(value: null, title: "No cap", symbol: "infinity"))
            .ToArray();

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static List<Control> Segments(Control picker) =>
        VisualQuery.Descendants<Control>(picker)
            .Where(c => Peer(c).GetAutomationControlType() == AutomationControlType.Button && !string.IsNullOrEmpty(Peer(c).GetName()))
            .OrderBy(c => c.TranslatePoint(new Point(0, 0), picker)!.Value.X)
            .ToList();

    private static Control Segment(Control picker, string title) => Assert.Single(Segments(picker), c => Peer(c).GetName() == title);

    private static bool IsSelected(Control segment) => Peer(segment).GetProvider<ISelectionItemProvider>()?.IsSelected ?? false;

    private static string Drawn(TextBlock text) =>
        text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : text.Text ?? "";

    private static IEnumerable<TextBlock> DrawnTexts(Control root) =>
        VisualQuery.Descendants<TextBlock>(root).Where(t => t.IsVisible && Drawn(t).Length > 0);

    private static TextBlock Label(Control segment) => Assert.Single(DrawnTexts(segment));

    // The text element that carries the label's font and colour: its single run when it renders runs, else itself.
    private static (double Size, FontWeight Weight, string Family, IBrush? Foreground) TextStyle(TextBlock label) =>
        label.Inlines is { Count: > 0 } inlines && inlines.OfType<Run>().FirstOrDefault() is { } run
            ? (run.FontSize, run.FontWeight, run.FontFamily.Name, run.Foreground)
            : (label.FontSize, label.FontWeight, label.FontFamily.Name, label.Foreground);

    private static Rect BoundsIn(Control control, Control ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static Color Effective(IBrush? brush, double elementOpacity = 1)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        var alpha = (byte)Math.Round(solid.Color.A * solid.Opacity * elementOpacity);
        return Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B);
    }

    // The segment's capsule fill: the Border of the segment's size with capsule corners (:63, `Capsule(style: .continuous)`).
    private static Color Fill(Control segment)
    {
        var capsules = new[] { segment }.Concat(VisualQuery.Descendants<Control>(segment)).OfType<Border>()
            .Where(b => b.Bounds.Size == segment.Bounds.Size && b.CornerRadius == new CornerRadius(segment.Bounds.Height / 2))
            .ToList();
        Assert.NotEmpty(capsules);
        return capsules.FirstOrDefault(b => b.Background is not null) is { } filled
            ? Effective(filled.Background, filled.Opacity)
            : Color.FromArgb(0, 0, 0, 0);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        if (expected.A == 0)
        {
            Assert.Equal(0, (int)actual.A); // a zero-alpha fill draws nothing, whatever its channels
            return;
        }
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1); // alpha x opacity may round either way by one step
    }

    private static Point Centre(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)!.Value;
        return new Point(origin.X + control.Bounds.Width / 2, origin.Y + control.Bounds.Height / 2);
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // ---- The option and the two initialisers ----

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:8-13
    public void An_option_holds_its_value_and_title_and_its_symbol_defaults_to_none()
    {
        var noCap = new ChromeSegmentedOption<int?>(value: null, title: "No cap", symbol: "infinity");
        Assert.Null(noCap.Value);
        Assert.Equal("No cap", noCap.Title);
        Assert.Equal("infinity", noCap.Symbol);

        var four = new ChromeSegmentedOption<int?>(value: 4, title: "4");
        Assert.Equal(4, four.Value);
        Assert.Equal("4", four.Title);
        Assert.Null(four.Symbol);
    }

    [Fact]
    [Trait("Decision", "ED-095")]
    [Trait("Rule", "R-VIEW-1")]
    public void The_picker_is_a_code_first_Decorator() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(null));
        Assert.IsAssignableFrom<Decorator>(picker);
    });

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:25-27, :74-81
    public void The_pair_initialiser_makes_options_with_no_symbol_so_every_title_is_drawn_as_text() => headless.OnUiThread(() =>
    {
        (int?, string)[] pairs = [(1, "1"), (2, "2"), (3, "3")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(2));
        var window = Show(picker);

        var segments = Segments(picker);
        Assert.Equal(["1", "2", "3"], segments.Select(s => Peer(s).GetName()));
        Assert.Equal(["1", "2", "3"], segments.Select(s => Drawn(Label(s))));
        Assert.Equal([false, true, false], segments.Select(IsSelected));
        window.Close();
    });

    // ---- The strip (UI-THEME-054) ----

    [Fact]
    [Trait("Req", "UI-THEME-054")] // UI/Chrome/ChromeSegmentedPicker.swift:29-40, Chrome.swift:33
    [Trait("Req", "PR-THEME-184")]
    public void The_strip_is_28_high_sized_to_its_content_with_3_points_inside_each_end_and_2_between_segments() => headless.OnUiThread(() =>
    {
        (int?, string)[] pairs = [(null, "No cap"), (1, "1"), (2, "2"), (3, "3")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(1));
        var window = Show(picker); // 400 by 300 offered: .fixedSize() keeps the strip at its ideal size

        var segments = Segments(picker);
        Assert.Equal(4, segments.Count);
        var rects = segments.Select(s => BoundsIn(s, picker)).ToList();

        Assert.Equal(StripHeight, picker.Bounds.Height);
        var contentWidth = rects.Sum(r => r.Width) + SegmentSpacing * (rects.Count - 1);
        Assert.Equal(StripPadding + contentWidth + StripPadding, picker.Bounds.Width, 6);

        Assert.Equal(StripPadding, rects[0].X, 6);
        for (var i = 1; i < rects.Count; i++)
            Assert.Equal(rects[i - 1].Right + SegmentSpacing, rects[i].X, 6);
        Assert.Equal(picker.Bounds.Width - StripPadding, rects[^1].Right, 6);
        foreach (var rect in rects)
            Assert.Equal((StripHeight - SegmentHeight) / 2, rect.Y, 6); // .frame(height: 28) centres the 22-point row
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-054")] // UI/Chrome/ChromeSegmentedPicker.swift:31-35, HydraSettingsPage.swift:66-67
    [Trait("Req", "PR-THEME-184")]
    public void The_segments_follow_the_callers_option_order_from_the_leading_edge() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(3));
        var window = Show(picker);

        Assert.Equal(["No cap", "1", "2", "3", "4", "5", "6", "7", "8"], Segments(picker).Select(s => Peer(s).GetName()));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-054")] // UI/Chrome/ChromeSegmentedPicker.swift:42
    public void The_strip_is_one_accessibility_container_holding_every_segment_in_order() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(null));
        var window = Show(picker);

        var names = new List<string>();
        void Walk(AutomationPeer peer)
        {
            foreach (var child in peer.GetChildren())
            {
                if (child.GetAutomationControlType() == AutomationControlType.Button)
                    names.Add(child.GetName());
                else
                    Walk(child);
            }
        }
        Walk(Peer(picker));
        Assert.Equal(["No cap", "1", "2", "3", "4", "5", "6", "7", "8"], names);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-054")] // UI/Chrome/ChromeSegmentedPicker.swift:43, Chrome.swift:1105-1135
    [Trait("Req", "PR-THEME-184")]
    public void A_no_window_drag_area_lies_behind_the_whole_strip() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(1));
        var window = Show(picker);

        var area = Assert.Single(VisualQuery.Descendants<NoWindowDragArea>(picker));
        var rect = BoundsIn(area, picker);
        Assert.Equal(new Rect(0, 0, picker.Bounds.Width, StripHeight), rect);
        // .background places it behind the strip's content: no segment is inside it.
        foreach (var segment in Segments(picker))
            Assert.DoesNotContain(segment, VisualQuery.Descendants<Control>(area));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-054")] // UI/Chrome/ChromeSegmentedPicker.swift:30-35: the gap is not a segment
    public void A_press_in_the_gap_between_two_segments_selects_nothing() => headless.OnUiThread(() =>
    {
        var writes = new List<int?>();
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => { writes.Add(v); cell.Value = v; }));
        var window = Show(picker);

        var two = Segment(picker, "2");
        var origin = two.TranslatePoint(new Point(0, 0), window)!.Value;
        Click(window, new Point(origin.X + two.Bounds.Width + SegmentSpacing / 2, origin.Y + SegmentHeight / 2));

        Assert.Empty(writes);
        Assert.Equal(1, cell.Value);
        window.Close();
    });

    // ---- A segment (UI-THEME-055) ----

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:59-62
    [Trait("Req", "PR-THEME-184")]
    public void A_segment_is_22_high_with_9_points_each_side_of_its_title_and_never_narrower_than_26() => headless.OnUiThread(() =>
    {
        (int?, string)[] pairs = [(null, "No cap"), (1, "i")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(null));
        var window = Show(picker);

        var wide = Segment(picker, "No cap");
        var wideLabel = BoundsIn(Label(wide), wide);
        Assert.Equal(SegmentHeight, wide.Bounds.Height);
        Assert.True(wideLabel.Width + 2 * SegmentPadding > SegmentMinWidth); // the padding, not the minimum, sets this one
        Assert.Equal(wideLabel.Width + 2 * SegmentPadding, wide.Bounds.Width, LayoutRounding);
        Assert.Equal(SegmentPadding, wideLabel.X, LayoutRounding);

        var narrow = Segment(picker, "i");
        var narrowLabel = BoundsIn(Label(narrow), narrow);
        Assert.Equal(SegmentHeight, narrow.Bounds.Height);
        Assert.True(narrowLabel.Width + 2 * SegmentPadding < SegmentMinWidth); // the minimum, not the padding, sets this one
        Assert.Equal(SegmentMinWidth, narrow.Bounds.Width);
        Assert.Equal((narrow.Bounds.Width - narrowLabel.Width) / 2, narrowLabel.X, LayoutRounding);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:57, :59, :79; ED-059 callout 12 pt; OD-0009 Inter
    [Trait("Req", "PR-THEME-184")]
    public void A_title_is_drawn_verbatim_on_one_line_in_the_callout_style() => headless.OnUiThread(() =>
    {
        // Text(verbatim:) draws the string as given: no Markdown, no localisation lookup.
        (int?, string)[] pairs = [(1, "**2** _x_"), (2, "No cap")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(2));
        var window = Show(picker);

        foreach (var (title, segment) in new[] { "**2** _x_", "No cap" }.Select(t => (t, Segment(picker, t))))
        {
            var label = Label(segment);
            Assert.Equal(title, Drawn(label));
            Assert.Equal(1, label.MaxLines);
            var face = TextStyle(label);
            Assert.Equal(12.0, face.Size);
            Assert.Equal("Inter", face.Family);
        }
        window.Close();
    });

    [Theory]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:57-58, :63, :71; Chrome.swift:76, :85
    [Trait("Req", "PR-THEME-184")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_chosen_segment_is_semibold_in_the_primary_label_colour_on_the_accent_at_22_percent_and_selected(bool dark) => headless.OnUiThread(() =>
    {
        (int?, string)[] pairs = [(1, "1"), (2, "2"), (3, "3")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(2));
        var window = Show(picker, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        var theme = window.ActualThemeVariant;
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, theme);

        var chosen = Segment(picker, "2");
        var face = TextStyle(Label(chosen));
        Assert.Equal(FontWeight.SemiBold, face.Weight);
        AssertColor(Chrome.PrimaryText.Resolve(theme), Effective(face.Foreground));
        AssertColor(Chrome.Accent.Opacity(0.22).Resolve(theme), Fill(chosen));
        Assert.True(IsSelected(chosen));
        window.Close();
    });

    [Theory]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:57-58, :63, :71; Chrome.swift:71-73, :79 (ED-062)
    [Trait("Req", "PR-THEME-184")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_other_segments_are_medium_in_the_label_colour_at_70_percent_with_no_fill_and_not_selected(bool dark) => headless.OnUiThread(() =>
    {
        (int?, string)[] pairs = [(1, "1"), (2, "2"), (3, "3")];
        var picker = new ChromeSegmentedPicker<int?>(options: pairs, selection: Binding<int?>.Constant(2));
        var window = Show(picker, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        var theme = window.ActualThemeVariant;

        foreach (var title in new[] { "1", "3" })
        {
            var idle = Segment(picker, title);
            var face = TextStyle(Label(idle));
            Assert.Equal(FontWeight.Medium, face.Weight);
            AssertColor(Chrome.PrimaryText.Opacity(0.7).Resolve(theme), Effective(face.Foreground));
            AssertColor(Chrome.PrimaryText.Opacity(0).Resolve(theme), Fill(idle)); // Chrome.overlay(0)
            Assert.False(IsSelected(idle));
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:32, :63; HydraSettingsPage.swift:66, :68
    [Trait("Req", "PR-THEME-184")]
    public void A_nil_selection_chooses_the_nil_option() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(null));
        var window = Show(picker);

        var segments = Segments(picker);
        Assert.Equal([true, false, false, false, false, false, false, false, false], segments.Select(IsSelected));
        AssertColor(Chrome.Accent.Opacity(0.22).Resolve(window.ActualThemeVariant), Fill(segments[0]));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:10-12, :72, :74-81
    [Trait("Req", "PR-THEME-184")]
    public void A_symbol_segment_draws_no_text_and_is_still_announced_by_its_title() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(1));
        var window = Show(picker);

        var noCap = Segment(picker, "No cap");
        Assert.Equal(AutomationControlType.Button, Peer(noCap).GetAutomationControlType());
        Assert.Empty(DrawnTexts(noCap)); // the symbol replaces the title: no "No cap", and never the SF name "infinity"
        Assert.DoesNotContain(DrawnTexts(picker), t => Drawn(t) is "No cap" or "infinity");
        Assert.Equal(SegmentHeight, noCap.Bounds.Height);
        Assert.True(noCap.Bounds.Width >= SegmentMinWidth);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:55, :71-72
    [Trait("Req", "PR-THEME-184")]
    public void Every_segment_is_a_button_named_by_its_title_verbatim_and_only_the_chosen_one_is_selected() => headless.OnUiThread(() =>
    {
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(5));
        var window = Show(picker);

        var segments = Segments(picker);
        Assert.Equal(9, segments.Count);
        Assert.All(segments, s => Assert.Equal(AutomationControlType.Button, Peer(s).GetAutomationControlType()));
        Assert.Equal(["No cap", "1", "2", "3", "4", "5", "6", "7", "8"], segments.Select(s => Peer(s).GetName()));
        Assert.Equal([false, false, false, false, false, true, false, false, false], segments.Select(IsSelected));
        window.Close();
    });

    // ---- Interaction ----

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:32-34, Chrome.swift:98
    [Trait("Req", "UI-THEME-054")]
    [Trait("Req", "UI-THEME-055")]
    public void Clicking_a_segment_sets_the_binding_to_its_value_inside_the_ease_out_animation_of_a_tenth_of_a_second() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var writes = new List<(int? Value, Marrow.UI.Port.Animation? Animation)>();
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => { writes.Add((v, Transaction.Current)); cell.Value = v; }));
        var window = Show(picker);

        Click(window, Centre(Segment(picker, "3"), window));
        Assert.Equal([(3, Marrow.UI.Port.Animation.EaseOut(duration: 0.1))], writes);
        Assert.Equal(3, cell.Value);

        Click(window, Centre(Segment(picker, "No cap"), window));
        Assert.Equal([(3, Marrow.UI.Port.Animation.EaseOut(duration: 0.1)), (null, Marrow.UI.Port.Animation.EaseOut(duration: 0.1))], writes);
        Assert.Null(cell.Value);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:32-34: the button runs its action whatever the selection
    [Trait("Req", "UI-THEME-055")]
    public void Clicking_the_chosen_segment_writes_its_value_again() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var writes = new List<int?>();
        var cell = new CellSample<int?>(4);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => { writes.Add(v); cell.Value = v; }));
        var window = Show(picker);

        Click(window, Centre(Segment(picker, "4"), window));
        Assert.Equal([4], writes);
        Assert.Equal(4, cell.Value);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:32-34, :57-58, :63, :71
    [Trait("Req", "UI-THEME-055")]
    public void After_a_click_the_new_choice_is_drawn_chosen_and_the_old_one_idle() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        // AnimatedColor.Set times the fill's fade on SwiftClock.Now (Port/PlainControls.cs:207-233), so the frame
        // source alone does not carry the animation to completion: SwiftClock.Provider must move in lockstep too
        // (see the clock parameter added to Frames() below, and AnimationClocks in Fixtures/AnimationFixtures.cs).
        var clock = new ManualTimeProvider();
        using var clockSource = SwiftClock.Provider.Override(clock);
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => cell.Value = v));
        var window = Show(picker);
        var theme = window.ActualThemeVariant;

        Click(window, Centre(Segment(picker, "6"), window));
        Frames(frames, 30, clock: clock); // 300 ms, well past the 0.1 s of Chrome.hover

        var chosen = Segment(picker, "6");
        Assert.True(IsSelected(chosen));
        Assert.Equal(FontWeight.SemiBold, TextStyle(Label(chosen)).Weight);
        AssertColor(Chrome.PrimaryText.Resolve(theme), Effective(TextStyle(Label(chosen)).Foreground));
        AssertColor(Chrome.Accent.Opacity(0.22).Resolve(theme), Fill(chosen)); // hovered too, but the chosen fill wins (:63)

        var old = Segment(picker, "1");
        Assert.False(IsSelected(old));
        Assert.Equal(FontWeight.Medium, TextStyle(Label(old)).Weight);
        AssertColor(Chrome.PrimaryText.Opacity(0.7).Resolve(theme), Effective(TextStyle(Label(old)).Foreground));
        AssertColor(Chrome.PrimaryText.Opacity(0).Resolve(theme), Fill(old));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-184")] // UI/Chrome/ChromeSegmentedPicker.swift:18, :32: the picker reads the binding, so an outside write moves the choice
    [Trait("Req", "UI-THEME-055")]
    public void A_change_to_the_bound_value_from_outside_moves_the_choice_without_animating() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => cell.Value = v));
        var window = Show(picker);
        var theme = window.ActualThemeVariant;

        cell.Value = 7; // no withAnimation around it: SwiftUI draws the new state at once
        Dispatcher.UIThread.RunJobs();

        Assert.True(IsSelected(Segment(picker, "7")));
        Assert.False(IsSelected(Segment(picker, "1")));
        AssertColor(Chrome.Accent.Opacity(0.22).Resolve(theme), Fill(Segment(picker, "7")));
        AssertColor(Chrome.PrimaryText.Opacity(0).Resolve(theme), Fill(Segment(picker, "1")));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:63, :68-70; Chrome.swift:71-73, :98
    [Trait("Req", "PR-THEME-184")]
    public void Hovering_an_idle_segment_fades_in_the_label_colour_at_8_percent_over_a_tenth_of_a_second_and_leaving_fades_it_out() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        // AnimatedColor.Set (Port/PlainControls.cs:207-233) times the fade on SwiftClock.Now, not on the frame
        // callback's own TimeSpan (Port/FrameClock.cs:7-10: "the TimeSpan a frame callback receives decides
        // nothing"). Without also overriding SwiftClock.Provider here, elapsed time keeps coming from the real wall
        // clock while only the frame wake-ups are manual, so the mid-animation assertions below raced the real clock
        // instead of the deterministic one - the documented mechanism (Fixtures/AnimationFixtures.cs's
        // AnimationClocks, and PopoversTests.cs:208-228) is to move a ManualTimeProvider bound to SwiftClock.Provider
        // in lockstep with the ManualFrameSource, which this test omitted.
        var clock = new ManualTimeProvider();
        using var clockSource = SwiftClock.Provider.Override(clock);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(1));
        var window = Show(picker);
        var theme = window.ActualThemeVariant;
        var target = Chrome.PrimaryText.Opacity(0.08).Resolve(theme);
        var two = Segment(picker, "2");

        window.MouseMove(Centre(two, window), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Fill(two).A < target.A - 1); // animated: no jump to the end state

        // Whether the 0.1 s starts at the change or at the next frame, 50 ms in is mid-way: ease-out progress 0.57 to
        // 0.68 of the change (cubic Bezier 0, 0, 0.58, 1 at 0.4 and 0.5), strictly between the two ends.
        Frames(frames, 5, clock: clock);
        Assert.InRange((int)Fill(two).A, 1, target.A - 2);
        Frames(frames, 20, clock: clock);
        AssertColor(target, Fill(two));
        Assert.False(IsSelected(two)); // hover never selects

        window.MouseMove(new Point(390, 290), RawInputModifiers.None); // off the strip
        Dispatcher.UIThread.RunJobs();
        Frames(frames, 5, clock: clock);
        Assert.InRange((int)Fill(two).A, 1, target.A - 2);
        Frames(frames, 20, clock: clock);
        AssertColor(Chrome.PrimaryText.Opacity(0).Resolve(theme), Fill(two));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:63: isSelected is tested before isHovering
    [Trait("Req", "PR-THEME-184")]
    public void Hovering_the_chosen_segment_keeps_the_accent_fill() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: Binding<int?>.Constant(1));
        var window = Show(picker);
        var accent = Chrome.Accent.Opacity(0.22).Resolve(window.ActualThemeVariant);
        var one = Segment(picker, "1");

        window.MouseMove(Centre(one, window), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        AssertColor(accent, Fill(one));
        Frames(frames, 30);
        AssertColor(accent, Fill(one));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-THEME-055")] // UI/Chrome/ChromeSegmentedPicker.swift:64: .contentShape(Capsule(style: .continuous))
    [Trait("Req", "PR-THEME-184")]
    public void The_whole_capsule_takes_the_click_but_the_corner_of_its_bounding_box_does_not() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var writes = new List<int?>();
        var cell = new CellSample<int?>(1);
        var picker = new ChromeSegmentedPicker<int?>(options: HydraOptions(), selection: new Binding<int?>(() => cell.Value, v => { writes.Add(v); cell.Value = v; }));
        var window = Show(picker);
        var eight = Segment(picker, "8");
        var origin = eight.TranslatePoint(new Point(0, 0), window)!.Value;

        // (1, 1) from the top-left corner lies outside a capsule of radius 11: sqrt(10^2 + 10^2) = 14.1 > 11.
        Click(window, new Point(origin.X + 1, origin.Y + 1));
        Assert.Empty(writes);

        // (4, 11) lies in the 9-point padding, left of the title, inside the capsule: |4 - 11| = 7 < 11 on the centre line.
        Click(window, new Point(origin.X + 4, origin.Y + SegmentHeight / 2));
        Assert.Equal([8], writes);
        window.Close();
    });
}

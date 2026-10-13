using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.FirstRun;

// GR-2 (BUILD-PLAN 2.2; OD-0096 and the OD-0098 amendments: the getting-ready pick "Look I, Words W1, Download D3"): the Getting ready
// sheet. Look I "Sheet": Welcome waits behind, blurred and dimmed; a glass sheet hangs from the top of the detail pane with W1's steps
// (a spinner while one runs, then a check), says "Ready", then lifts away while Welcome comes back into focus. It is the screen of
// GR-1's FirstRunPreparation (IFirstRunScreen): it shows what the run reports as work completes and moves nothing by a timer.
//
// SOURCE FACT: the look is the locked page E:\Droppy\work\design\decisions-2026-10-03\getting-ready\ (variant I; src\gr_lib.py:612-626
// for the sheet, :28-29 for the words and :401-432 for the marks, gr-page.js:19-23 and :99 for the hold, the hand-over and its easing),
// measured in headless Chrome into tests/fixtures/getting-ready/sheet.json (measure.cjs). Download D3 with consent: this sheet lists
// the six steps of the screen only (Voice model and Voice speed test are behind Welcome, WV-1).
//
// ENGINEERING DECISIONS (porting/engineering_decisions.md, ED-306): the numbers are the page's; where the page is silent:
//  (1) "Ready": variant I draws no word for "done", W1 words it "Ready". The title "Getting ready" crossfades to "Ready" with the
//      page's own word swap (gr_lib.py .stk: opacity 0.28 s ease, a 5 px rise 0.38 s cubic-bezier(.32,.72,0,1)) when the run is prepared.
//      A run that failed or was cancelled never says it.
//  (2) The entrance is the window's own reveal: the sheet is in the window from its first frame (RootView.ShowGettingReady), so the
//      launch's boot hand-over (ED-103) is its transition in; the sheet never appears by itself.
//  (3) The glass is the app's (SwiftGlass.Regular, as HydraPanel and CommandPalette use it), not the page's CSS frost; its top is
//      taller than the window's edge so the square top the page draws is the window's own edge.
//  (4) RR-37: the pane's saturation (0.6 returning to 1) is drawn by RootView's live Skia SaturationLayer, alongside
//      the approved blur and dimming. Native cost and full design-frame comparison still need Windows evidence.
//  (5) Reduce motion stills every transition here (the sheet is an addition beyond the Mac: the lead's rule of 2026-10-04, ED-084): each
//      goes to its end at once; the 0.15 s hold stays (the page keeps it), the spinner turns on (ED-231).
//  (6) The line under the title is the page's one line in Segoe UI Variable; in the app's own font (Inter, wider) it would leave "once." alone
//      on a second line, so it is tracked to the page's own length (GettingReadyLook.SubTracking, ED-307): one line, the page's
//      height. It still wraps rather than cut, should a font ever set it longer.
//  (7) Input: Welcome behind is not pressable until the sheet has gone; the sheet is bounded by the work (GR-1's step bound), never held.

/// <summary>The numbers of look I, as measured (see the file note).</summary>
public static class GettingReadyLook
{
    public const double SheetWidth = 460, PaddingTop = 44, PaddingSide = 26, PaddingBottom = 22, CornerRadius = 18;
    public const double TitleSize = 20, TitleLine = 26, SubSize = 12.5, SubLine = 16.25, SubTop = 4, SubBottom = 14;

    /// <summary>
    /// The line under the title is tracked so that W1's sentence, in the app's Inter, runs the length the page gives it in Segoe UI Variable
    /// Text (405.5 px of the 408 px box, one line). ENGINEERING DECISION (ED-307 (2)): untracked, Inter sets it 437.7 px long and
    /// leaves "once." alone on a second line; -0.435 px a letter brings it to the page's 405.5, its one line and its 16.25 px height.
    /// </summary>
    public const double SubTracking = -0.435;
    public const int Columns = 2;
    public const double ColumnGap = 20, RowHeight = 26, GlyphGap = 8, GlyphSize = 14, MarkSize = 16, LabelSize = 12, LabelLine = 15.6, ListBottom = 16, BarHeight = 4;

    /// <summary>gr-page.js HOLD: the pause between the last step and the lift (seconds).</summary>
    public const double Hold = 0.15;

    /// <summary>gr-page.js HAND: the lift and the pane's return (seconds).</summary>
    public const double HandOverSeconds = 1.1;

    /// <summary>gr_lib.py .vI .sheet: the sheet goes its own height plus this much up.</summary>
    public const double LiftExtra = 30;

    /// <summary>gr_lib.py .grw --hi: the sheet is gone and the pane whole by this share of the hand-over.</summary>
    public const double HiSpan = 0.56;

    public const double BlurSigma = 9, PaneOpacityRest = 0.45;

    /// <summary>The word that rises as the sheet's title when the run is prepared (W1 "done").</summary>
    public const string Done = "Ready";

    public const string Title = "Getting ready";

    public const string Line = "nth is preparing its graphics and voice for this PC. This happens once.";

    /// <summary>The 5 px a swapped word rises (gr_lib.py .stk).</summary>
    public const double WordRise = 5;

    /// <summary>gr-page.js ease: cubic in-out.</summary>
    public static double Ease(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;

    /// <summary>--hi = clamp(h / .56).</summary>
    public static double Hi(double hand) => Math.Clamp(hand / HiSpan, 0, 1);

    /// <summary>The sheet's translation: its own height and 30 px up, by --hi.</summary>
    public static double LiftOffset(double hand, double sheetHeight) => -(Hi(hand) * sheetHeight + Hi(hand) * LiftExtra);

    public static double SheetOpacity(double hand) => 1 - Hi(hand) * 0.6;

    public static double PaneBlurSigma(double hand) => (1 - Hi(hand)) * BlurSigma;

    public static double PaneOpacity(double hand) => PaneOpacityRest + (1 - PaneOpacityRest) * Hi(hand);

    /// <summary>
    /// The sheet's shadow (gr_lib.py --gshadow) as a share of the page's: whole while the sheet rises (to --hi = 1), then down to zero on
    /// the hand-over's own curve by its end, the frame the sheet is taken out of the tree. ENGINEERING DECISION (ED-307 (1)):
    /// at --hi = 1 the sheet rests 30 px above the window and only its shadow's tail is still over the pane (the page keeps it: its sheet is
    /// never taken away); removing it there would cut (OD-0076).
    /// </summary>
    public static double ShadowOpacity(double hand) => hand <= HiSpan ? 1 : Math.Max(0, 1 - (hand - HiSpan) / (1 - HiSpan));

    internal static Animation HandOver { get; } = new CubicInOut(HandOverSeconds);

    // CSS "ease" and the page's cubic-bezier(.32,.72,0,1)
    internal static Animation Fade(double seconds) => Animation.TimingCurve(0.25, 0.1, 0.25, 1, seconds);

    internal static Animation Settle(double seconds) => Animation.TimingCurve(0.32, 0.72, 0, 1, seconds);

    // nth motion pack step 15: the title's words cross-fade over 0.25 s and the word rises 5 px over 0.3 s on (.2,.8,.2,1)
    internal static Animation Arrive(double seconds) => Animation.TimingCurve(0.2, 0.8, 0.2, 1, seconds);

    /// <summary>The percentage beside the bar (step 15): mono 11 px, 34 px wide, right-aligned, with a 10 px gap.</summary>
    public const double PercentWidth = 34, PercentGap = 10, PercentSize = 11, PercentHeight = 14;

    private sealed record CubicInOut(double Seconds) : Animation
    {
        public override double Duration => Seconds;

        public override double Progress(double seconds) => Ease(seconds / Seconds);
    }
}

public enum GettingReadyPhase { Working, Ready, Lifting, Gone }

/// <summary>One step of the sheet: its glyph, W1's name and the nth status mark that says where it is (nth motion pack step 15: a hollow ring waiting, the tile-trace spinner while it runs, the solid disc with its tick
/// done; each change a 0.2 s cross-fade, the done tick drawn in), the label's ink following it (waiting text3, working text, done text2; 0.25 s).</summary>
public sealed class GettingReadyRowView
{
    private const double DimmedLabel = 0.45; // gr_lib.py --tx3 over --tx in the dark theme: a waiting step's ink

    // the sheet's own ink, per step state: step 15 "label colour waiting text3, working text, done text2". The sheet has the app's primary text (the label) and its secondary text
    // (the glyph and the line, 70 %); its tertiary is the waiting step's 45 % ink it already drew (DimmedLabel).
    internal static SwiftColor InkFor(FirstRunStepState state) => state switch
    {
        FirstRunStepState.Running => Chrome.PrimaryText,
        FirstRunStepState.Done => Chrome.SecondaryText,
        _ => Chrome.PrimaryText.Opacity(DimmedLabel),
    };

    private readonly Control owner;
    private readonly AnimatedColor labelInk;
    private readonly NthStatusMark statusMark = new() { DoneMark = NthDoneMark.Disc, MarkSize = GettingReadyLook.GlyphSize, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock text;

    internal GettingReadyRowView(Control owner, FirstRunStepInfo info)
    {
        this.owner = owner;
        Id = info.Step.Id;
        Label = info.Step.Name;
        State = FirstRunStepState.Waiting;
        text = new LiveStatusText { Text = Label, VerticalAlignment = VerticalAlignment.Center, LineHeight = GettingReadyLook.LabelLine, ClipToBounds = true, TextWrapping = TextWrapping.NoWrap };
        AutomationProperties.SetName(text, Label + ": Waiting");
        SwiftFont.System(GettingReadyLook.LabelSize, SwiftFont.Weight.Regular).Apply(text);
        labelInk = new AnimatedColor(text);
        labelInk.Set(InkFor(State), null);
        text.Foreground = labelInk.Brush;
        var glyph = new StepGlyph(Id) { VerticalAlignment = VerticalAlignment.Center };
        statusMark.Status = NthStatus.Waiting;
        Glyph = glyph;
        Mark = statusMark;
        Text = text;
        var grid = new Grid { Height = GettingReadyLook.RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GettingReadyLook.GlyphSize, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GettingReadyLook.GlyphGap, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GettingReadyLook.GlyphGap, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GettingReadyLook.MarkSize, GridUnitType.Pixel));
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(statusMark, 4);
        grid.Children.Add(glyph);
        grid.Children.Add(text);
        grid.Children.Add(statusMark);
        Box = grid;
    }

    public string Id { get; }

    public string Label { get; }

    public FirstRunStepState State { get; private set; }

    /// <summary>What the status mark shows (it cross-fades to a new one over 0.2 s).</summary>
    public NthStatus Status => statusMark.Status;

    /// <summary>The label's ink as drawn now (it moves to its step's ink over 0.25 s).</summary>
    public Avalonia.Media.Color LabelColour => labelInk.Brush.Color;

    public Control Box { get; }

    public Control Glyph { get; }

    public Control Text { get; }

    /// <summary>The status slot: the nth status mark (16 x 16).</summary>
    public Control Mark { get; }

    /// <summary>The nth status mark itself (tests and pictures).</summary>
    internal NthStatusMark StatusMark => statusMark;

    /// <summary>The share of the step's work done, as the run reported it: 1 done, the fraction running, 0 waiting.</summary>
    internal double Share { get; private set; }

    internal void Apply(FirstRunStepState state, double fraction)
    {
        Share = state == FirstRunStepState.Done ? 1 : state == FirstRunStepState.Running ? fraction : 0;
        var status = state == FirstRunStepState.Running
            ? "Running " + Math.Floor(Math.Clamp(fraction, 0, 1) * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
            : state == FirstRunStepState.Done ? "Done" : "Waiting";
        AutomationProperties.SetName(text, Label + ": " + status);
        if (state == State) return;
        State = state;
        statusMark.Status = state switch { FirstRunStepState.Running => NthStatus.Working, FirstRunStepState.Done => NthStatus.Done, _ => NthStatus.Waiting };
        var still = EnvironmentValues.GetAccessibilityReduceMotion(owner) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
        labelInk.Set(InkFor(state), still ? null : GettingReadyLook.Fade(0.25));
    }

    /// <summary>A step's glyph in the page's 16 x 16 box, drawn at 14 (gr_lib.py GLYPH; strokes 1.3, round).</summary>
    private sealed class StepGlyph : Control
    {
        private static readonly Dictionary<string, Geometry[]> Drawings = new()
        {
            ["gpu"] = [Round(4, 4, 8, 8, 1.6), Geometry.Parse("M6 2v2M10 2v2M6 12v2M10 12v2M2 6h2M2 10h2M12 6h2M12 10h2")],
            ["knob"] = [Geometry.Parse("M1.8 8h12.4"), new EllipseGeometry(new Rect(6.7, 4.7, 6.6, 6.6))],
            ["lights"] = [Round(1.8, 4.2, 12.4, 7.6, 3.8), Geometry.Parse("M4.4 4.3a3.8 3.8 0 0 0-2.5 3")],
            ["liquid"] = [Round(1.8, 5.4, 12.4, 5.2, 2.6), Geometry.Parse("M4 8h5")],
            ["strike"] = [Geometry.Parse("M9.2 1.8 4.4 9h3.4l-1 5.2L11.6 7H8.2Z")],
            ["hw"] = [Round(3.5, 3.5, 9, 9, 2), Geometry.Parse("M8 5.6v4.8M5.6 8h4.8")],
        };

        private readonly Geometry[] drawing;
        private IBrush? ink;

        public StepGlyph(string id)
        {
            drawing = Drawings.GetValueOrDefault(id) ?? []; // a step registered later has no glyph yet: its slot stays empty
            Width = GettingReadyLook.GlyphSize;
            Height = GettingReadyLook.GlyphSize;
            AttachedToVisualTree += (_, _) => { ink = Chrome.SecondaryText.Brush(this); InvalidateVisual(); };
        }

        private static Geometry Round(double x, double y, double w, double h, double r) =>
            Geometry.Parse(FormattableString.Invariant($"M{x + r} {y}H{x + w - r}A{r} {r} 0 0 1 {x + w} {y + r}V{y + h - r}A{r} {r} 0 0 1 {x + w - r} {y + h}H{x + r}A{r} {r} 0 0 1 {x} {y + h - r}V{y + r}A{r} {r} 0 0 1 {x + r} {y}Z"));

        public override void Render(DrawingContext context)
        {
            var pen = new Pen(ink ?? Brushes.Gray, 1.3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(GettingReadyLook.GlyphSize / 16, GettingReadyLook.GlyphSize / 16)))
                foreach (var geometry in drawing) context.DrawGeometry(null, pen, geometry);
        }
    }
}

/// <summary>
/// The Getting ready sheet (see the file note). Built with the plan, in the window from its first frame; it shows what GR-1's run
/// reports and nothing else; <see cref="EndAsync"/> says "Ready" (a prepared run), holds 0.15 s, lifts for 1.1 s and then completes,
/// which is the only thing the run waits for.
/// </summary>
public sealed class GettingReadySheet : Decorator, IFirstRunScreen
{
    private readonly List<GettingReadyRowView> rows = [];
    private readonly Grid list = new();
    private readonly Panel card = new() { Width = GettingReadyLook.SheetWidth };
    private readonly Grid titleBox = new() { Height = GettingReadyLook.TitleLine };
    private readonly TextBlock title, ready, line;
    private readonly Border shadow;
    private readonly Grid barRow;
    private readonly NthProgressBar bar = new() { Height = GettingReadyLook.BarHeight, VerticalAlignment = VerticalAlignment.Center };
    private readonly NthLetters percent = new(NthLettersMode.Roll) { Width = GettingReadyLook.PercentWidth, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly AnimatedScalar readyOpacity, readyRise;
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<TimeSpan>? tick;
    private IFrameSource? source;
    private bool saidReady;

    public GettingReadySheet(IReadOnlyList<FirstRunStepInfo> plan)
    {
        title = Word(GettingReadyLook.Title);
        ready = Word(GettingReadyLook.Done);
        titleBox.Children.Add(title);
        titleBox.Children.Add(ready);
        line = new TextBlock { Text = GettingReadyLook.Line, LineHeight = GettingReadyLook.SubLine, LetterSpacing = GettingReadyLook.SubTracking, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, GettingReadyLook.SubTop, 0, GettingReadyLook.SubBottom) };
        SwiftFont.System(GettingReadyLook.SubSize, SwiftFont.Weight.Regular).Apply(line);
        line.Foreground = Chrome.SecondaryText.Brush(line);
        list.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        list.ColumnDefinitions.Add(new ColumnDefinition(GettingReadyLook.ColumnGap, GridUnitType.Pixel));
        list.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        list.Margin = new Thickness(0, 0, 0, GettingReadyLook.ListBottom);
        BuildRows(plan);
        // step 15: the 4 px nth bar (step 3 option B) with the percentage in mono beside it, 10 px apart, the percentage 34 px wide and right-aligned. The row is as tall as the 14 px figure but takes only the
        // bar's 4 px of the sheet's height (a -5 px margin each side): the sheet keeps the height it was measured at, and the figure, centred on the bar, passes over the margins around it.
        percent.SetValue(TextElement.FontFamilyProperty, new FontFamily("Cascadia Mono,Consolas,monospace"));
        percent.SetValue(TextElement.FontSizeProperty, GettingReadyLook.PercentSize);
        percent.SetTextInstantly("0%");
        percent.Foreground = new SolidColorBrush(WaitingInk(percent));
        percent.ActualThemeVariantChanged += (_, _) => percent.Foreground = new SolidColorBrush(WaitingInk(percent));
        percent.Height = GettingReadyLook.PercentHeight;
        barRow = new Grid { Height = GettingReadyLook.PercentHeight, Margin = new Thickness(0, -(GettingReadyLook.PercentHeight - GettingReadyLook.BarHeight) / 2), ColumnDefinitions = new ColumnDefinitions($"*,{GettingReadyLook.PercentGap},{GettingReadyLook.PercentWidth}") };
        Grid.SetColumn(percent, 2);
        barRow.Children.Add(bar);
        barRow.Children.Add(percent);

        var content = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(GettingReadyLook.PaddingSide, GettingReadyLook.PaddingTop, GettingReadyLook.PaddingSide, GettingReadyLook.PaddingBottom) };
        content.Children.Add(titleBox);
        content.Children.Add(line);
        content.Children.Add(list);
        content.Children.Add(barRow);

        // the glass is the app's, taller than the window's top edge so the page's square top is the edge itself (ED-306 (3));
        // gr_lib.py --gshadow under it
        shadow = new Border { Margin = new Thickness(0, -GettingReadyLook.CornerRadius, 0, 0), CornerRadius = new CornerRadius(GettingReadyLook.CornerRadius), IsHitTestVisible = false };
        shadow.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 18, Blur = 40, Color = Color.FromArgb(71, 0, 0, 0) },
            [new BoxShadow { OffsetY = 2, Blur = 6, Color = Color.FromArgb(41, 0, 0, 0) }]);
        var glass = new SwiftShapeBorder { Margin = new Thickness(0, -GettingReadyLook.CornerRadius, 0, 0), Background = Brushes.Transparent }
            .RoundedRectangle(GettingReadyLook.CornerRadius, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(GettingReadyLook.CornerRadius, RoundedCornerStyle.Continuous)));
        SwiftGlass.SetCastsOwnShadow(glass, true); // OD-0147: the sheet's shadow is the page's (--gshadow above), faded by the hand-over
        card.Children.Add(shadow);
        card.Children.Add(glass);
        card.Children.Add(content);
        Child = card;

        readyOpacity = new AnimatedScalar(this, 0, value => { ReadyOpacity = value; ready.Opacity = value; title.Opacity = 1 - value; });
        readyRise = new AnimatedScalar(this, 0, value =>
        {
            ReadyRise = value;
            ready.RenderTransform = new TranslateTransform(0, GettingReadyLook.WordRise * (1 - value));
            title.RenderTransform = new TranslateTransform(0, GettingReadyLook.WordRise * value);
        });
        DetachedFromVisualTree += (_, _) => { if (Phase is GettingReadyPhase.Ready or GettingReadyPhase.Lifting) Finish(); };
    }

    private static Color WaitingInk(Control control) => GettingReadyRowView.InkFor(FirstRunStepState.Waiting).Resolve(control.ActualThemeVariant);

    private static TextBlock Word(string text)
    {
        var word = new TextBlock { Text = text, LineHeight = GettingReadyLook.TitleLine, TextWrapping = TextWrapping.NoWrap, LetterSpacing = -0.1 };
        SwiftFont.System(GettingReadyLook.TitleSize, SwiftFont.Weight.Semibold).Apply(word);
        word.Foreground = Chrome.PrimaryText.Brush(word);
        return word;
    }

    private void BuildRows(IReadOnlyList<FirstRunStepInfo> plan)
    {
        rows.Clear();
        list.Children.Clear();
        list.RowDefinitions.Clear();
        for (var index = 0; index < plan.Count; index++)
        {
            var row = new GettingReadyRowView(this, plan[index]);
            rows.Add(row);
            if (index % GettingReadyLook.Columns == 0) list.RowDefinitions.Add(new RowDefinition(GettingReadyLook.RowHeight, GridUnitType.Pixel));
            Grid.SetRow(row.Box, index / GettingReadyLook.Columns);
            Grid.SetColumn(row.Box, index % GettingReadyLook.Columns * 2);
            list.Children.Add(row.Box);
        }
    }

    public GettingReadyPhase Phase { get; private set; } = GettingReadyPhase.Working;

    /// <summary>How far the lift is, 0 hanging to 1 gone (gr-page.js --h).</summary>
    public double Hand { get; private set; }

    /// <summary>The bar's fill as drawn: the share of the work done, on the pack's spring.</summary>
    public double BarFraction => bar.Shown;

    /// <summary>The share of the work done the bar is going to.</summary>
    public double BarTarget => bar.Value;

    /// <summary>The percentage as it reads (the share reported, rounded down).</summary>
    public string PercentText => percent.Text;

    /// <summary>The word the title says: W1's title, and "Ready" once a prepared run has ended.</summary>
    public string TitleWord => saidReady ? GettingReadyLook.Done : GettingReadyLook.Title;

    public double ReadyOpacity { get; private set; }

    public double ReadyRise { get; private set; }

    public IReadOnlyList<GettingReadyRowView> Rows => rows;

    public Control Card => card;

    public Control TitleBox => titleBox;

    public Control LineText => line;

    public Control List => list;

    /// <summary>The nth bar (its box is the bar's track).</summary>
    public NthProgressBar Bar => bar;

    public Control BarTrack => bar;

    internal NthLetters Percent => percent;

    /// <summary>Raised with the lift's progress each time it changes (RootView brings the pane back with it).</summary>
    public event Action<double>? HandChanged;

    /// <summary>Raised once, when the sheet is gone.</summary>
    public event Action? Lifted;

    private bool ReduceMotion => EnvironmentValues.GetAccessibilityReduceMotion(this) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    public void Begin(IReadOnlyList<FirstRunStepInfo> steps)
    {
        if (steps.Select(step => step.Step.Id).SequenceEqual(rows.Select(row => row.Id))) return;
        BuildRows(steps); // the registry changed between making the sheet and the run (not at launch): the list is made again
    }

    public void Report(FirstRunProgress progress)
    {
        if (Phase != GettingReadyPhase.Working) return;
        var row = rows.FirstOrDefault(candidate => candidate.Id == progress.Step.Id);
        if (row is null) return; // a step the sheet was not made with has no row: its work is not on this screen
        row.Apply(progress.State, progress.Fraction);
        var share = rows.Count == 0 ? 0 : rows.Sum(candidate => candidate.Share) / rows.Count;
        bar.Value = share;
        percent.Text = Math.Floor(Math.Clamp(share, 0, 1) * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"; // the figure rolls to the share reported
    }

    public Task EndAsync(FirstRunOutcome outcome)
    {
        if (Phase != GettingReadyPhase.Working) return ended.Task;
        var prepared = outcome.Kind == FirstRunOutcomeKind.Prepared;
        if (prepared)
        {
            saidReady = true;
            Phase = GettingReadyPhase.Ready;
            readyOpacity.Set(1, GettingReadyLook.Fade(0.25)); // step 15: the title cross-fades to "Ready" over 0.25 s, the word rising 5 px over 0.3 s (.2,.8,.2,1)
            readyRise.Set(1, GettingReadyLook.Arrive(0.3));
        }
        else Phase = GettingReadyPhase.Lifting;
        Lift(prepared ? GettingReadyLook.Hold : 0);
        return ended.Task;
    }

    // the hold, then the lift: one run on the frame clock, measured on SwiftClock (ED-117); frames are the only wake-ups
    private void Lift(double hold)
    {
        if (!FrameClock.IsBound) { Finish(); return; }
        var reduce = ReduceMotion;
        var total = hold + (reduce ? 0 : GettingReadyLook.HandOverSeconds);
        var begun = SwiftClock.Now;
        source = FrameClock.Source.Current;
        tick = _ =>
        {
            if (Phase == GettingReadyPhase.Gone) return;
            var elapsed = SwiftClock.Now - begun;
            if (elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(total).Ticks) { Finish(); return; } // ED-117 (2): complete 1 ms early at most
            var into = elapsed.TotalSeconds - hold;
            if (into > 0 && !reduce)
            {
                Phase = GettingReadyPhase.Lifting;
                Draw(GettingReadyLook.HandOver.Progress(into));
            }
            source!.RequestFrame(tick!);
        };
        source.RequestFrame(tick);
    }

    private void Draw(double hand)
    {
        Hand = hand;
        RenderTransform = new TranslateTransform(0, GettingReadyLook.LiftOffset(hand, Bounds.Height > 0 ? Bounds.Height : card.DesiredSize.Height));
        Opacity = GettingReadyLook.SheetOpacity(hand);
        shadow.Opacity = GettingReadyLook.ShadowOpacity(hand);
        HandChanged?.Invoke(hand);
    }

    private void Finish()
    {
        if (tick is { } frame) source?.CancelFrame(frame);
        tick = null;
        var already = Phase == GettingReadyPhase.Gone;
        Phase = GettingReadyPhase.Gone;
        if (!already)
        {
            Draw(1);
            Lifted?.Invoke();
        }
        ended.TrySetResult();
    }
}

/// <summary>
/// The startup hook (GR-2): shows the sheet over the main window's RootView only when the machine is not prepared, and runs GR-1's
/// preparation behind it once the window is shown. Armed before the main window is made, so the sheet is in the window's first frame.
/// Inert for any content that is not a RootView (and then never reads the prepared state).
/// </summary>
internal sealed class GettingReadyLaunch
{
    private readonly FirstRunPreparation preparation;
    private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool ran;

    internal GettingReadyLaunch(FirstRunPreparation preparation) => this.preparation = preparation;

    /// <summary>Puts the hook in front of the window manager's main content factory.</summary>
    internal static GettingReadyLaunch Arm(WindowManager windows, FirstRunPreparation? preparation = null)
    {
        var launch = new GettingReadyLaunch(preparation ?? FirstRunPreparation.Default);
        windows.MainContent = launch.Wrap(windows.MainContent);
        return launch;
    }

    internal Func<Control> Wrap(Func<Control> makeMain) => () =>
    {
        var content = makeMain();
        if (Sheet is not null) return content;
        if (content is not RootView root || preparation.IsPrepared)
        {
            done.TrySetResult();
            return content;
        }
        Sheet = new GettingReadySheet(preparation.Plan());
        root.ShowGettingReady(Sheet);
        return content;
    };

    /// <summary>The sheet over the main window; null when none is needed.</summary>
    internal GettingReadySheet? Sheet { get; private set; }

    /// <summary>Completes when no sheet is needed, or when the sheet has lifted (the first-run tour waits for it).</summary>
    internal Task Done => done.Task;

    /// <summary>Runs the preparation behind the sheet (once, on the UI thread, with the window the app shows) and completes when the
    /// sheet has lifted. A run that was not started (the machine is found prepared) just lifts the sheet.</summary>
    internal async Task<FirstRunOutcome> RunAsync(TopLevel? window)
    {
        if (Sheet is not { } sheet || ran)
        {
            done.TrySetResult();
            return new FirstRunOutcome(FirstRunOutcomeKind.NotShown);
        }
        ran = true;
        FirstRunOutcome outcome;
        if (window is null) outcome = new FirstRunOutcome(FirstRunOutcomeKind.NotShown);
        else
        {
            try { outcome = await FirstRunStartup.RunAsync(window, preparation, sheet); }
            catch (Exception error) { outcome = new FirstRunOutcome(FirstRunOutcomeKind.Failed, error.Message); }
        }
        await sheet.EndAsync(outcome); // a run that never told the sheet (prepared in between) or could not end it: lifts it now; a no-op once it has ended
        done.TrySetResult();
        return outcome;
    }
}

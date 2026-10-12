// Source: DroppyCode/App/Views/Settings/TokenActivitySection.swift (B3 52bc792), :1-272.
//
// Port shape (R-VIEW-1, R-VIEW-2, R-VIEW-9, ED-066): TokenActivitySection is a Decorator whose constructor
// builds `body` once (:52-99). @State (mode, previousGrid, switchCount, grid) becomes a ViewState : MainActorModel
// (R-VIEW-2). withAnimation(...) { } completion: { } (:69-74) has no ready port primitive (no ED covers a
// completion-carrying withAnimation) - it is ported as an explicit frame-driven BlendAnimator (ENGINEERING
// DECISION, modelled on GenieAnimator's PresentedValue, Genie.cs:535-582, which is the only existing precedent in
// this tree for a value animated imperatively at the point of mutation rather than through Track/OnChange).
// HeatmapCanvas + HeatmapBlendModifier (:207-272) fold into one Control, TokenActivityHeatmapCanvas, since Avalonia
// has no separate geometry-reader/Animatable-modifier split; MeasureOverride derives the height from the available
// width (:216-224) and Render draws the blended grid in one pass (:243-271). monthStrip's GeometryReader (:117-134)
// becomes TokenActivityMonthStrip : Canvas, rebuilding its labels in ArrangeOverride so they track the available
// width the way SwiftUI's GeometryReader does.
//
// UsagePanel.tokens(_:) (DroppyCode/App/Views/Usage/UsagePanel.swift:110-118) is used at :143, :145.
// Views.Usage.UsagePanel.Tokens is the shared port of that formatter; both legend values call it directly.
//
// ChipButtonStyle (DroppyCode/UI/Common/Components.swift:58-99, cited by this file's own records) has no ported
// twin: UI.Common.Components's TokenActivitySection-independent ChipButtonStyleHelper (Components.cs:120-125) is an
// unimplemented placeholder. The chip's exact geometry and colours are fully specified by this unit's own records
// (UI-SETTINGS-022), so they are reproduced directly in ChipButton below, citing Components.swift:58-99, rather than
// depending on that placeholder or inventing a different shape.
//
// SwiftUI Color.mix(with:by:) (:260) has no port primitive yet (no ED covers it). TokenActivityHeatmapCanvas.Mix
// reproduces it as linear channel interpolation over the already-resolved (theme-space) colour, the same convention
// WorkingIndicators.swift's blended(withFraction:of:) established in this port (GradientSpin's private Blend,
// WorkingIndicators.cs:39-47), since no other blend convention exists in the tree. Swift's actual default colour
// space for Color.mix (perceptual/Oklab) is UNVERIFIED here (OD-0001); this is an ENGINEERING DECISION.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port;
using Marrow.App.Views.Usage;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Settings;

/// :4-29 TokenActivityStyle: the five-step colour ladder and the layout constants the section uses. Swift's
/// `private enum` becomes `internal` (not `private`) so this unit's tests (Marrow.App.Tests, InternalsVisibleTo
/// declared in Port/AttachmentFlightPlatform.cs) can prove PR-SETTINGS-133 and PR-SETTINGS-134 directly (R-NAME-6
/// is a default, not an absolute bar, and every other private helper in this file stays private).
internal static class TokenActivityStyle
{
    internal const double CellGap = 4; // :6
    internal const double CellRadius = 2; // :7
    internal const double LabelHeight = 18; // :9
    internal const double MinLabelAdvance = 30; // :11

    /// :17-28 color(for:maxValue:future:). PR-SETTINGS-133.
    internal static SwiftColor Color(long value, long maxValue, bool future) => LevelColor(Level(value, maxValue, future), Chrome.Accent); // :20

    /// The number of colours <see cref="Level"/> tells apart (ED-NEW-lean-21).
    internal const int Levels = 6;

    /// ED-NEW-lean-21: which of :17-28's six colours a cell takes, so a drawing resolves each colour once, not once per cell.
    internal static int Level(long value, long maxValue, bool future)
    {
        if (future) return 0; // :18
        if (!(value > 0 && maxValue > 0)) return 1; // :19
        var fraction = (double)value / maxValue; // :21
        if (fraction < 0.25) return 2; // :23
        if (fraction < 0.5) return 3; // :24
        if (fraction < 0.75) return 4; // :25
        return 5; // :26 default
    }

    /// :18-26 the colour of each <see cref="Level"/>, made with :20's accent.
    internal static SwiftColor LevelColor(int level, SwiftColor accent) => level switch
    {
        0 => Chrome.Overlay(0.035), // :18
        1 => Chrome.Overlay(0.07), // :19
        2 => accent.Opacity(0.32), // :23
        3 => accent.Opacity(0.55), // :24
        4 => accent.Opacity(0.8), // :25
        5 => accent, // :26 default
        _ => throw new UnreachableException(),
    };
}

/// The section's private computed helpers (:174-197), extracted so they are directly testable (internal, not
/// private, for the same reason as TokenActivityStyle above).
internal static class TokenActivityFormat
{
    /// :179-185 modeHelp(_:). PR-SETTINGS-131, UI-SETTINGS-022.
    internal static string ModeHelp(TokenActivityMode mode) => mode switch
    {
        TokenActivityMode.Daily => "Each day on its own, against the busiest day of the year", // :181
        TokenActivityMode.Weekly => "Each week as one shade, against the busiest week", // :182
        TokenActivityMode.Cumulative => "The running total, so the year fills as it goes", // :183
        _ => throw new UnreachableException(),
    };

    /// :142-146, :169-171. summary/detail/accessibility-value for the legend. PR-SETTINGS-138, PR-SETTINGS-139.
    internal static (string Summary, string Detail, string AccessibilityValue) Legend(long total, long uncertainTokens)
    {
        var summary = $"{UsagePanel.Tokens(total)} tokens"; // :143
        var detail = uncertainTokens > 0 // :144-146
            ? $"{UsagePanel.Tokens(uncertainTokens)} tokens have an unknown source and may overlap imported history."
            : "Total tokens recorded this year.";
        var accessibilityValue = uncertainTokens > 0 ? $"{summary}. {detail}" : summary; // :169-171
        return (summary, detail, accessibilityValue);
    }

    /// :175-177 ladder. PR-SETTINGS-138.
    internal static IReadOnlyList<SwiftColor> Ladder() =>
        new long[] { 0, 1, 3, 5, 7 }.Select(v => TokenActivityStyle.Color(v, 8, false)).ToList();

    /// :187-197 placedTicks(_:stride:). PR-SETTINGS-137.
    internal static IReadOnlyList<MonthTick> PlacedTicks(IReadOnlyList<MonthTick> ticks, double stride)
    {
        var placed = new List<MonthTick>();
        var lastX = -TokenActivityStyle.MinLabelAdvance; // :189
        foreach (var tick in ticks) // :190
        {
            var x = tick.Column * stride; // :191
            if (x - lastX < TokenActivityStyle.MinLabelAdvance) continue; // :192 guard
            placed.Add(tick); // :193
            lastX = x; // :194
        }
        return placed;
    }
}

/// :207-226 HeatmapCanvas, folded with :231-272 HeatmapBlendModifier (see file header). PR-SETTINGS-132,
/// PR-SETTINGS-133, PR-SETTINGS-134, UI-SETTINGS-023.
internal sealed class TokenActivityHeatmapCanvas : Control
{
    private TokenActivityGrid grid;
    private TokenActivityGrid? previous;
    private double blend;
    private int switchIndex;

    // ED-253 (OD-0076, SPEC 6.5): the cells as last drawn, and an appearance or theme change easing from them.
    // ED-NEW-lean-21: both are buffers kept between frames; a turn copies the cells as drawn into its own.
    private Color[,]? drawn;
    private Color[,]? turnFrom;
    private Color[,]? turnCells;
    private double turn = 1;
    private Action stopTurn = static () => { };

    // ED-NEW-lean-21: :257's six colours as made with the accent of the last frame, the six resolved for this frame, and one
    // immutable brush per colour drawn (a blend's in-between colours included; emptied once it holds BrushCap of them).
    private const int BrushCap = 512;
    private readonly SwiftColor[] levelColors = new SwiftColor[TokenActivityStyle.Levels];
    private readonly Color[] levels = new Color[TokenActivityStyle.Levels];
    private SwiftColor? levelAccent;
    private readonly Dictionary<Color, IImmutableSolidColorBrush> brushes = [];

    internal TokenActivityHeatmapCanvas(TokenActivityGrid grid)
    {
        this.grid = grid;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :108 .accessibilityHidden(true)
        HorizontalAlignment = HorizontalAlignment.Stretch; // :222 .frame(maxWidth: .infinity)
        // :257 the cells' colours (Chrome.overlay, Chrome.accent) resolve as the canvas draws, so a change of appearance or
        // theme redraws it; in a tree that turns over (the Settings window) the cells ease from what they show over 0.32 s.
        ActualThemeVariantChanged += (_, _) => Turn();
        AttachedToVisualTree += (_, _) => ThemeManager.DidChange += OnThemeChanged;
        DetachedFromVisualTree += (_, _) => { ThemeManager.DidChange -= OnThemeChanged; stopTurn(); turnFrom = null; turn = 1; };
    }

    private void OnThemeChanged()
    {
        if (CheckAccess()) Turn();
        else Dispatcher.Post(Turn);
    }

    private void Turn()
    {
        stopTurn();
        turnFrom = Snapshot(drawn);
        stopTurn = AppearanceTurn.Run(this, progress =>
        {
            turn = progress;
            if (progress >= 1) turnFrom = null;
            InvalidateVisual();
        });
    }

    // ED-NEW-lean-21: the cells as drawn now, copied into the turn's own buffer (the drawn buffer is drawn into again next frame).
    private Color[,]? Snapshot(Color[,]? cells)
    {
        if (cells is null) return null;
        if (turnCells is null || turnCells.GetLength(0) != cells.GetLength(0) || turnCells.GetLength(1) != cells.GetLength(1))
            turnCells = new Color[cells.GetLength(0), cells.GetLength(1)];
        Array.Copy(cells, turnCells, cells.Length);
        return turnCells;
    }

    private IBrush BrushFor(Color color)
    {
        if (!brushes.TryGetValue(color, out var brush))
        {
            if (brushes.Count >= BrushCap) brushes.Clear();
            brush = new ImmutableSolidColorBrush(color);
            brushes.Add(color, brush);
        }
        return brush;
    }

    internal void Update(TokenActivityGrid newGrid, TokenActivityGrid? newPrevious, double newBlend, int newSwitchIndex)
    {
        grid = newGrid;
        previous = newPrevious;
        blend = newBlend;
        switchIndex = newSwitchIndex;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// :216-224 cell = max(0, (width - gap*(columns-1))/columns); height = max(1, cell*rows + gap*(rows-1)).
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsPositiveInfinity(availableSize.Width) ? 0 : availableSize.Width; // :224 tracked width
        var columns = Math.Max(grid.Columns.Count, 1); // :216
        var gap = TokenActivityStyle.CellGap;
        var cell = Math.Max(0, (width - gap * (columns - 1)) / columns); // :219
        var rows = TokenActivityGrid.Rows;
        var height = Math.Max(1, cell * rows + gap * (rows - 1)); // :223
        return new Size(width, height);
    }

    /// :243-271 HeatmapBlendModifier.body: progress = clamp(blend - base, 0, 1) with base = switchIndex - 1, then
    /// every cell mixed that far from its previous colour to its current one.
    public override void Render(DrawingContext context)
    {
        var columns = grid.Columns.Count; // :248
        if (columns == 0) return; // :249 guard
        var width = Bounds.Width;
        var gap = TokenActivityStyle.CellGap;
        var cell = (width - gap * (columns - 1)) / columns; // :250
        if (cell <= 0) return; // :251 guard
        var pitch = cell + gap; // :252
        var progress = Math.Min(Math.Max(blend - (switchIndex - 1), 0), 1); // :244
        var theme = ActualThemeVariant;
        var rows = TokenActivityGrid.Rows;
        var previousGrid = previous;
        var from = turnFrom;
        var shown = drawn is { } kept && kept.GetLength(0) == columns ? kept : new Color[columns, rows]; // ED-NEW-lean-21
        // :257, :259 the colours resolve as the canvas draws: each of the six once for the frame (ED-NEW-lean-21).
        var accent = Chrome.Accent; // :20
        if (!ReferenceEquals(accent, levelAccent))
        {
            for (var level = 0; level < levelColors.Length; level++) levelColors[level] = TokenActivityStyle.LevelColor(level, accent);
            levelAccent = accent;
        }
        for (var level = 0; level < levels.Length; level++) levels[level] = levelColors[level].Resolve(theme);
        for (var c = 0; c < columns; c++) // :253
        {
            for (var r = 0; r < rows; r++) // :254
            {
                var date = grid.Columns[c][r];
                if (date is null) // :255 guard let date
                {
                    shown[c, r] = default;
                    continue;
                }
                var future = date.Value >= grid.Tomorrow; // :256
                var color = levels[TokenActivityStyle.Level(grid.Values[c][r], grid.MaxValue, future)]; // :257
                if (previousGrid is { } prev && progress < 1 && c < prev.Values.Count) // :258
                {
                    var old = levels[TokenActivityStyle.Level(prev.Values[c][r], prev.MaxValue, future)]; // :259
                    color = progress <= 0 ? old : Mix(old, color, progress); // :260
                }
                if (from is not null && c < from.GetLength(0) && turn < 1) color = SwiftColor.PremultipliedMix(from[c, r], color, turn); // ED-253
                shown[c, r] = color;
                var rect = new Rect(c * pitch, r * pitch, cell, cell); // :262
                context.DrawRectangle(BrushFor(color), null, rect, TokenActivityStyle.CellRadius, TokenActivityStyle.CellRadius); // :263-266
            }
        }
        drawn = shown;
    }

    /// SwiftUI <c>Color.mix(with:by:)</c>; see file header for why this is a linear-channel approximation.
    internal static Color Mix(Color from, Color to, double fraction)
    {
        byte Channel(byte a, byte b) =>
            (byte)Math.Round(Math.Clamp(a / 255.0 * (1 - fraction) + b / 255.0 * fraction, 0, 1) * 255, MidpointRounding.AwayFromZero);
        return Color.FromArgb(Channel(from.A, to.A), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }
}

/// :112-136 monthStrip(grid:). PR-SETTINGS-137, UI-SETTINGS-024.
internal sealed class TokenActivityMonthStrip : Canvas
{
    private TokenActivityGrid grid;

    internal TokenActivityMonthStrip(TokenActivityGrid grid)
    {
        this.grid = grid;
        Height = TokenActivityStyle.LabelHeight; // :116 Color.clear.frame(height: labelHeight)
    }

    internal void Update(TokenActivityGrid newGrid)
    {
        grid = newGrid;
        InvalidateArrange();
    }

    /// :117-134 GeometryReader: rebuilds the placed labels for the width ArrangeOverride is given, which Avalonia
    /// re-runs whenever the strip's size changes, the way SwiftUI's GeometryReader re-evaluates its content.
    protected override Size ArrangeOverride(Size finalSize)
    {
        Rebuild(finalSize.Width);
        return base.ArrangeOverride(finalSize);
    }

    // ED-253: the labels are kept from one layout to the next (their text and place set again), so their colour, a SwiftColor
    // brush, eases with the window's turnover instead of a new label drawing the new appearance at once.
    private readonly List<TextBlock> labels = [];

    private void Rebuild(double width)
    {
        var count = 0;
        var columnCount = Math.Max(grid.Columns.Count, 1); // :118
        var stride = (width + TokenActivityStyle.CellGap) / columnCount; // :119
        var placed = TokenActivityFormat.PlacedTicks(grid.MonthTicks, stride); // :120
        foreach (var tick in placed) // :121
        {
            TextBlock text;
            if (count < labels.Count) text = labels[count];
            else
            {
                text = new TextBlock(); // :122
                SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(text); // :123
                text.Foreground = Chrome.SecondaryText.Brush(text); // :124
                labels.Add(text);
                Children.Add(text);
            }
            count++;
            text.Text = tick.Label; // :122
            text.Measure(Size.Infinity); // :125 .fixedSize()
            var x = Math.Min(Math.Max(tick.Column * stride, 12), width - 12); // :127-129
            // :126, :131 .position(x:,y:) anchors the view's CENTER, not its top-left.
            SetLeft(text, x - text.DesiredSize.Width / 2);
            SetTop(text, TokenActivityStyle.LabelHeight / 2 - text.DesiredSize.Height / 2);
        }
        for (var i = labels.Count - 1; i >= count; i--)
        {
            Children.Remove(labels[i]);
            labels.RemoveAt(i);
        }
    }
}

/// The heatmap section at the top of Settings, General. :31-36. PR-SETTINGS-130.
public sealed class TokenActivitySection : Decorator
{
    /// :37-45 @State. R-VIEW-2.
    private sealed class ViewState : MainActorModel
    {
        private TokenActivityMode mode = TokenActivityMode.Daily; // :37
        internal TokenActivityMode Mode { get => Read(mode); set => Set(ref mode, value); }

        private TokenActivityGrid? previousGrid; // :41
        internal TokenActivityGrid? PreviousGrid { get => Read(previousGrid); set => Set(ref previousGrid, value); }

        private int switchCount; // :42
        internal int SwitchCount { get => Read(switchCount); set => Set(ref switchCount, value); }

        private TokenActivityGrid grid = TokenActivityGrid.Build(ValueMap<string, long>.Empty, TokenActivityMode.Daily); // :45
        internal TokenActivityGrid Grid { get => Read(grid); set => Set(ref grid, value); }
    }

    /// :47-50 GridKey.
    private readonly record struct GridKey(ValueMap<string, long> Daily, TokenActivityMode Mode);

    /// SwiftUI <c>withAnimation(anim) { mutate } completion: { done }</c>: no port primitive carries a completion
    /// (see file header); modelled on Genie.cs's PresentedValue (:535-582), with a completion callback added.
    private sealed class BlendAnimator
    {
        private readonly Action<double> apply;
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        internal BlendAnimator(double value, Action<double> apply)
        {
            Value = value;
            this.apply = apply;
        }

        internal double Value { get; private set; }

        internal void Set(double target, Animation? animation, Action? completion = null)
        {
            Stop();
            if (animation is null)
            {
                Value = target;
                apply(target);
                completion?.Invoke();
                return;
            }
            var from = Value;
            var start = SwiftClock.Now;
            var frames = FrameClock.Source.Current;
            source = frames;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var seconds = (SwiftClock.Now - start).TotalSeconds;
                var complete = seconds >= animation.Duration;
                Value = complete ? target : from + (target - from) * animation.Progress(seconds);
                apply(Value);
                if (complete)
                {
                    pending = null;
                    completion?.Invoke();
                }
                else
                {
                    frames.RequestFrame(frame!);
                }
            };
            pending = frame;
            frames.RequestFrame(frame);
        }

        internal void Stop()
        {
            if (pending is not null) source?.CancelFrame(pending);
            pending = null;
        }
    }

    /// A capsule chip (Components.swift:58-99 ChipButtonStyle, cited by UI-SETTINGS-022; see file header for why
    /// it is reproduced here rather than depending on UI.Common.Components's placeholder). Its own automation
    /// peer carries the isSelected trait (:84), following the SelectableButton convention Chrome.cs:1139-1153
    /// established for the same trait.
    private sealed class ChipButton : Button
    {
        private bool isSelected;

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

        internal void SetSelected(bool value)
        {
            isSelected = value;
        }

        /// <summary>:62-75 the SwiftUI Button's action: what a UI Automation invoke runs, exactly like a click.</summary>
        internal void Activate() => OnClick();

        private sealed class Peer(ChipButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
            bool ISelectionItemProvider.IsSelected => owner.isSelected;
            ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
            void ISelectionItemProvider.AddToSelection() { }
            void ISelectionItemProvider.RemoveFromSelection() { }
            void ISelectionItemProvider.Select() { }
            void IInvokeProvider.Invoke() => owner.Activate();
        }
    }

    /// <summary>:148-172 the legend as one accessibility element: label "Token activity" plus a separate value
    /// (UIA Value pattern, the convention ContextMeter established for .accessibilityValue).</summary>
    private sealed class LegendRow : Grid
    {
        internal string SpokenValue { get; set; } = "";

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

        private sealed class Peer(LegendRow owner) : ControlAutomationPeer(owner), IValueProvider
        {
            public string Value => owner.SpokenValue;
            public bool IsReadOnly => true;
            public void SetValue(string? value) => throw new InvalidOperationException("The legend value is read-only.");
        }
    }

    private static readonly Avalonia.Controls.Templates.FuncControlTemplate<Button> PlainButtonTemplate = new((btn, scope) =>
        new Avalonia.Controls.Presenters.ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = btn[!ContentControl.ContentProperty],
        }.RegisterInNameScope(scope));

    private readonly TokenLedger ledger; // :38 @State private var ledger = TokenLedger.shared
    private readonly ViewTask<int> refreshTask;
    private readonly ViewState state = new();
    private readonly TokenActivityHeatmapCanvas canvas;
    private readonly TokenActivityMonthStrip monthStrip;
    private TextBlock? legendSummary;
    private Control? legendRow;
    private BlendAnimator? blend;
    private OnChange<GridKey>? gridWatch;
    private readonly List<IDisposable> tracks = [];

    /// :36 <c>TokenActivitySection()</c>, reading the shared ledger (:38) the way every call site does.
    public TokenActivitySection() : this(TokenLedger.Shared.Current)
    {
    }

    /// For tests: an explicit ledger instance, still standing in for <c>@State private var ledger = TokenLedger.shared</c>.
    internal TokenActivitySection(TokenLedger ledger)
    {
        this.ledger = ledger;
        refreshTask = new ViewTask<int>(0, async _ => await ledger.RefreshIfNeeded()); // :96-98

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 }; // :53

        stack.Children.Add(BuildHeader()); // :54-87, :89

        canvas = new TokenActivityHeatmapCanvas(state.Grid); // :106-109
        stack.Children.Add(canvas); // :89

        monthStrip = new TokenActivityMonthStrip(state.Grid); // :114-136
        stack.Children.Add(monthStrip); // :90

        var legend = BuildLegend(); // :138-172
        stack.Children.Add(legend); // :91

        Child = stack;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        blend = new BlendAnimator(0, v => canvas.Update(state.Grid, state.PreviousGrid, v, state.SwitchCount));

        AttachedToVisualTree += (_, _) => Appear();
        DetachedFromVisualTree += (_, _) => Disappear();
    }

    /// :93-98. Runs once at appear (OnChange initial: true) and on every later change of (daily totals, mode);
    /// plus the refresh task (:96-98), started on appear and cancelled on disappear (R-VIEW-3, .task with no id).
    private void Appear()
    {
        gridWatch = new OnChange<GridKey>(
            () => new GridKey(ledger.DailyTotals, state.Mode),
            (_, key) => state.Grid = TokenActivityGrid.Build(key.Daily, key.Mode), // :94
            initial: true);

        tracks.Add(new Tracked<(TokenActivityGrid Grid, TokenActivityGrid? PreviousGrid)>(() => (state.Grid, state.PreviousGrid), pair =>
        {
            canvas.Update(pair.Grid, pair.PreviousGrid, blend!.Value, state.SwitchCount);
            monthStrip.Update(pair.Grid);
        }));

        tracks.Add(new Tracked<(long Total, long Uncertain)>(
            () => (ledger.DailyTotals.Values.Aggregate(0L, (a, b) => a + b), ledger.UncertainTokensThisYear),
            pair => UpdateLegend(pair.Total, pair.Uncertain)));

        refreshTask.Appear(); // :96-98: start during this appearance, not the next attachment.
    }

    private void Disappear()
    {
        refreshTask.Disappear();
        gridWatch?.Dispose();
        gridWatch = null;
        foreach (var track in tracks) track.Dispose();
        tracks.Clear();
        blend?.Stop();
    }

    /// :54-87. HStack(spacing: 12) of the heading, a Spacer(minLength: 12), and one chip per TokenActivityMode.
    /// The Spacer's minimum is reproduced as the chip row's leading margin, following the ChromeRow/ChromeSection
    /// convention for Spacer(minLength:) (Chrome.cs:1039, :1066): a two-column Grid ("*,Auto") rather than the
    /// flat HStack SwiftUI draws, since Avalonia's StackPanel has no flexible-spacer primitive.
    private Control BuildHeader()
    {
        var title = new TextBlock { Text = "Token activity", MaxLines = 1 }; // :55, :58
        SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(title); // :56
        title.Foreground = Chrome.PrimaryText.Brush(title); // :57
        title.VerticalAlignment = VerticalAlignment.Center; // :54 firstTextBaseline, approximated

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 0, 0, 0) }; // :54, :60 Spacer(minLength: 12)
        foreach (TokenActivityMode option in Enum.GetValues<TokenActivityMode>()) // :61 TokenActivityMode.allCases, declaration order
        {
            chips.Children.Add(BuildChip(option));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(chips, 1);
        grid.Children.Add(title);
        grid.Children.Add(chips);
        return grid;
    }

    /// :61-85 the per-mode chip button. PR-SETTINGS-131, PR-SETTINGS-132, UI-SETTINGS-022, UI-USAGE-045.
    private ChipButton BuildChip(TokenActivityMode option)
    {
        var label = new TextBlock { Text = option.Title(), MaxLines = 1 }; // :76, :78 .lineLimit via callout+no-wrap
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(label); // :74 .font(.callout)
        var labelColor = new AnimatedColor(label); // the label's foregroundStyle rides the header's animation (:87)
        label.Foreground = labelColor.Brush;

        var border = new Border // :81-82 Capsule().fill(fill), .contentShape(.capsule)
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 5), // :73-74 .padding(.horizontal, 9).padding(.vertical, 5)
            Child = label,
        };
        var fill = new AnimatedColor(border);
        border.Background = fill.Brush;

        var button = new ChipButton { Template = PlainButtonTemplate, Content = border, Padding = default, Background = Brushes.Transparent };

        var hovering = false;
        var pressed = false;

        void UpdateFill()
        {
            var isActive = state.Mode == option;
            SwiftColor color;
            if (isActive) color = Chrome.Accent.Opacity(pressed ? 0.26 : 0.16); // :88-89 .tint.opacity
            else if (pressed) color = Chrome.PrimaryText.Opacity(0.12); // :90 .primary.opacity(0.12)
            else color = Chrome.PrimaryText.Opacity(hovering ? 0.07 : 0); // :91 .primary.opacity(isHovering ? 0.07 : 0)
            fill.Set(color, Transaction.Current);
            labelColor.Set(isActive ? Chrome.Accent : Chrome.SecondaryText, Transaction.Current); // :80 .tint / .secondary
        }

        var initialized = false;
        button.Track(() => state.Mode == option, active =>
        {
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: active ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(label); // :77
            button.SetSelected(active); // :84 .isSelected trait
            if (initialized) Transaction.WithAnimation(Animation.Smooth(0.25), UpdateFill); // :87 mode changes only.
            else UpdateFill();
            initialized = true;
        });

        button.OnHover(over =>
        {
            hovering = over;
            Transaction.WithAnimation(Animation.EaseOut(0.12), UpdateFill); // :81 .animation(.easeOut(duration: 0.12), value: isHovering)
        });
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => { pressed = true; UpdateFill(); }, RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { pressed = false; UpdateFill(); }, RoutingStrategies.Tunnel);

        button.Click += (_, _) => Switch(option); // :62-75

        ToolTip.SetTip(button, TokenActivityFormat.ModeHelp(option)); // :82
        AutomationProperties.SetName(button, option.Title()); // :83

        return button;
    }

    /// :62-75. Guarded switch that starts the 0.45s blend.
    private void Switch(TokenActivityMode option)
    {
        if (option == state.Mode) return; // :63 guard option != mode else return
        state.PreviousGrid = state.Grid; // :67
        var switchIndex = state.SwitchCount + 1; // :68
        var animation = Animation.Smooth(0.45); // :69
        Transaction.WithAnimation(animation, () =>
        {
            state.Mode = option; // :70
            state.SwitchCount = switchIndex; // :71
        });
        blend!.Set(switchIndex, animation, () =>
        {
            if (state.SwitchCount == switchIndex) state.PreviousGrid = null; // :73 completion
        });
    }

    /// :138-172 legend(total:uncertainTokens:). PR-SETTINGS-138, PR-SETTINGS-139, UI-SETTINGS-025.
    private Control BuildLegend()
    {
        var summary = new TextBlock { MaxLines = 1 }; // :149, :152
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(summary); // :150
        summary.Foreground = Chrome.SecondaryText.Brush(summary); // :151

        var row = new LegendRow { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; // :148-154 flexible spacer
        row.Children.Add(summary);

        var spacer = new Control { MinWidth = 28 }; // :154 minimum 12 + adjacent HStack spacings 8 + 8.
        Grid.SetColumn(spacer, 1);
        row.Children.Add(spacer);
        var ladder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Grid.SetColumn(ladder, 2);
        row.Children.Add(ladder);

        var less = new TextBlock { Text = "Less" }; // :155
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(less); // :156
        less.Foreground = Chrome.SecondaryText.Brush(less); // :157
        ladder.Children.Add(less);

        foreach (var step in TokenActivityFormat.Ladder()) // :158-162
        {
            var swatch = new Border // :159-161
            {
                CornerRadius = new CornerRadius(TokenActivityStyle.CellRadius),
                Width = 10,
                Height = 10,
                Background = step.Brush(row),
            };
            ladder.Children.Add(swatch);
        }

        var more = new TextBlock { Text = "More" }; // :163
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(more); // :164
        more.Foreground = Chrome.SecondaryText.Brush(more); // :165
        ladder.Children.Add(more);

        // :167 .accessibilityElement(children: .ignore): the ladder swatches carry no automation of their own.
        // :168-171 .accessibilityLabel("Token activity") + .accessibilityValue(...): Avalonia's AutomationProperties
        // has no separate reader for label and value (no IValueProvider precedent elsewhere in this tree); both are
        // folded into one automation Name, updated by UpdateLegend below (ENGINEERING DECISION, UNVERIFIED OD-0001).
        AutomationProperties.SetAccessibilityView(row, AccessibilityView.Content);

        legendSummary = summary;
        legendRow = row;
        UpdateLegend(0, 0);
        return row;
    }

    private void UpdateLegend(long total, long uncertainTokens)
    {
        if (legendSummary is null || legendRow is null) return;
        var (summary, detail, value) = TokenActivityFormat.Legend(total, uncertainTokens); // :142-146, :169-171
        legendSummary.Text = summary;
        ToolTip.SetTip(legendSummary, detail); // :153 .help(detail)
        AutomationProperties.SetName(legendRow, "Token activity"); // :168 accessibilityLabel
        ((LegendRow)legendRow).SpokenValue = value; // :169-171 accessibilityValue
    }
}

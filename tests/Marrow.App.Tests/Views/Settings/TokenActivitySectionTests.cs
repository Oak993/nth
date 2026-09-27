using System;
using System.Linq;
using System.Reflection;
using Marrow.Testing;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Settings;

// Tests for src/Marrow.App/Views/Settings/TokenActivitySection.cs, the port of
// DroppyCode/App/Views/Settings/TokenActivitySection.swift (B3 52bc792), :1-272.
//
// Records PR-SETTINGS-130..135, 137..139, UI-SETTINGS-021..025, UI-USAGE-045.
//
// Every expectation below is transcribed from the pinned Swift lines cited on each test and from the packet's own
// records, never from this port's own output. Pure helpers (TokenActivityStyle, TokenActivityFormat) are proven
// directly with plain Facts; the composite control needs the headless session (R-TEST-7) because it is an Avalonia
// Control built from real fonts/colours/automation peers.
//
// TokenActivitySection reads TokenLedger.Shared.Current (:38), a process-wide singleton with a private constructor
// (R-LIFE-2: Ambient<TokenLedger>, but with no public factory a test cannot build its own instance). The mode
// switching, header, chips, canvas-accessibility and month-strip/legend STRUCTURE tests below never depend on the
// ledger's content (TokenActivityGrid's columns and month ticks come only from the current year, not from daily
// totals: TokenLedger.swift/TokenActivityGrid.Build), so they are safe under the shared singleton. The one test
// that proves the legend's total is actually the ledger's total (TokenActivitySection_LegendReflectsLedgerTotal)
// reads the ledger's own total before and after a known Record() rather than assuming a clean slate, since the
// singleton is not resettable between tests in the same process.
[Collection("Headless")]
public sealed class TokenActivitySectionTests(HeadlessSession headless)
{
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object StateValue(object state, string name) => state.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
    private static Button Chip(TokenActivitySection section, string name) => section.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
    private static void Click(Button button) { button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    [Trait("Req", "PR-SETTINGS-131")] // App/Views/Settings/TokenActivitySection.swift:69,87; Components.swift:84-91
    public void HeaderFillUsesOwnQuarterSecondCurve()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var time = new ManualTimeProvider();
            using var clock = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var section = new TokenActivitySection();
            var window = Show(section);
            try
            {
                var weekly = Chip(section, "Weekly");
                var border = Assert.IsType<Border>(weekly.Content);
                Click(weekly);
                time.Advance(TimeSpan.FromTicks(1_250_000)); frames.Advance(TimeSpan.FromTicks(1_250_000));
                var expected = TokenActivityHeatmapCanvas.Mix(Chrome.PrimaryText.Opacity(0).Resolve(ThemeVariant.Light),
                    Chrome.Accent.Opacity(.16).Resolve(ThemeVariant.Light), Marrow.UI.Port.Animation.Smooth(.25).Progress(.125));
                Assert.Equal(expected, Assert.IsType<SolidColorBrush>(border.Background).Color);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-025")] // App/Views/Settings/TokenActivitySection.swift:148-166
    public void LegendLadderTracksRightEdgeWhenSectionResizes()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(); var window = Show(section);
            try
            {
                foreach (var width in new[] {600d, 900d})
                {
                    window.Width = width; window.UpdateLayout();
                    var more = section.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "More");
                    Assert.Equal(section.Bounds.Width, more.TranslatePoint(new Point(more.Bounds.Width, 0), section)!.Value.X, 1);
                }
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-135")] // App/Views/Settings/TokenActivitySection.swift:43-50,93-98
    public void AppearanceRefreshesMemoizesAndDisappearanceCancelsAndUnsubscribes()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var ledger = (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!;
            var section = new TokenActivitySection(ledger); var window = Show(section);
            try
            {
                Assert.True((bool)Field(ledger, "didStartLoad"));
                var state = Field(section, "state"); var grid = StateValue(state, "Grid");
                typeof(TokenLedger).GetProperty("LegacyDaily")!.SetValue(ledger, ValueMap<string, long>.Empty.Mutating(d => d["2026-01-01"] = 9));
                Dispatcher.UIThread.RunJobs(); Assert.Same(grid, StateValue(state, "Grid"));
                var task = (ViewTask<int>)Field(section, "refreshTask"); var running = Assert.IsType<TaskHandle>(task.Running);
                window.Content = null; Dispatcher.UIThread.RunJobs();
                Assert.True(running.IsCancelled); Assert.Null(task.Running);
                Assert.Null(Field(section, "gridWatch"));
                Assert.Empty((System.Collections.IEnumerable)Field(section, "tracks"));
                ledger.Record(77); Dispatcher.UIThread.RunJobs(); Assert.Same(grid, StateValue(state, "Grid"));
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-132")] // App/Views/Settings/TokenActivitySection.swift:62-74
    public void RapidSwitchRetainsPreviousGridUntilNewestBlendSettles()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope(); var time = new ManualTimeProvider();
            using var clock = SwiftClock.Provider.Override(time); var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var section = new TokenActivitySection(); var window = Show(section);
            try
            {
                var state = Field(section, "state"); Click(Chip(section, "Weekly"));
                var weeklyGrid = StateValue(state, "Grid");
                time.Advance(TimeSpan.FromTicks(1_000_000)); frames.Advance(TimeSpan.FromTicks(1_000_000));
                Click(Chip(section, "Cumulative"));
                Assert.Equal(2, StateValue(state, "SwitchCount")); Assert.Same(weeklyGrid, StateValue(state, "PreviousGrid"));
                var firstDeadline = TimeSpan.FromTicks((long)Math.Ceiling(Marrow.UI.Port.Animation.Smooth(.45).Duration * TimeSpan.TicksPerSecond) - 1_000_000);
                time.Advance(firstDeadline); frames.Advance(firstDeadline); Dispatcher.UIThread.RunJobs();
                Assert.Same(weeklyGrid, StateValue(state, "PreviousGrid"));
                time.Advance(TimeSpan.FromTicks(1_000_001)); frames.Advance(TimeSpan.FromTicks(1_000_001)); Dispatcher.UIThread.RunJobs();
                Assert.Null(StateValue(state, "PreviousGrid"));
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }
    private readonly HeadlessSession headless = headless;

    /// Binds UserDefaults.Standard and Storage.Locations to throwaway, in-memory / scratch-folder stand-ins for
    /// the calling async flow (R-TEST-9): TokenLedger's private constructor reads both (LiveSpendStateStore.Load).
    /// Combined scope type lives in Fixtures/TokenActivityTestScope.cs (R-TEST-2/3: a test file holds no other
    /// class/record declaration).
    private static TokenActivityTestScope BindTestScope() => TokenActivityTestScope.Create();

    // ---- TokenActivityStyle.Color: PR-SETTINGS-133 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-133")] // TokenActivitySection.swift:18
    public void Color_Future_IsOverlay0035()
    {
        var expected = Marrow.UI.Chrome.Chrome.Overlay(0.035);
        var actual = TokenActivityStyle.Color(0, 100, future: true);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual.Resolve(ThemeVariant.Light));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-133")] // TokenActivitySection.swift:19
    public void Color_ZeroValue_IsOverlay007()
    {
        var expected = Marrow.UI.Chrome.Chrome.Overlay(0.07);
        var actual = TokenActivityStyle.Color(0, 100, future: false);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual.Resolve(ThemeVariant.Light));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-133")] // TokenActivitySection.swift:19, "maxValue of zero" failure case
    public void Color_ZeroMaxValue_IsOverlay007EvenWithPositiveValue()
    {
        var expected = Marrow.UI.Chrome.Chrome.Overlay(0.07);
        var actual = TokenActivityStyle.Color(5, 0, future: false);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual.Resolve(ThemeVariant.Light));
    }

    [Theory]
    [InlineData(1, 10, 0.32)]  // fraction 0.1 < 0.25 :23
    [InlineData(4, 10, 0.55)]  // fraction 0.4 < 0.5 :24
    [InlineData(7, 10, 0.8)]   // fraction 0.7 < 0.75 :25
    [InlineData(10, 10, 1.0)]  // fraction 1.0, default :26 (full accent, opacity implicit 1)
    [Trait("Req", "PR-SETTINGS-133")] // TokenActivitySection.swift:22-27
    public void Color_FourAccentSteps(long value, long max, double expectedOpacity)
    {
        headless.OnUiThread(() =>
        {
            var expected = Marrow.UI.Chrome.Chrome.Accent.Opacity(expectedOpacity);
            var actual = TokenActivityStyle.Color(value, max, future: false);
            Assert.Equal(expected.Resolve(ThemeVariant.Light), actual.Resolve(ThemeVariant.Light));
            Assert.Equal(expected.Resolve(ThemeVariant.Dark), actual.Resolve(ThemeVariant.Dark));
        });
    }

    // ---- TokenActivityFormat.ModeHelp: PR-SETTINGS-131 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-131")] // TokenActivitySection.swift:179-185
    public void ModeHelp_ExactTooltipsInOrder()
    {
        Assert.Equal("Each day on its own, against the busiest day of the year", TokenActivityFormat.ModeHelp(TokenActivityMode.Daily));
        Assert.Equal("Each week as one shade, against the busiest week", TokenActivityFormat.ModeHelp(TokenActivityMode.Weekly));
        Assert.Equal("The running total, so the year fills as it goes", TokenActivityFormat.ModeHelp(TokenActivityMode.Cumulative));
    }

    // ---- TokenActivityFormat.Legend: PR-SETTINGS-138, PR-SETTINGS-139 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-138")] // TokenActivitySection.swift:143, 146
    [Trait("Req", "PR-SETTINGS-139")] // TokenActivitySection.swift:144-146, 169-171
    public void Legend_NoUncertainTokens_TooltipAndValue()
    {
        var (summary, detail, value) = TokenActivityFormat.Legend(total: 1234, uncertainTokens: 0);
        Assert.Equal("1.2k tokens", summary); // UsagePanel.tokens(1234) -> "1.2k"
        Assert.Equal("Total tokens recorded this year.", detail);
        Assert.Equal("1.2k tokens", value);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-138")] // TokenActivitySection.swift:143-146
    [Trait("Req", "PR-SETTINGS-139")] // TokenActivitySection.swift:169-171
    public void Legend_UncertainTokens_TooltipAndValueNameTheOverlap()
    {
        var (summary, detail, value) = TokenActivityFormat.Legend(total: 5000, uncertainTokens: 250);
        Assert.Equal("5k tokens", summary);
        Assert.Equal("250 tokens have an unknown source and may overlap imported history.", detail);
        Assert.Equal("5k tokens. 250 tokens have an unknown source and may overlap imported history.", value);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1500, "1.5k")]
    [InlineData(1_000_000, "1M")]
    [InlineData(2_500_000, "2.5M")]
    [InlineData(1_000_000_000, "1B")]
    [Trait("Req", "PR-SETTINGS-138")] // UsagePanel.tokens(_:), UsagePanel.swift:109-118, cited at TokenActivitySection.swift:143
    public void Legend_TokenCompactFormatting(long count, string expectedSummaryCount)
    {
        var (summary, _, _) = TokenActivityFormat.Legend(total: count, uncertainTokens: 0);
        Assert.Equal($"{expectedSummaryCount} tokens", summary);
    }

    // ---- TokenActivityFormat.Ladder: PR-SETTINGS-138 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-138")] // TokenActivitySection.swift:174-177
    public void Ladder_FiveStepsMatchColorFunctionAtSampleValues()
    {
        headless.OnUiThread(() =>
        {
            var ladder = TokenActivityFormat.Ladder();
            Assert.Equal(5, ladder.Count);
            var samples = new long[] { 0, 1, 3, 5, 7 };
            for (var i = 0; i < 5; i++)
            {
                var expected = TokenActivityStyle.Color(samples[i], 8, future: false);
                Assert.Equal(expected.Resolve(ThemeVariant.Light), ladder[i].Resolve(ThemeVariant.Light));
            }
        });
    }

    // ---- TokenActivityFormat.PlacedTicks: PR-SETTINGS-137 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-137")] // TokenActivitySection.swift:187-197
    public void PlacedTicks_DropsLabelsCloserThan30Points()
    {
        var ticks = new[]
        {
            new MonthTick("Jan", 0),  // x=0, 0-(-30)=30 >= 30: kept; lastX=0
            new MonthTick("Feb", 4),  // x=20, 20-0=20 < 30: dropped
            new MonthTick("Mar", 5),  // x=25, 25-0=25 < 30: dropped
            new MonthTick("Apr", 13), // x=65, 65-0=65 >= 30: kept
        };
        var placed = TokenActivityFormat.PlacedTicks(ticks, stride: 5);
        Assert.Equal(new[] { "Jan", "Apr" }, placed.Select(t => t.Label));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-137")] // TokenActivitySection.swift:189, 191: the first tick always clears -minLabelAdvance
    public void PlacedTicks_FirstTickIsAlwaysKept()
    {
        var ticks = new[] { new MonthTick("Jan", 0) };
        var placed = TokenActivityFormat.PlacedTicks(ticks, stride: 1);
        Assert.Single(placed);
        Assert.Equal("Jan", placed[0].Label);
    }

    // ---- TokenActivityHeatmapCanvas geometry: PR-SETTINGS-134 ----

    [Fact]
    [Trait("Req", "PR-SETTINGS-134")] // TokenActivitySection.swift:216-224
    public void HeatmapCanvas_MeasuresCellAndHeightFromAvailableWidth()
    {
        var daily = ValueMap<string, long>.Empty;
        var grid = TokenActivityGrid.Build(daily, TokenActivityMode.Daily, new System.DateTimeOffset(2026, 6, 15, 0, 0, 0, System.TimeSpan.Zero));
        var canvas = new TokenActivityHeatmapCanvas(grid) { UseLayoutRounding = false };
        var columns = grid.Columns.Count;
        const double width = 700;
        canvas.Measure(new Size(width, double.PositiveInfinity));
        var expectedCell = System.Math.Max(0, (width - TokenActivityStyle.CellGap * (columns - 1)) / columns);
        var expectedHeight = System.Math.Max(1, expectedCell * TokenActivityGrid.Rows + TokenActivityStyle.CellGap * (TokenActivityGrid.Rows - 1));
        Assert.Equal(width, canvas.DesiredSize.Width, 3);
        Assert.Equal(expectedHeight, canvas.DesiredSize.Height, 3);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-134")] // TokenActivitySection.swift:219, 223, "Zero or negative available width" failure case:
    // the cell size floors at 0, but the row gaps (4pt * 6) still add up, so the height floors at that, not at 1.
    public void HeatmapCanvas_ZeroWidth_CellFloorsAtZero_HeightIsJustTheRowGaps()
    {
        var daily = ValueMap<string, long>.Empty;
        var grid = TokenActivityGrid.Build(daily, TokenActivityMode.Daily, new System.DateTimeOffset(2026, 6, 15, 0, 0, 0, System.TimeSpan.Zero));
        var canvas = new TokenActivityHeatmapCanvas(grid) { UseLayoutRounding = false };
        canvas.Measure(new Size(0, double.PositiveInfinity));
        var expectedHeight = TokenActivityStyle.CellGap * (TokenActivityGrid.Rows - 1); // cell=0, height=max(1, 0 + gap*(rows-1))
        Assert.Equal(expectedHeight, canvas.DesiredSize.Height, 3);
    }

    // ---- TokenActivityHeatmapCanvas.Mix: PR-SETTINGS-132 (Color.mix approximation) ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [Trait("Req", "PR-SETTINGS-132")] // TokenActivitySection.swift:260 Color.mix(with:by:); see file header for the approximation
    public void Mix_InterpolatesLinearlyBetweenTwoResolvedColors(double fraction)
    {
        var from = Color.FromArgb(255, 0, 0, 0);
        var to = Color.FromArgb(255, 255, 255, 255);
        var mixed = TokenActivityHeatmapCanvas.Mix(from, to, fraction);
        var expected = (byte)System.Math.Round(255 * fraction);
        Assert.Equal(expected, mixed.R);
        Assert.Equal(expected, mixed.G);
        Assert.Equal(expected, mixed.B);
    }

    // ---- Composite control: UI-SETTINGS-021..025, UI-USAGE-045, PR-SETTINGS-130 ----

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = content };
        window.RequestedThemeVariant = ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-130")] // TokenActivitySection.swift:52-53
    [Trait("Req", "UI-SETTINGS-021")] // TokenActivitySection.swift:55-59
    public void Section_HeaderTitle_ExactTextFontAndColor()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var stack = Assert.IsType<StackPanel>(section.Child);
            Assert.Equal(Orientation.Vertical, stack.Orientation); // :53 VStack
            Assert.Equal(10, stack.Spacing); // :53 spacing: 10
            Assert.Equal(4, stack.Children.Count); // header, heatmap, monthStrip, legend :89-91

            var title = section.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Token activity");
            Assert.Equal(1, title.MaxLines); // :58 lineLimit(1)
            var expectedColor = Marrow.UI.Chrome.Chrome.PrimaryText.Resolve(ThemeVariant.Light);
            var brush = Assert.IsType<SolidColorBrush>(title.Foreground);
            Assert.Equal(expectedColor, brush.Color);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-022")] // TokenActivitySection.swift:61-87
    [Trait("Req", "UI-USAGE-045")] // Daily/Weekly/Cumulative chips
    public void Section_ThreeChips_ExactTextsTooltipsAndOrder()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var buttons = section.GetVisualDescendants().OfType<Button>()
                .Where(b => AutomationProperties.GetName(b) is "Daily" or "Weekly" or "Cumulative")
                .ToList();
            Assert.Equal(3, buttons.Count);
            Assert.Equal("Daily", AutomationProperties.GetName(buttons[0]));
            Assert.Equal("Weekly", AutomationProperties.GetName(buttons[1]));
            Assert.Equal("Cumulative", AutomationProperties.GetName(buttons[2]));

            Assert.Equal("Each day on its own, against the busiest day of the year", ToolTip.GetTip(buttons[0]));
            Assert.Equal("Each week as one shade, against the busiest week", ToolTip.GetTip(buttons[1]));
            Assert.Equal("The running total, so the year fills as it goes", ToolTip.GetTip(buttons[2]));

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-131")] // TokenActivitySection.swift:61-87, isSelected trait and switching
    [Trait("Req", "UI-USAGE-045")]
    public void Section_ChipSelection_TracksModeAndIgnoresReclickOfCurrent()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            // ED-098 (1): the blend animator's own frame requests are driven manually here so the assertions below
            // depend only on the (synchronous) mode switch, not on a real animation frame completing.
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var buttons = section.GetVisualDescendants().OfType<Button>()
                .Where(b => AutomationProperties.GetName(b) is "Daily" or "Weekly" or "Cumulative")
                .ToList();
            var daily = buttons[0];
            var weekly = buttons[1];

            bool IsSelected(Button b)
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(b);
                var provider = (ISelectionItemProvider)peer;
                return provider.IsSelected;
            }

            // :37 mode starts .daily
            Assert.True(IsSelected(daily));
            Assert.False(IsSelected(weekly));

            weekly.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(IsSelected(daily));
            Assert.True(IsSelected(weekly));

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-023")] // TokenActivitySection.swift:108 .accessibilityHidden(true)
    public void Section_HeatmapCanvas_HiddenFromAccessibility()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var canvas = section.GetVisualDescendants().OfType<TokenActivityHeatmapCanvas>().Single();
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(canvas));

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-024")] // TokenActivitySection.swift:116, labelHeight = 18
    public void Section_MonthStrip_Is18PointsTall()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var strip = section.GetVisualDescendants().OfType<TokenActivityMonthStrip>().Single();
            Assert.Equal(18, strip.Height);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-025")] // TokenActivitySection.swift:148-166
    public void Section_Legend_LessMoreAndFiveSwatches()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);

            var texts = section.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Less", texts);
            Assert.Contains("More", texts);

            var swatches = section.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Width == 10 && b.Height == 10)
                .ToList();
            Assert.Equal(5, swatches.Count);
            foreach (var swatch in swatches)
                Assert.Equal(TokenActivityStyle.CellRadius, swatch.CornerRadius.TopLeft);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-138")] // TokenActivitySection.swift:91, 143: total = ledger.dailyTotals.values.reduce(0,+)
    public void Section_LegendReflectsLedgerTotal()
    {
        headless.OnUiThread(() =>
        {
            using var scope = BindTestScope();
            var ledger = TokenLedger.Shared.Current;
            var before = ledger.DailyTotals.Values.Aggregate(0L, (a, b) => a + b);

            var section = new TokenActivitySection(ledger);
            var window = Show(section);
            Dispatcher.UIThread.RunJobs();

            ledger.Record(4321);
            Dispatcher.UIThread.RunJobs();

            var expected = TokenActivityFormat.Legend(before + 4321, ledger.UncertainTokensThisYear).Summary;
            var summary = section.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text != null && t.Text.EndsWith(" tokens", System.StringComparison.Ordinal));
            Assert.Equal(expected, summary.Text);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }
}

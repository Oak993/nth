using Avalonia;
using Avalonia.Media;
using Marrow.UI.Common;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/WorkingIndicators.swift (B2 1fbf81e), :1-345.
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Common/WorkingIndicators.cs, namespace Marrow.UI.Common:
//   GradientSpin: Color[] RowTints(Color accent), Dim, Period, Opacity(phase)
//   SpinnerCell(Row, Column, Lag)
//   WorkingSpinner.Cells, MiniSpinner.Cells: the fixed 3x3 / 2x3 cell grids and their lags
//   WorkingWords.Words, RotateSeconds, Seed(identifier), Word(seed, elapsedSeconds)
//   internal SpinnerGeometry.ContentSize/RowCount/CellFrame (F.-shared layout math, InternalsVisibleTo already
//   granted to Marrow.UI.Tests by src/Marrow.UI/Theme/AppTheme.cs)
public sealed class WorkingIndicatorsTests
{
    // ---- GradientSpin.RowTints (PR-THEME-162) ----

    [Fact]
    [Trait("Req", "PR-THEME-162")] // WorkingIndicators.swift:22-29
    public void RowTints_blends_the_accent_038_toward_white_on_top_and_022_toward_black_on_bottom()
    {
        var accent = Color.FromArgb(255, 100, 150, 200);
        var tints = GradientSpin.RowTints(accent);

        Assert.Equal(3, tints.Length);
        // base.blended(withFraction: 0.38, of: .white) = base*(1-0.38) + white*0.38, per channel including alpha.
        // R: 100*0.62 + 255*0.38 = 158.9 -> 159; G: 150*0.62 + 255*0.38 = 189.9 -> 190; B: 200*0.62 + 255*0.38 = 220.9 -> 221.
        Assert.Equal(Color.FromArgb(255, 159, 190, 221), tints[0]);
        Assert.Equal(accent, tints[1]);
        // base.blended(withFraction: 0.22, of: .black) = base*(1-0.22) + black*0.22
        // R: 100*0.78 = 78; G: 150*0.78 = 117; B: 200*0.78 = 156; A: 255*0.78 + 255*0.22 = 255 (both opaque).
        Assert.Equal(Color.FromArgb(255, 78, 117, 156), tints[2]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-162")] // WorkingIndicators.swift:22-29
    public void RowTints_of_pure_white_and_pure_black_stay_at_their_extremes()
    {
        // White blended toward white stays white; blended 0.22 toward black: 255*0.78 = 198.9 -> 199 (alpha
        // blends the same way between two opaque colours, so it stays 255).
        Assert.Equal([Colors.White, Colors.White, Color.FromArgb(255, 199, 199, 199)], GradientSpin.RowTints(Colors.White));
        // Black blended 0.38 toward white: 255*0.38 = 96.9 -> 97; blended toward black stays black.
        Assert.Equal([Color.FromArgb(255, 97, 97, 97), Colors.Black, Colors.Black], GradientSpin.RowTints(Colors.Black));
    }

    // ---- GradientSpin.Opacity (PR-THEME-163) ----

    [Theory]
    [Trait("Req", "PR-THEME-163")] // WorkingIndicators.swift:34-44
    [InlineData(0, 1)]
    [InlineData(0.45, 0.1)]
    [InlineData(0.7, 0.1)]
    [InlineData(0.92, 0.1)]
    [InlineData(1, 1)]
    public void Opacity_hits_its_named_landmarks(double phase, double expected) =>
        Assert.Equal(expected, GradientSpin.Opacity(phase), 12);

    [Fact]
    [Trait("Req", "PR-THEME-163")] // WorkingIndicators.swift:35-36: negative phase wraps into [0, 1)
    public void Opacity_wraps_a_negative_phase_the_same_as_its_positive_equivalent()
    {
        Assert.Equal(GradientSpin.Opacity(0.9), GradientSpin.Opacity(-0.1), 12);
        Assert.Equal(GradientSpin.Opacity(0.6), GradientSpin.Opacity(-1.4), 12);
    }

    [Fact]
    [Trait("Req", "PR-THEME-163")] // WorkingIndicators.swift:37-39: smoothstep fall, zero slope at both ends
    public void Opacity_falls_by_smoothstep_between_0_and_045()
    {
        var t = 0.2 / 0.45;
        var expected = 1 - 0.9 * (t * t * (3 - 2 * t));
        Assert.Equal(expected, GradientSpin.Opacity(0.2), 12);
        Assert.True(GradientSpin.Opacity(0.01) > GradientSpin.Opacity(0.02)); // strictly falling
    }

    [Fact]
    [Trait("Req", "PR-THEME-163")] // WorkingIndicators.swift:41-43: linear rise from 0.92 to 1
    public void Opacity_rises_linearly_between_092_and_1()
    {
        Assert.Equal(0.1 + 0.9 * 0.5, GradientSpin.Opacity(0.96), 12);
        Assert.Equal(0.1, GradientSpin.Opacity(0.919999), 6);
    }

    [Fact]
    [Trait("Req", "PR-THEME-163")] // WorkingIndicators.swift:30-31
    public void Dim_and_period_are_the_pinned_constants()
    {
        Assert.Equal(0.1, GradientSpin.Dim);
        Assert.Equal(0.75, GradientSpin.Period);
    }

    // ---- WorkingSpinner.Cells (PR-THEME-164, UI-THEME-049) ----

    [Fact]
    [Trait("Req", "PR-THEME-164")] // WorkingIndicators.swift:67-74
    public void WorkingSpinner_builds_nine_cells_row_major_with_lag_from_the_bottom_centre_cell()
    {
        var cells = WorkingSpinner.Cells;
        Assert.Equal(9, cells.Count);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(i / 3, cells[i].Row);
            Assert.Equal(i % 3, cells[i].Column);
        }

        // Bottom-centre (row 2, column 1) is the wave's origin: lag 0.
        var bottomCentre = cells.Single(c => c.Row == 2 && c.Column == 1);
        Assert.Equal(0, bottomCentre.Lag);

        // The two top corners (row 0, column 0 and column 2) are farthest: hypot(1, 2) / 2.5.
        var expectedCornerLag = Math.Sqrt(1 * 1 + 2 * 2) / 2.5;
        Assert.Equal(expectedCornerLag, cells.Single(c => c.Row == 0 && c.Column == 0).Lag, 12);
        Assert.Equal(expectedCornerLag, cells.Single(c => c.Row == 0 && c.Column == 2).Lag, 12);
        Assert.True(cells.Max(c => c.Lag) == expectedCornerLag);

        // Every cell matches hypot(column - 1, 2 - row) / 2.5 exactly.
        foreach (var cell in cells)
        {
            var dx = cell.Column - 1;
            var dy = 2 - cell.Row;
            Assert.Equal(Math.Sqrt(dx * dx + dy * dy) / 2.5, cell.Lag, 12);
        }
    }

    // ---- MiniSpinner.Cells (PR-THEME-165, UI-THEME-050) ----

    [Fact]
    [Trait("Req", "PR-THEME-165")] // WorkingIndicators.swift:90-97
    public void MiniSpinner_builds_six_cells_whose_lag_walks_the_clockwise_perimeter_ring()
    {
        var cells = MiniSpinner.Cells;
        Assert.Equal(6, cells.Count);
        (int Row, int Column, double Lag)[] expected =
        [
            (0, 0, 0.0 / 6), (0, 1, 1.0 / 6), (1, 1, 2.0 / 6),
            (2, 1, 3.0 / 6), (2, 0, 4.0 / 6), (1, 0, 5.0 / 6),
        ];
        foreach (var (row, column, lag) in expected)
        {
            var cell = cells.Single(c => c.Row == row && c.Column == column);
            Assert.Equal(lag, cell.Lag, 12);
        }
        // Every (row, column) over 3 rows by 2 columns appears exactly once.
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 2; column++)
                Assert.Single(cells, c => c.Row == row && c.Column == column);
    }

    // ---- Layout geometry (PR-THEME-166, UI-THEME-049, UI-THEME-050) ----

    [Fact]
    [Trait("Req", "PR-THEME-166")] // WorkingIndicators.swift:196-205
    public void ContentSize_is_columns_and_rows_of_cells_plus_08_cellSize_gaps()
    {
        var size = SpinnerGeometry.ContentSize(WorkingSpinner.Cells, 3, 3.5);
        Assert.Equal(3 * 3.5 + 2 * (3.5 * 0.8), size.Width, 12);
        Assert.Equal(3 * 3.5 + 2 * (3.5 * 0.8), size.Height, 12);
    }

    [Fact]
    [Trait("Req", "UI-THEME-049")] // 3 * 3.5 + 2 * 2.8 = 16.1
    public void WorkingSpinner_default_content_size_is_161_points_square()
    {
        var size = SpinnerGeometry.ContentSize(WorkingSpinner.Cells, 3, 3.5);
        Assert.Equal(16.1, size.Width, 9);
        Assert.Equal(16.1, size.Height, 9);
    }

    [Fact]
    [Trait("Req", "UI-THEME-050")] // 2*2.4 + 1*1.92 = 6.72 wide, 3*2.4 + 2*1.92 = 11.04 tall
    public void MiniSpinner_default_content_size_is_672_by_1104()
    {
        var size = SpinnerGeometry.ContentSize(MiniSpinner.Cells, 2, 2.4);
        Assert.Equal(6.72, size.Width, 9);
        Assert.Equal(11.04, size.Height, 9);
    }

    [Fact]
    [Trait("Req", "PR-THEME-166")] // WorkingIndicators.swift:282-283: centred, origin rounded to whole points
    public void CellFrame_centres_the_grid_in_the_offered_bounds_with_a_rounded_origin()
    {
        var content = SpinnerGeometry.ContentSize(WorkingSpinner.Cells, 3, 3.5); // 16.1 x 16.1
        var bounds = new Size(21, 21);
        var cell = new SpinnerCell(0, 0, 0);
        var frame = SpinnerGeometry.CellFrame(cell, bounds, content, 3.5, 3.5 * 0.8);
        var expectedOrigin = Math.Round((21 - 16.1) / 2, MidpointRounding.AwayFromZero); // rounds to 2
        Assert.Equal(expectedOrigin, frame.X);
        Assert.Equal(expectedOrigin, frame.Y);
        Assert.Equal(3.5, frame.Width);
        Assert.Equal(3.5, frame.Height);
    }

    [Fact]
    [Trait("Req", "PR-THEME-166")] // WorkingIndicators.swift:287-290: top-origin layout, row 0 at the top, no reversal
    public void CellFrame_places_row_0_above_row_2_because_the_grid_is_top_origin()
    {
        var content = SpinnerGeometry.ContentSize(WorkingSpinner.Cells, 3, 3.5);
        var bounds = new Size(content.Width, content.Height);
        var top = SpinnerGeometry.CellFrame(new SpinnerCell(0, 0, 0), bounds, content, 3.5, 3.5 * 0.8);
        var bottom = SpinnerGeometry.CellFrame(new SpinnerCell(2, 0, 0), bounds, content, 3.5, 3.5 * 0.8);
        Assert.True(top.Y < bottom.Y);
        Assert.Equal(top.X, bottom.X);
    }

    [Fact]
    [Trait("Req", "PR-THEME-166")] // WorkingIndicators.swift:201-202: a single row or column adds no gap
    public void ContentSize_floors_the_gap_count_at_zero_for_a_single_row_or_column()
    {
        var oneCell = new[] { new SpinnerCell(0, 0, 0) };
        var size = SpinnerGeometry.ContentSize(oneCell, 1, 4.0);
        Assert.Equal(4.0, size.Width);
        Assert.Equal(4.0, size.Height);
    }

    // ---- WorkingWords (PR-CHAT-118) ----

    [Fact]
    [Trait("Req", "PR-CHAT-118")] // WorkingIndicators.swift:323-328
    public void Words_is_the_fixed_list_of_twenty_two_in_order()
    {
        string[] expected =
        [
            "Thinking", "Pondering", "Scheming", "Brewing", "Weaving", "Tinkering",
            "Musing", "Composing", "Sifting", "Untangling", "Distilling", "Sketching",
            "Plotting", "Riffing", "Combobulating", "Percolating", "Marinating",
            "Noodling", "Puzzling", "Conjuring", "Creating", "Dancing",
        ];
        Assert.Equal(expected, WorkingWords.Words);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-118")] // WorkingIndicators.swift:329
    public void RotateSeconds_is_seven() => Assert.Equal(7, WorkingWords.RotateSeconds);

    [Fact]
    [Trait("Req", "PR-CHAT-118")] // WorkingIndicators.swift:332-339: FNV-1a, offset basis and prime, 64-bit wrapping
    public void Seed_is_FNV1a_over_the_identifiers_utf8_bytes()
    {
        // Hand-computed FNV-1a("A"): (0xcbf29ce484222325 ^ 0x41) * 0x100000001b3, mod 2^64.
        var expectedA = unchecked((0xcbf29ce484222325UL ^ 0x41UL) * 0x100000001b3UL);
        Assert.Equal(expectedA, WorkingWords.Seed("A"));

        // The empty string leaves the offset basis untouched.
        Assert.Equal(0xcbf29ce484222325UL, WorkingWords.Seed(""));

        // Two different identifiers give two different seeds (so two threads do not say the same thing).
        Assert.NotEqual(WorkingWords.Seed("11111111-1111-1111-1111-111111111111"),
            WorkingWords.Seed("22222222-2222-2222-2222-222222222222"));

        // Deterministic: the same identifier always gives the same seed.
        Assert.Equal(WorkingWords.Seed("thread-x"), WorkingWords.Seed("thread-x"));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-118")] // WorkingIndicators.swift:341-344
    public void Word_steps_every_seven_whole_seconds_and_wraps_modulo_the_word_count()
    {
        var seed = WorkingWords.Seed("thread-y");
        var w0 = WorkingWords.Word(seed, 0);
        Assert.Equal(w0, WorkingWords.Word(seed, 6)); // still the same word through second 6
        Assert.NotEqual(w0, WorkingWords.Word(seed, 7)); // the seventh whole second rotates it (unless it wraps to
                                                          // the same index, which the word count of 22 avoids here)
        Assert.Equal(WorkingWords.Words[(int)(seed % 22)], w0);
        Assert.Equal(WorkingWords.Words[(int)(unchecked(seed + 1) % 22)], WorkingWords.Word(seed, 7));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-118")] // WorkingIndicators.swift:342: max(0, elapsedSeconds) / rotateSeconds
    public void Word_treats_a_negative_elapsed_time_as_zero()
    {
        var seed = WorkingWords.Seed("thread-z");
        Assert.Equal(WorkingWords.Word(seed, 0), WorkingWords.Word(seed, -100));
    }
}

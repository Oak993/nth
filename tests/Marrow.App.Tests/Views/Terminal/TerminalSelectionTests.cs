using Marrow.App.Views.Terminal.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// UI-TERM-010: the cell ranges SwiftTerm 1.20.0's SelectionService makes (Sources/SwiftTerm/SelectionService.swift), checked on a line written out by hand:
//
//   col  0         1         2         3
//        0123456789012345678901234567890123456789
//   row0 hello world.foo-bar (a [b] c) x ,
//
// "hello" 0-4, a space at 5, "world.foo-bar" 6-18, a space at 19, "(a [b] c)" 20-28, a space at 29, "x" 30, a space at 31, "," at 32; 33 on were never written.
// Every expected start and end below follows from the Swift of the cited lines; an end is exclusive.
public sealed class TerminalSelectionTests
{
    private static TerminalFrame Frame()
    {
        var screen = new TerminalScreenBuffer(40, 5);
        screen.Feed("hello world.foo-bar (a [b] c) x ,");
        return screen.Snapshot();
    }

    private static (TerminalPosition Start, TerminalPosition End) Double(int column, int row = 0)
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(column, row), frame, 0);
        Assert.True(selection.Active);
        Assert.Equal(TerminalSelectionMode.Word, selection.Mode);
        return (selection.Start, selection.End);
    }

    [Theory]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:663-675, :501-533: letters, digits and . _ - make a word, wherever in it the click lands
    [InlineData(0, 0, 5)]
    [InlineData(4, 0, 5)]
    [InlineData(6, 6, 19)]
    [InlineData(8, 6, 19)]
    [InlineData(12, 6, 19)] // the dot and the dash lie inside the word the scan finds from a letter
    [InlineData(16, 6, 19)]
    [InlineData(18, 6, 19)]
    [InlineData(30, 30, 31)]
    public void A_double_click_on_a_letter_selects_the_run_of_letters_digits_dots_underscores_and_dashes(int column, int start, int end) =>
        Assert.Equal((new TerminalPosition(start, 0), new TerminalPosition(end, 0)), Double(column));

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:668-670: a written blank selects its run of written blanks
    public void A_double_click_on_a_space_selects_the_spaces_around_it()
    {
        Assert.Equal((new TerminalPosition(5, 0), new TerminalPosition(6, 0)), Double(5));
        var screen = new TerminalScreenBuffer(40, 5);
        screen.Feed("a   b");
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(2, 0), screen.Snapshot(), 0);
        Assert.Equal((new TerminalPosition(1, 0), new TerminalPosition(4, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:666-667: cells never written are the NUL character, a run of its own up to the end of the row
    public void A_double_click_past_the_text_selects_the_unwritten_cells_to_the_end_of_the_row()
    {
        Assert.Equal((new TerminalPosition(33, 0), new TerminalPosition(40, 0)), Double(36));
        Assert.Equal((new TerminalPosition(0, 1), new TerminalPosition(40, 1)), Double(10, 1)); // a row with nothing on it
    }

    [Theory]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:672-681 with :535-575: an opening bracket selects to its balanced closer, across nested pairs
    [InlineData(20, 20, 29)] // ( to its )
    [InlineData(23, 23, 26)] // [ to its ]
    public void A_double_click_on_an_opening_bracket_selects_to_the_one_that_closes_it(int column, int start, int end) =>
        Assert.Equal((new TerminalPosition(start, 0), new TerminalPosition(end, 0)), Double(column));

    [Theory]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:682-686 with :577-615: a closing bracket selects back to the one that opens it
    [InlineData(28, 20, 29)]
    [InlineData(25, 23, 26)]
    public void A_double_click_on_a_closing_bracket_selects_back_to_the_one_that_opens_it(int column, int start, int end) =>
        Assert.Equal((new TerminalPosition(start, 0), new TerminalPosition(end, 0)), Double(column));

    [Theory]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:687-691: any other character selects nothing, but starts a word selection there (a dot or a dash only joins a word from a letter)
    [InlineData(32)]
    [InlineData(11)]
    [InlineData(15)]
    public void A_double_click_on_punctuation_selects_no_range_but_is_active(int column)
    {
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(column, 0), Frame(), 0);
        Assert.True(selection.Active);
        Assert.False(selection.HasRange);
        Assert.Equal(new TerminalPosition(column, 0), selection.Start);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:664-667: the position is held to the columns and the top of the buffer
    public void A_double_click_position_is_held_to_the_grid()
    {
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(-5, -3), Frame(), 0);
        Assert.Equal((new TerminalPosition(0, 0), new TerminalPosition(5, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:472-481
    public void A_triple_click_selects_the_whole_row()
    {
        var selection = new TerminalSelection();
        selection.SelectRow(2, Frame());
        Assert.True(selection.Active);
        Assert.Equal(TerminalSelectionMode.Row, selection.Mode);
        Assert.Equal((new TerminalPosition(0, 2), new TerminalPosition(39, 2)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:277-281, :247-254, :408-444
    public void A_drag_extends_the_end_from_the_soft_start()
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SetSoftStart(new TerminalPosition(2, 0));
        selection.StartSelection();
        selection.DragExtend(new TerminalPosition(8, 0), frame);
        Assert.Equal((new TerminalPosition(2, 0), new TerminalPosition(8, 0)), (selection.Start, selection.End));
        Assert.Equal(TerminalSelectionMode.Character, selection.Mode);
        selection.DragExtend(new TerminalPosition(1, 0), frame); // back past the start: the range is the other way round
        Assert.Equal((new TerminalPosition(2, 0), new TerminalPosition(1, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:307-345: a shift click moves the end; before the start, the start moves to the old end
    public void A_shift_click_extends_the_selection_and_swaps_the_start_when_it_goes_before_it()
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SetSoftStart(new TerminalPosition(2, 0));
        selection.StartSelection();
        selection.DragExtend(new TerminalPosition(8, 0), frame);
        selection.ShiftExtend(new TerminalPosition(14, 0), frame);
        Assert.Equal((new TerminalPosition(2, 0), new TerminalPosition(14, 0)), (selection.Start, selection.End));
        selection.ShiftExtend(new TerminalPosition(1, 0), frame);
        Assert.Equal((new TerminalPosition(14, 0), new TerminalPosition(1, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:307-322 with :617-661: after a double click a shift click snaps to word boundaries
    public void A_shift_click_after_a_double_click_ends_at_the_edge_of_the_word_it_lands_in()
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(8, 0), frame, 0);
        selection.ShiftExtend(new TerminalPosition(30, 0), frame);
        Assert.Equal((new TerminalPosition(6, 0), new TerminalPosition(31, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:408-432: a drag after a double click keeps the seed word whole and snaps both ends to words
    public void A_drag_after_a_double_click_keeps_the_seed_word_and_snaps_to_words_either_way()
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SelectWordOrExpression(new TerminalPosition(8, 0), frame, 0);
        selection.DragExtend(new TerminalPosition(3, 0), frame); // back into "hello"
        Assert.Equal((new TerminalPosition(0, 0), new TerminalPosition(19, 0)), (selection.Start, selection.End));
        selection.DragExtend(new TerminalPosition(30, 0), frame); // forward to "x"
        Assert.Equal((new TerminalPosition(6, 0), new TerminalPosition(31, 0)), (selection.Start, selection.End));
        selection.DragExtend(new TerminalPosition(10, 0), frame); // back inside the seed word
        Assert.Equal((new TerminalPosition(6, 0), new TerminalPosition(19, 0)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:483-490, :309-312, :409-413: after a triple click a shift click or a drag grows the selection by whole rows
    public void A_shift_click_or_drag_after_a_triple_click_grows_by_whole_rows()
    {
        var frame = Frame();
        var selection = new TerminalSelection();
        selection.SelectRow(2, frame);
        selection.ShiftExtend(new TerminalPosition(5, 4), frame);
        Assert.Equal((new TerminalPosition(0, 2), new TerminalPosition(39, 4)), (selection.Start, selection.End));
        selection.DragExtend(new TerminalPosition(5, 0), frame);
        Assert.Equal((new TerminalPosition(0, 0), new TerminalPosition(39, 2)), (selection.Start, selection.End));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SelectionService.swift:705-714
    public void Selecting_none_deactivates_and_forgets_the_mode()
    {
        var selection = new TerminalSelection();
        selection.SelectRow(1, Frame());
        selection.SelectNone();
        Assert.False(selection.Active);
        Assert.Equal(TerminalSelectionMode.Character, selection.Mode);
    }
}

using Avalonia.Media;
using Marrow.App.Views.Terminal.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

public sealed class TerminalScreenBufferTests
{
    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Vt_cursor_motion_and_erase_rewrite_the_screen_instead_of_appending_a_log()
    {
        var screen = new TerminalScreenBuffer(8, 3);
        screen.Feed("hello\r\x1b[2Kbye");
        Assert.Equal("bye", screen.LineText(0));
        Assert.Equal(3, screen.CursorColumn);
        screen.Feed("\x1b[2;4Hred\x1b[1D!");
        Assert.Equal("   re!", screen.LineText(1));
        screen.Feed("\x1b[1;2H\x1b[0K");
        Assert.Equal("b", screen.LineText(0));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Split_escape_sequences_scroll_the_viewport_and_preserve_source_500_line_scrollback()
    {
        var screen = new TerminalScreenBuffer(6, 2);
        screen.Feed("one\r\ntwo\r\nthree");
        Assert.Equal("two", screen.LineText(0));
        Assert.Equal("three", screen.LineText(1));
        Assert.Equal("one", screen.ScrollbackLines[0]);
        screen.Feed("\x1b[");
        screen.Feed("2J");
        Assert.Equal("", screen.LineText(0));
        Assert.Equal("", screen.LineText(1));
        Assert.Equal("one", screen.ScrollbackLines[0]);
        for (var i = 0; i < 510; i++) screen.Feed($"\r\n{i}");
        Assert.Equal(500, screen.ScrollbackLines.Count);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Sgr_attributes_use_SwiftTerm_terminal_app_colours_and_support_true_colour()
    {
        var screen = new TerminalScreenBuffer(10, 2);
        screen.Feed("\x1b[31;44;1mR\x1b[0mD\x1b[38;2;1;2;3mT");
        Assert.Equal(Color.FromRgb(194, 54, 33), screen.CellAt(0, 0).Foreground);
        Assert.Equal(Color.FromRgb(73, 46, 225), screen.CellAt(0, 0).Background);
        Assert.True(screen.CellAt(0, 0).Bold);
        Assert.Null(screen.CellAt(1, 0).Foreground);
        Assert.Null(screen.CellAt(1, 0).Background);
        Assert.Equal(Color.FromRgb(1, 2, 3), screen.CellAt(2, 0).Foreground);
        // Colors.swift:200-242: cube corners are the base8 Lab anchors, with theme bg/fg
        // replacing corners 0/7. This rules out the prototype's guessed xterm RGB cube.
        screen.SetDefaultColors(Color.FromRgb(12, 24, 36), Color.FromRgb(240, 242, 244));
        screen.Feed("\x1b[38;5;16mB\x1b[38;5;21mC\x1b[38;5;196mR\x1b[38;5;231mF");
        Assert.Equal(Color.FromRgb(12, 24, 36), screen.CellAt(3, 0).Foreground);
        Assert.Equal(Color.FromRgb(73, 46, 225), screen.CellAt(4, 0).Foreground);
        Assert.Equal(Color.FromRgb(194, 54, 33), screen.CellAt(5, 0).Foreground);
        Assert.Equal(Color.FromRgb(240, 242, 244), screen.CellAt(6, 0).Foreground);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Alternate_screen_and_bracketed_paste_modes_follow_vt_private_sequences()
    {
        var screen = new TerminalScreenBuffer(8, 2);
        screen.Feed("normal\x1b[?1049halt\x1b[?2004h");
        Assert.Equal("alt", screen.LineText(0));
        Assert.True(screen.BracketedPasteMode);
        screen.Feed("\x1b[?1049l\x1b[?2004l");
        Assert.Equal("normal", screen.LineText(0));
        Assert.False(screen.BracketedPasteMode);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Cell_selection_copies_the_visible_grid_without_control_sequences()
    {
        var screen = new TerminalScreenBuffer(8, 2);
        screen.Feed("first\r\nsecond");
        Assert.Equal("rst\nsec", screen.CopySelection((2, 0), (3, 1)));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Terminal.swift:3537-3546 cmdRestoreCursor.
    public void Dec_saved_cursor_restore_clamps_after_row_and_column_shrink() =>
        AssertSavedCursorRestore("\u001b7", "\u001b8");

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Terminal.swift:3537-3546 CSI u shares the cursor restore.
    public void Csi_saved_cursor_restore_clamps_after_row_and_column_shrink() =>
        AssertSavedCursorRestore("\u001b[s", "\u001b[u");

    private static void AssertSavedCursorRestore(string save, string restore)
    {
        // E's exact reproduction: restoring saved row 3 after shrinking to two rows
        // must permit the next printable byte without indexing past wrapped/lines.
        var screen = new TerminalScreenBuffer(8, 4);
        screen.Feed("\u001b[4;1H" + save);
        screen.Resize(8, 2);
        screen.Feed(restore + "x");
        Assert.Equal(1, screen.CursorRow);
        Assert.Equal(1, screen.CursorColumn);
        Assert.Equal("x", screen.CellAt(0, 1).Text);

        foreach (var (rows, savedRow) in new[] { (4, 1), (2, 4) })
        {
            var narrow = new TerminalScreenBuffer(8, 4);
            narrow.Feed($"\u001b[{savedRow};8H" + save);
            narrow.Resize(2, rows);
            // Move away first so omitting restore cannot accidentally pass.
            narrow.Feed("\u001b[1;1H" + restore);
            var expectedRow = Math.Min(savedRow - 1, rows - 1);
            Assert.Equal(1, narrow.CursorColumn);
            Assert.Equal(expectedRow, narrow.CursorRow);
            narrow.Feed("x");
            Assert.Equal("x", narrow.CellAt(1, expectedRow).Text);
            Assert.Equal(expectedRow, narrow.CursorRow);
            Assert.Equal(1, narrow.CursorColumn);
        }
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm v1.20.0 Terminal.swift:1386-1490; CharData.swift:259-285
    public void Unicode_scalars_and_combining_marks_occupy_the_source_cell_widths()
    {
        var combining = new TerminalScreenBuffer(8, 3);
        combining.Feed("e");
        combining.Feed("\u0301");
        Assert.Equal(1, combining.CursorColumn);
        Assert.Equal("e\u0301", combining.LineText(0));
        var wide = new TerminalScreenBuffer(8, 3);
        wide.Feed("\u4e2d");
        Assert.Equal(2, wide.CursorColumn);
        wide.Feed("\U0001f642");
        Assert.Equal(4, wide.CursorColumn);
        var screen = new TerminalScreenBuffer(20, 3);
        const string text = "caf\u00e9 e\u0301 \u4e2d\U0001f642";
        screen.Feed(text);
        Assert.Equal(text, screen.LineText(0));
        Assert.Equal(11, screen.CursorColumn);
        Assert.Equal(text, screen.CopySelection((0, 0), (11, 0)));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Buffer.swift:1136-1138,1338-1415,1533-1543
    [Trait("Req", "PR-TERM-033")]
    [Trait("Req", "PR-TERM-034")]
    public void Resizing_reflows_finished_wrapped_lines_with_attributes_and_leaves_the_cursor_paragraph_to_the_child()
    {
        var screen = new TerminalScreenBuffer(8, 8);
        screen.Feed("\x1b[31mabcdefghijklmnop\x1b[0m\r\nprompt");
        screen.Resize(4, 8);
        Assert.Equal("abcd", screen.LineText(0));
        Assert.Equal("efgh", screen.LineText(1));
        Assert.Equal("ijkl", screen.LineText(2));
        Assert.Equal("mnop", screen.LineText(3));
        Assert.Equal(Color.FromRgb(194, 54, 33), screen.CellAt(0, 3).Foreground);
        Assert.Equal(4, screen.CursorRow);
        Assert.Equal("prom", screen.LineText(4)); // source excludes the cursor paragraph from reflow
        screen.Resize(8, 8);
        Assert.Equal("abcdefgh", screen.LineText(0));
        Assert.Equal("ijklmnop", screen.LineText(1));
        Assert.Equal(Color.FromRgb(194, 54, 33), screen.CellAt(7, 1).Foreground);
        Assert.Equal(2, screen.CursorRow);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Terminal.swift:6723-6726 eraseAttr
    public void Erasing_uses_the_current_background_and_default_foreground_without_moving_the_cursor()
    {
        var screen = new TerminalScreenBuffer(8, 3);
        screen.Feed("\x1b[31;44;1mabc\x1b[2K");
        Assert.Equal("", screen.LineText(0));
        Assert.Equal(Color.FromRgb(73, 46, 225), screen.CellAt(0, 0).Background);
        Assert.Null(screen.CellAt(0, 0).Foreground);
        Assert.False(screen.CellAt(0, 0).Bold);
        Assert.Equal(3, screen.CursorColumn);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Terminal.swift cmdCharAttributes and Buffer.swift insertCharacter
    public void An_attribute_sequence_at_the_right_margin_keeps_the_pending_wrap()
    {
        var screen = new TerminalScreenBuffer(4, 3);
        screen.Feed("abcd\x1b[31me");
        Assert.Equal("abcd", screen.LineText(0));
        Assert.Equal("e", screen.LineText(1));
        Assert.Equal(Color.FromRgb(194, 54, 33), screen.CellAt(0, 1).Foreground);
    }
}

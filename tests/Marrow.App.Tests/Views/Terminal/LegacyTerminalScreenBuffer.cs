using System.Text;
using Avalonia.Media;
using Marrow.App.Views.Terminal.Port;

namespace Marrow.App.Tests.Views.Terminal;

// ED-NEW-lean-10: a frozen copy of TerminalScreenBuffer as it was before rows became copy-on-write
// (claude/release-integration, 2026-10-12). Its Snapshot deep-copies every row, so it is the reference the
// copy-on-write buffer must match at every step. Do not edit it to follow later changes of the real buffer.
internal sealed class LegacyTerminalScreenBuffer
{
    private const int ScrollbackCapacity = 500; // SwiftTerm TerminalOptions.swift:118
    private Color[] indexedColors = TerminalPalette.Generate();

    private readonly List<TerminalCell[]> scrollback = [];
    private readonly List<bool> scrollbackWrapped = [];
    private TerminalCell[][] lines;
    private bool[] wrapped;
    private TerminalCell[][]? savedNormalLines;
    private bool[]? savedNormalWrapped;
    private int savedNormalColumn;
    private int savedNormalRow;
    private int savedColumn;
    private int savedRow;
    private int scrollTop;
    private int scrollBottom;
    private Color? foreground;
    private Color? background;
    private bool bold;
    private bool underline;
    private bool inverse;
    private bool wrapPending;
    private ParseState parseState;
    private readonly StringBuilder control = new();

    private enum ParseState { Ground, Escape, Csi, Osc, OscEscape }

    public LegacyTerminalScreenBuffer(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        lines = NewLines(Columns, Rows);
        wrapped = new bool[Rows];
        scrollBottom = Rows - 1;
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorColumn { get; private set; }
    public int CursorRow { get; private set; }
    public bool ApplicationCursorMode { get; private set; }
    public bool BracketedPasteMode { get; private set; }
    public bool CursorVisible { get; private set; } = true; // SwiftTerm DECTCEM defaults on
    /// <summary>SwiftTerm Terminal.swift:727-731 <c>mouseMode</c>: what the program asked to be told of (DECSET 9, 1000, 1002, 1003).</summary>
    public TerminalMouseMode MouseMode { get; private set; }
    /// <summary>:589 <c>mouseProtocol</c>: how reported events are written (DECSET 1005, 1006, 1015, 1016).</summary>
    public TerminalMouseEncoding MouseEncoding { get; private set; }
    /// <summary>:735 <c>mouseShiftCapture</c>: XTSHIFTESCAPE (<c>CSI &gt; 1 s</c>) asks that a shift-click reach the program instead of selecting.</summary>
    public bool MouseShiftCapture { get; private set; }
    public bool IsAlternateScreen => savedNormalLines is not null;
    public IReadOnlyList<string> ScrollbackLines => scrollback.Select(TrimLine).ToArray();
    public string? Title { get; private set; }
    public string? CurrentDirectory { get; private set; }

    public void SetDefaultColors(Color? backgroundColor, Color? foregroundColor) =>
        indexedColors = TerminalPalette.Generate(backgroundColor, foregroundColor);

    public TerminalCell CellAt(int column, int row) => lines[row][column];

    public string LineText(int row) => TrimLine(lines[row]);

    public string AllText() => string.Join('\n', ScrollbackLines.Concat(Enumerable.Range(0, Rows).Select(LineText)));

    public TerminalFrame Snapshot() => new(scrollback.Concat(lines).Select(line => line.ToArray()).ToArray(),
        scrollbackWrapped.Concat(wrapped).ToArray(), scrollback.Count, Columns, Rows,
        CursorColumn, CursorRow, CursorVisible);

    public string CopySelection((int Column, int Row) start, (int Column, int Row) end)
    {
        if (start.Row > end.Row || start.Row == end.Row && start.Column > end.Column)
            (start, end) = (end, start);
        var result = new List<string>();
        for (var row = Math.Clamp(start.Row, 0, Rows - 1); row <= Math.Clamp(end.Row, 0, Rows - 1); row++)
        {
            var from = row == start.Row ? Math.Clamp(start.Column, 0, Columns) : 0;
            var to = row == end.Row ? Math.Clamp(end.Column, 0, Columns) : Columns;
            var text = string.Concat(lines[row].Skip(from).Take(Math.Max(0, to - from))
                .Select(cell => cell.Text ?? " "));
            result.Add(text.TrimEnd());
        }
        return string.Join('\n', result);
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows) return;
        // SwiftTerm Buffer.swift:1136-1138,1365-1372 excludes the cursor's paragraph:
        // the child will redraw it after its PTY size notification. Completed paragraphs reflow.
        var all = scrollback.Concat(lines).ToArray();
        var flags = scrollbackWrapped.Concat(wrapped).ToArray();
        var cursor = scrollback.Count + CursorRow;
        var result = new List<TerminalCell[]>();
        var resultWrapped = new List<bool>();
        var newCursor = cursor;
        for (var first = 0; first < all.Length;)
        {
            var end = first + 1;
            while (end < all.Length && flags[end]) end++;
            if (columns == Columns || IsAlternateScreen || cursor >= first && cursor < end)
            {
                if (cursor >= first && cursor < end) newCursor = result.Count + cursor - first;
                for (var row = first; row < end; row++)
                {
                    var copy = new TerminalCell[columns];
                    Array.Copy(all[row], copy, Math.Min(columns, all[row].Length));
                    result.Add(copy);
                    resultWrapped.Add(flags[row]);
                }
            }
            else
            {
                var cells = new List<TerminalCell>();
                for (var row = first; row < end; row++)
                {
                    var length = row == end - 1 ? ContentLength(all[row]) : Columns;
                    // Buffer.getWrappedLineTrimmedLength omits the padding before a wide glyph.
                    if (row + 1 < end && length == Columns && all[row][length - 1].Text is null
                        && all[row + 1][0].Width == 2) length--;
                    cells.AddRange(all[row].Take(length).Where(cell => !cell.IsContinuation));
                }
                var line = new TerminalCell[columns];
                var column = 0;
                var continuation = flags[first];
                foreach (var cell in cells)
                {
                    var width = Math.Clamp(cell.Width, 1, Math.Min(2, columns));
                    if (column + width > columns)
                    {
                        result.Add(line); resultWrapped.Add(continuation);
                        line = new TerminalCell[columns]; column = 0; continuation = true;
                    }
                    line[column] = cell with { Width = width };
                    if (width == 2) line[column + 1] = cell with { Text = "", Width = 0 };
                    column += width;
                }
                result.Add(line); resultWrapped.Add(continuation);
            }
            first = end;
        }
        // Buffer.resize removes rows below the cursor first, and only then advances scrollback.
        while (result.Count > scrollback.Count + rows && result.Count - 1 > newCursor)
        { result.RemoveAt(result.Count - 1); resultWrapped.RemoveAt(resultWrapped.Count - 1); }
        var history = Math.Max(0, result.Count - rows);
        scrollback.Clear(); scrollbackWrapped.Clear();
        for (var row = Math.Max(0, history - ScrollbackCapacity); row < history; row++)
        { scrollback.Add(result[row]); scrollbackWrapped.Add(resultWrapped[row]); }
        var replacement = NewLines(columns, rows);
        var replacementWrapped = new bool[rows];
        for (var row = history; row < result.Count; row++)
        { replacement[row - history] = result[row]; replacementWrapped[row - history] = resultWrapped[row]; }
        Columns = columns;
        Rows = rows;
        lines = replacement;
        wrapped = replacementWrapped;
        CursorColumn = Math.Clamp(CursorColumn, 0, columns - 1);
        CursorRow = Math.Clamp(newCursor - history, 0, rows - 1);
        scrollTop = 0;
        scrollBottom = rows - 1;
        wrapPending = false;
        if (savedNormalLines is not null)
        {
            var normal = NewLines(columns, rows);
            for (var row = 0; row < Math.Min(rows, savedNormalLines.Length); row++)
                Array.Copy(savedNormalLines[row], normal[row], Math.Min(columns, savedNormalLines[row].Length));
            savedNormalLines = normal;
            Array.Resize(ref savedNormalWrapped, rows);
            savedNormalColumn = Math.Clamp(savedNormalColumn, 0, columns - 1);
            savedNormalRow = Math.Clamp(savedNormalRow, 0, rows - 1);
        }
    }

    public void Feed(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var character = rune.Value <= char.MaxValue ? (char)rune.Value : '\0';
            switch (parseState)
            {
                case ParseState.Ground:
                    FeedGround(rune);
                    break;
                case ParseState.Escape:
                    FeedEscape(character);
                    break;
                case ParseState.Csi:
                    if (character is >= '@' and <= '~')
                    {
                        ExecuteCsi(character, control.ToString());
                        control.Clear();
                        parseState = ParseState.Ground;
                    }
                    else if (control.Length < 256) control.Append(character);
                    break;
                case ParseState.Osc:
                    if (character == '\a') { ExecuteOsc(); parseState = ParseState.Ground; }
                    else if (character == '\x1b') parseState = ParseState.OscEscape;
                    else if (control.Length < 4096) control.Append(rune.ToString());
                    break;
                case ParseState.OscEscape:
                    if (character == '\\') { ExecuteOsc(); parseState = ParseState.Ground; }
                    else { control.Append('\x1b').Append(character); parseState = ParseState.Osc; }
                    break;
            }
        }
    }

    private void FeedGround(Rune rune)
    {
        switch (rune.Value)
        {
            case 0: return;
            case '\x1b': parseState = ParseState.Escape; return;
            case '\r': CursorColumn = 0; wrapPending = false; return;
            case '\n': LineFeed(); return;
            case '\b': CursorColumn = Math.Max(0, CursorColumn - 1); wrapPending = false; return;
            case '\t': CursorColumn = Math.Min(Columns - 1, (CursorColumn / 8 + 1) * 8); return;
        }
        var width = TerminalUnicode.Width(rune);
        if (width < 0) return;
        var previousRow = CursorRow;
        var previousColumn = wrapPending ? CursorColumn : CursorColumn - 1;
        if (previousColumn < 0 && CursorRow > 0 && wrapped[CursorRow])
        { previousRow--; previousColumn = Columns - 1; }
        while (previousColumn > 0 && lines[previousRow][previousColumn].IsContinuation) previousColumn--;
        var previous = previousColumn >= 0 ? lines[previousRow][previousColumn] : default;
        // Terminal.swift:1440-1535 combines marks, joiners and regional-indicator pairs into one cell.
        var regionalPair = rune.Value is >= 0x1f1e6 and <= 0x1f1ff && previous.Text is not null
            && previous.Text.EnumerateRunes().Count() == 1
            && previous.Text.EnumerateRunes().First().Value is >= 0x1f1e6 and <= 0x1f1ff;
        if (previousColumn >= 0 && previous.Text is not null
            && (width == 0 || previous.Text.EndsWith("\u200d", StringComparison.Ordinal) || regionalPair))
        {
            lines[previousRow][previousColumn] = previous with { Text = previous.Text + rune.ToString() };
            return;
        }
        if (width == 0) return;
        width = Math.Min(width, Columns);
        if (wrapPending || CursorColumn + width > Columns)
        { CursorColumn = 0; LineFeed(continuation: true); wrapPending = false; }
        lines[CursorRow][CursorColumn] = new TerminalCell(rune.ToString(), foreground, background, bold, underline, inverse, width);
        if (width == 2) lines[CursorRow][CursorColumn + 1] = new TerminalCell("", foreground, background, bold, underline, inverse, 0);
        var nextColumn = CursorColumn + width;
        wrapPending = nextColumn >= Columns;
        CursorColumn = Math.Min(Columns - 1, nextColumn);
    }

    private void FeedEscape(char character)
    {
        parseState = ParseState.Ground;
        switch (character)
        {
            case '[': control.Clear(); parseState = ParseState.Csi; break;
            case ']': control.Clear(); parseState = ParseState.Osc; break;
            case '7': savedColumn = CursorColumn; savedRow = CursorRow; break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break;
            case 'E': CursorColumn = 0; LineFeed(); break;
            case 'M': ReverseLineFeed(); break;
        }
    }

    private void ExecuteOsc()
    {
        var value = control.ToString();
        control.Clear();
        var separator = value.IndexOf(';', StringComparison.Ordinal);
        if (separator < 0) return;
        var code = value[..separator];
        if (code is "0" or "2") Title = value[(separator + 1)..];
        if (code == "7") CurrentDirectory = value[(separator + 1)..];
    }

    // SwiftTerm Terminal.swift:5260-5304 (reset) and :5495-5554 (set). The tracking modes 9 and 1000-1003 each name their mode when set and turn it
    // off when reset, whichever mode was on; the encodings 1005, 1006, 1015 and 1016 only choose how events are written (a reset returns to the
    // default encoding and leaves tracking as it is: :5290-5297).
    private void SetMouseMode(int mode, bool set)
    {
        switch (mode)
        {
            case 9: MouseMode = set ? TerminalMouseMode.X10 : TerminalMouseMode.Off; break;
            case 1000: MouseMode = set ? TerminalMouseMode.Vt200 : TerminalMouseMode.Off; break;
            case 1002: MouseMode = set ? TerminalMouseMode.ButtonEventTracking : TerminalMouseMode.Off; break;
            case 1003: MouseMode = set ? TerminalMouseMode.AnyEvent : TerminalMouseMode.Off; break;
            case 1005: MouseEncoding = set ? TerminalMouseEncoding.Utf8 : TerminalMouseEncoding.X10; break;
            case 1006: MouseEncoding = set ? TerminalMouseEncoding.Sgr : TerminalMouseEncoding.X10; break;
            case 1015: MouseEncoding = set ? TerminalMouseEncoding.Urxvt : TerminalMouseEncoding.X10; break;
            case 1016: MouseEncoding = set ? TerminalMouseEncoding.SgrPixel : TerminalMouseEncoding.X10; break;
        }
    }

    private void ExecuteCsi(char final, string argument)
    {
        var privateMode = argument.StartsWith("?", StringComparison.Ordinal);
        if (privateMode) argument = argument[1..];
        var parameters = argument.Split(';').Select(value => int.TryParse(value, out var parsed) ? parsed : 0).ToArray();
        int Param(int index, int fallback = 1) => index < parameters.Length && parameters[index] != 0 ? parameters[index] : fallback;
        // XTSHIFTESCAPE (SwiftTerm Terminal.swift:6895-6905, parsed at EscapeSequenceParser.swift:467): CSI > Ps s.
        if (final == 's' && argument.StartsWith(">", StringComparison.Ordinal))
        {
            var shift = argument.Length > 1 && int.TryParse(argument[1..].Split(';')[0], out var requested) ? requested : 0;
            if (shift == 0) MouseShiftCapture = false;
            else if (shift == 1) MouseShiftCapture = true;
            return;
        }
        if (privateMode)
        {
            foreach (var mode in parameters)
            {
                // SwiftTerm Terminal.swift:5236-5237,5470-5471 (DECCKM).
                if (mode == 1 && final is 'h' or 'l') ApplicationCursorMode = final == 'h';
                if (mode == 2004 && final is 'h' or 'l') BracketedPasteMode = final == 'h';
                if (mode == 25 && final is 'h' or 'l') CursorVisible = final == 'h';
                if (final is 'h' or 'l') SetMouseMode(mode, final == 'h');
                if (mode is 47 or 1047 or 1049 && final is 'h' or 'l') SwitchAlternate(final == 'h');
            }
            return;
        }
        // Changing attributes does not consume the pending right-margin wrap.
        if (final is not 'm' and not 'n') wrapPending = false;
        switch (final)
        {
            case 'A': CursorRow = Math.Max(0, CursorRow - Param(0)); break;
            case 'B': CursorRow = Math.Min(Rows - 1, CursorRow + Param(0)); break;
            case 'C': CursorColumn = Math.Min(Columns - 1, CursorColumn + Param(0)); break;
            case 'D': CursorColumn = Math.Max(0, CursorColumn - Param(0)); break;
            case 'E': CursorRow = Math.Min(Rows - 1, CursorRow + Param(0)); CursorColumn = 0; break;
            case 'F': CursorRow = Math.Max(0, CursorRow - Param(0)); CursorColumn = 0; break;
            case 'G': CursorColumn = Math.Clamp(Param(0) - 1, 0, Columns - 1); break;
            case 'H' or 'f':
                CursorRow = Math.Clamp(Param(0) - 1, 0, Rows - 1);
                CursorColumn = Math.Clamp(Param(1) - 1, 0, Columns - 1);
                break;
            case 'J': EraseDisplay(parameters[0]); break;
            case 'K': EraseLine(parameters[0]); break;
            case 'm': ApplySgr(parameters); break;
            case 'r': scrollTop = Math.Clamp(Param(0) - 1, 0, Rows - 1);
                scrollBottom = Math.Clamp(Param(1, Rows) - 1, scrollTop, Rows - 1);
                CursorColumn = 0; CursorRow = 0; break;
            case 'S': for (var i = 0; i < Param(0); i++) ScrollUp(); break;
            case 'T': for (var i = 0; i < Param(0); i++) ScrollDown(); break;
            case '@': InsertCells(Param(0)); break;
            case 'P': DeleteCells(Param(0)); break;
            case 'X': for (var i = CursorColumn; i < Math.Min(Columns, CursorColumn + Param(0)); i++) lines[CursorRow][i] = EraseCell(); break;
            case 'L': for (var i = 0; i < Param(0); i++) ScrollDown(CursorRow); break;
            case 'M': for (var i = 0; i < Param(0); i++) ScrollUp(CursorRow); break;
            case 's': savedColumn = CursorColumn; savedRow = CursorRow; break;
            case 'u': RestoreCursor(); break;
        }
    }

    private void RestoreCursor()
    {
        // SwiftTerm Terminal.swift:3543-3546 bounds saved coordinates at restore;
        // resize/reflow can leave them outside the current viewport.
        CursorColumn = Math.Clamp(savedColumn, 0, Columns - 1);
        CursorRow = Math.Clamp(savedRow, 0, Rows - 1);
    }

    private void ApplySgr(int[] parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var code = parameters[i];
            switch (code)
            {
                case 0: foreground = null; background = null; bold = false; underline = false; inverse = false; break;
                case 1: bold = true; break;
                case 4: underline = true; break;
                case 7: inverse = true; break;
                case 22: bold = false; break;
                case 24: underline = false; break;
                case 27: inverse = false; break;
                case 39: foreground = null; break;
                case 49: background = null; break;
                case >= 30 and <= 37: foreground = TerminalPalette.Ansi[code - 30]; break;
                case >= 40 and <= 47: background = TerminalPalette.Ansi[code - 40]; break;
                case >= 90 and <= 97: foreground = TerminalPalette.Ansi[code - 90 + 8]; break;
                case >= 100 and <= 107: background = TerminalPalette.Ansi[code - 100 + 8]; break;
                case 38 or 48 when i + 2 < parameters.Length && parameters[i + 1] == 5:
                    var indexed = indexedColors[Math.Clamp(parameters[i + 2], 0, 255)];
                    if (code == 38) foreground = indexed; else background = indexed;
                    i += 2;
                    break;
                case 38 or 48 when i + 4 < parameters.Length && parameters[i + 1] == 2:
                    var rgb = Color.FromRgb((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]);
                    if (code == 38) foreground = rgb; else background = rgb;
                    i += 4;
                    break;
            }
        }
    }

    private void EraseDisplay(int mode)
    {
        if (mode == 3) { scrollback.Clear(); scrollbackWrapped.Clear(); return; }
        if (mode is < 0 or > 2) return;
        for (var row = 0; row < Rows; row++)
        {
            var from = mode == 0 && row == CursorRow ? CursorColumn : 0;
            var to = mode == 1 && row == CursorRow ? CursorColumn + 1 : Columns;
            if (mode == 0 && row < CursorRow || mode == 1 && row > CursorRow) continue;
            for (var col = from; col < to; col++) lines[row][col] = EraseCell();
            if (from == 0) wrapped[row] = false;
        }
    }

    private void EraseLine(int mode)
    {
        if (mode is < 0 or > 2) return;
        var from = mode == 0 ? CursorColumn : 0;
        var to = mode == 1 ? CursorColumn + 1 : Columns;
        for (var col = from; col < to; col++) lines[CursorRow][col] = EraseCell();
    }

    private void InsertCells(int count)
    {
        var line = lines[CursorRow];
        for (var col = Columns - 1; col >= CursorColumn + count; col--) line[col] = line[col - count];
        for (var col = CursorColumn; col < Math.Min(Columns, CursorColumn + count); col++) line[col] = EraseCell();
    }

    private void DeleteCells(int count)
    {
        var line = lines[CursorRow];
        for (var col = CursorColumn; col < Columns; col++) line[col] = col + count < Columns ? line[col + count] : EraseCell();
    }

    private void LineFeed(bool continuation = false)
    {
        wrapPending = false;
        if (CursorRow == scrollBottom) ScrollUp();
        else CursorRow = Math.Min(Rows - 1, CursorRow + 1);
        wrapped[CursorRow] = continuation;
    }

    private void ReverseLineFeed()
    {
        if (CursorRow == scrollTop) ScrollDown();
        else CursorRow = Math.Max(0, CursorRow - 1);
    }

    private void ScrollUp(int? from = null)
    {
        var top = from ?? scrollTop;
        if (top == 0 && scrollBottom == Rows - 1 && !IsAlternateScreen)
        {
            scrollback.Add(lines[0]); scrollbackWrapped.Add(wrapped[0]);
            if (scrollback.Count > ScrollbackCapacity)
            { scrollback.RemoveAt(0); scrollbackWrapped.RemoveAt(0); }
        }
        for (var row = top; row < scrollBottom; row++)
        { lines[row] = lines[row + 1]; wrapped[row] = wrapped[row + 1]; }
        lines[scrollBottom] = Enumerable.Repeat(EraseCell(), Columns).ToArray();
        wrapped[scrollBottom] = false;
    }

    private void ScrollDown(int? from = null)
    {
        var top = from ?? scrollTop;
        for (var row = scrollBottom; row > top; row--)
        { lines[row] = lines[row - 1]; wrapped[row] = wrapped[row - 1]; }
        lines[top] = Enumerable.Repeat(EraseCell(), Columns).ToArray();
        wrapped[top] = false;
    }

    private void SwitchAlternate(bool enabled)
    {
        if (enabled && savedNormalLines is null)
        {
            savedNormalLines = lines;
            savedNormalWrapped = wrapped;
            savedNormalColumn = CursorColumn;
            savedNormalRow = CursorRow;
            lines = NewLines(Columns, Rows);
            wrapped = new bool[Rows];
            CursorColumn = CursorRow = 0;
        }
        else if (!enabled && savedNormalLines is not null)
        {
            lines = savedNormalLines;
            wrapped = savedNormalWrapped!;
            savedNormalLines = null;
            savedNormalWrapped = null;
            CursorColumn = savedNormalColumn;
            CursorRow = savedNormalRow;
        }
    }

    private static TerminalCell[][] NewLines(int columns, int rows) =>
        Enumerable.Range(0, rows).Select(_ => new TerminalCell[columns]).ToArray();

    private static string TrimLine(TerminalCell[] line) =>
        string.Concat(line.Select(cell => cell.Text ?? " ")).TrimEnd();

    private static int ContentLength(TerminalCell[] line)
    {
        var length = line.Length;
        while (length > 0 && line[length - 1].Text is null) length--;
        return length;
    }

    private TerminalCell EraseCell() => new(null, null, background); // Terminal.swift:6723-6726
}

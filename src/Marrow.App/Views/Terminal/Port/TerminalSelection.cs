using System.Text;

namespace Marrow.App.Views.Terminal.Port;

/// <summary>SwiftTerm Position.swift: a cell of the buffer, the row counted from the top of the history.</summary>
internal readonly record struct TerminalPosition(int Column, int Row)
{
    /// <summary>Position.compare: by row, then by column.</summary>
    public static int Compare(TerminalPosition left, TerminalPosition right) =>
        left.Row != right.Row ? left.Row.CompareTo(right.Row) : left.Column.CompareTo(right.Column);
}

/// <summary>SelectionService.swift:200-203 <c>SelectionMode</c>.</summary>
internal enum TerminalSelectionMode
{
    Character,
    Word,
    Row,
}

/// <summary>
/// PORT-ONLY twin of SwiftTerm 1.20.0's <c>SelectionService</c> (Sources/SwiftTerm/SelectionService.swift, read from the copy under
/// E:\Droppy\wt\cx\terminal\_evidence\SwiftTerm-v1.20.0; ED Q128): the cell range a press, a drag, a double click, a triple click and a shift click
/// make. It works on the cell grid of a <see cref="TerminalFrame"/> (the retained history, then the screen); the surface turns the range into the
/// text its selectable text control shows and copies. A cell never written is the NUL character, a written blank is a space: the two groups the
/// word scan (:663-702) keeps apart.
/// </summary>
internal sealed class TerminalSelection
{
    private const string Nul = "\0";

    private (TerminalPosition Start, TerminalPosition End)? wordAnchor; // :183 wordSelectionAnchor
    private int? rowAnchor; // :187 rowSelectionAnchor

    /// <summary>:146-160 <c>active</c>.</summary>
    public bool Active { get; set; }

    /// <summary>:176-180 <c>start</c>.</summary>
    public TerminalPosition Start { get; private set; }

    /// <summary>:192-196 <c>end</c>, exclusive of its own cell like every range here.</summary>
    public TerminalPosition End { get; private set; }

    /// <summary>:208-216 <c>selectionMode</c>.</summary>
    public TerminalSelectionMode Mode { get; private set; }

    /// <summary>:270 <c>hasSelectionRange</c>: <c>start != end</c>.</summary>
    public bool HasRange => Start != End;

    /// <summary>:277-281 <c>setSoftStart(bufferPosition:)</c>: records where a press landed, so that a drag or a shift click knows where from.</summary>
    public void SetSoftStart(TerminalPosition position)
    {
        Start = position;
        End = position;
        Active = true;
    }

    /// <summary>:247-254 <c>startSelection()</c>: the range is from the last start position.</summary>
    public void StartSelection()
    {
        End = Start;
        Mode = TerminalSelectionMode.Character;
        wordAnchor = null;
        rowAnchor = null;
        Active = true;
    }

    /// <summary>:705-714 <c>selectNone()</c> (and :146-160 <c>active = false</c>).</summary>
    public void SelectNone()
    {
        if (!Active) return;
        Active = false;
        Mode = TerminalSelectionMode.Character;
        wordAnchor = null;
        rowAnchor = null;
    }

    /// <summary>:472-481 <c>select(row:)</c>: the whole row, which a later drag or shift click grows by rows.</summary>
    public void SelectRow(int row, TerminalFrame frame)
    {
        Start = new TerminalPosition(0, row);
        End = new TerminalPosition(frame.Columns - 1, row);
        Mode = TerminalSelectionMode.Row;
        wordAnchor = null;
        rowAnchor = row;
        Active = true;
    }

    /// <summary>:307-345 <c>shiftExtend(bufferPosition:)</c>: moves the end to the new position; when it lies before the start, the start moves to the old end.</summary>
    public void ShiftExtend(TerminalPosition newEnd, TerminalFrame frame)
    {
        if (Mode == TerminalSelectionMode.Row)
        {
            ExtendRows(newEnd.Row, frame);
            Active = true;
            return;
        }
        var adjusted = newEnd;
        if (Mode == TerminalSelectionMode.Word)
        {
            var direction = TerminalPosition.Compare(newEnd, Start) < 0 ? -1 : 1;
            adjusted = ExtendToWordBoundary(newEnd, frame, direction);
        }
        var swapStart = false;
        if (TerminalPosition.Compare(Start, End) < 0)
        {
            if (TerminalPosition.Compare(adjusted, Start) < 0) swapStart = true;
        }
        else if (TerminalPosition.Compare(Start, End) > 0)
        {
            if (TerminalPosition.Compare(adjusted, Start) > 0) swapStart = true;
        }
        if (swapStart) Start = End;
        End = adjusted;
        Active = true;
    }

    /// <summary>:408-444 <c>dragExtend(bufferPosition:)</c>: moves the end; after a double click the seed word stays whole, and both ends snap to word boundaries.</summary>
    public void DragExtend(TerminalPosition position, TerminalFrame frame)
    {
        if (Mode == TerminalSelectionMode.Row)
        {
            ExtendRows(position.Row, frame);
            Active = true;
            return;
        }
        if (Mode == TerminalSelectionMode.Word && wordAnchor is { } anchor)
        {
            if (TerminalPosition.Compare(position, anchor.Start) < 0)
            {
                Start = ExtendToWordBoundary(position, frame, -1);
                End = anchor.End;
            }
            else if (TerminalPosition.Compare(position, anchor.End) > 0)
            {
                Start = anchor.Start;
                End = ExtendToWordBoundary(position, frame, 1);
            }
            else
            {
                Start = anchor.Start;
                End = anchor.End;
            }
            Active = true;
            return;
        }
        var adjusted = position;
        if (Mode == TerminalSelectionMode.Word)
        {
            var direction = TerminalPosition.Compare(position, Start) < 0 ? -1 : 1;
            adjusted = ExtendToWordBoundary(position, frame, direction);
        }
        End = adjusted;
        Active = true;
    }

    // :483-490 extendRowSelection, setRowSelection.
    private void ExtendRows(int row, TerminalFrame frame)
    {
        var anchor = rowAnchor ?? Start.Row;
        Start = new TerminalPosition(0, Math.Min(anchor, row));
        End = new TerminalPosition(frame.Columns - 1, Math.Max(anchor, row));
    }

    /// <summary>
    /// :663-702 <c>selectWordOrExpression(at:in:)</c>: the double click. The position is held to the grid's columns and the buffer's top (:664-667). Blank (never written)
    /// cells and blanks select their run; a letter or a digit selects the run of letters, digits and <c>. _ -</c>; an opening bracket selects to its balanced
    /// closer, a closing one back to its opener; any other character selects nothing but is the seed of a later drag. <paramref name="viewTop"/> is the first
    /// row on screen (SwiftTerm's yDisp), where the forward bracket search stops (:539).
    /// </summary>
    public void SelectWordOrExpression(TerminalPosition unchecked_, TerminalFrame frame, int viewTop)
    {
        var position = new TerminalPosition(Math.Min(frame.Columns, Math.Max(unchecked_.Column, 0)), Math.Max(unchecked_.Row, 0));
        var character = CharacterAt(frame, position);
        if (character == Nul) SimpleScan(position, frame, other => other == Nul);
        else if (character == " ") SimpleScan(position, frame, other => other == " ");
        else if (IsLetterOrNumber(character)) SimpleScan(position, frame, other => IsLetterOrNumber(other) || other is "." or "_" or "-");
        else if (character is "{" or "(" or "[") BalancedSearchForward(position, frame, viewTop);
        else if (character is ")" or "]" or "}") BalancedSearchBackward(position, frame);
        else
        {
            Start = position;
            End = position;
        }
        Mode = TerminalSelectionMode.Word;
        wordAnchor = (Start, End);
        rowAnchor = null;
        Active = true;
    }

    // :492-499 character(at:in:): the cell's character; never written is NUL, outside the grid too.
    private static string CharacterAt(TerminalFrame frame, TerminalPosition position)
    {
        if (position.Row < 0 || position.Row >= frame.Lines.Length) return Nul;
        var line = frame.Lines[position.Row];
        if (position.Column < 0 || position.Column >= line.Length) return Nul;
        var cell = line[position.Column];
        return cell.Text is { Length: > 0 } text ? text : Nul;
    }

    // Swift Character.isLetter / isNumber on the cell's first scalar.
    private static bool IsLetterOrNumber(string character)
    {
        if (character.Length == 0) return false;
        var rune = Rune.GetRuneAt(character, 0);
        return Rune.IsLetter(rune) || Rune.IsNumber(rune);
    }

    // :501-533 simpleScanSelection(from:in:includeFunc:): the run of included cells around the position, on its row.
    private void SimpleScan(TerminalPosition position, TerminalFrame frame, Func<string, bool> include)
    {
        var scan = position.Column;
        var left = scan;
        while (scan >= 0)
        {
            if (!include(CharacterAt(frame, new TerminalPosition(scan, position.Row)))) break;
            left = scan;
            scan--;
        }
        scan = position.Column;
        var right = scan;
        while (scan < frame.Columns)
        {
            if (!include(CharacterAt(frame, new TerminalPosition(scan, position.Row)))) break;
            scan++;
            right = scan;
        }
        Start = new TerminalPosition(left, position.Row);
        End = new TerminalPosition(right, position.Row);
    }

    // :535-575 balancedSearchForward(from:in:).
    private void BalancedSearchForward(TerminalPosition position, TerminalFrame frame, int viewTop)
    {
        var startColumn = position.Column;
        var wait = new Stack<string>();
        Start = position;
        var maxRow = frame.Rows + viewTop;
        if (position.Row >= maxRow) return;
        for (var row = position.Row; row < maxRow; row++)
        {
            for (var column = startColumn; column < frame.Columns; column++)
            {
                var at = new TerminalPosition(column, row);
                var character = CharacterAt(frame, at);
                if (character == "(") wait.Push(")");
                else if (character == "[") wait.Push("]");
                else if (character == "{") wait.Push("}");
                else if (wait.Count > 0 && wait.Peek() == character)
                {
                    wait.Pop();
                    if (wait.Count == 0)
                    {
                        End = new TerminalPosition(at.Column + 1, at.Row);
                        return;
                    }
                }
            }
            startColumn = 0;
        }
        Start = position;
        End = position;
    }

    // :577-615 balancedSearchBackward(from:in:).
    private void BalancedSearchBackward(TerminalPosition position, TerminalFrame frame)
    {
        var startColumn = position.Column;
        var wait = new Stack<string>();
        End = position;
        for (var row = position.Row; row >= 0; row--)
        {
            for (var column = startColumn; column >= 0; column--)
            {
                var at = new TerminalPosition(column, row);
                var character = CharacterAt(frame, at);
                if (character == ")") wait.Push("(");
                else if (character == "]") wait.Push("[");
                else if (character == "}") wait.Push("{");
                else if (wait.Count > 0 && wait.Peek() == character)
                {
                    wait.Pop();
                    if (wait.Count == 0)
                    {
                        End = new TerminalPosition(End.Column + 1, End.Row);
                        Start = at;
                        return;
                    }
                }
            }
            startColumn = frame.Columns - 1;
        }
        Start = position;
        End = position;
    }

    // :617-661 extendToWordBoundary(position:in:direction:): the run's edge in the direction; a position on any other character stays.
    private static TerminalPosition ExtendToWordBoundary(TerminalPosition position, TerminalFrame frame, int direction)
    {
        var character = CharacterAt(frame, position);
        Func<string, bool> include;
        if (character == Nul) include = other => other == Nul;
        else if (character == " ") include = other => other == " ";
        else if (IsLetterOrNumber(character)) include = other => IsLetterOrNumber(other) || other is "." or "_" or "-";
        else return position;
        var result = position;
        if (direction < 0)
        {
            for (var column = position.Column; column >= 0; column--)
            {
                if (!include(CharacterAt(frame, new TerminalPosition(column, position.Row)))) break;
                result = result with { Column = column };
            }
        }
        else
        {
            for (var column = position.Column; column < frame.Columns;)
            {
                if (!include(CharacterAt(frame, new TerminalPosition(column, position.Row)))) break;
                column++;
                result = result with { Column = column };
            }
        }
        return result;
    }
}

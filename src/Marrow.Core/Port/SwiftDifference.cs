using System.Diagnostics;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY: swift-6.4.0-RELEASE stdlib <c>stdlib/public/core/Diffing.swift</c>, transliterated line for line
/// (<c>_linearSpaceMyers</c>, <c>EditGraphRect</c>, <c>DoubleKVector</c>, <c>LinearMyers</c>). Source saved at
/// <c>E:/Droppy/work/b3/evidence_6_1/Diffing-swift-6.4.0-RELEASE.swift</c> (provenance in that folder's
/// <c>SOURCES.tsv</c> and <c>SHA256SUMS.txt</c>; 6.2-RELEASE and 6.3-RELEASE copies sit beside it). Citations
/// below read "// Diffing.swift (swift-6.4.0-RELEASE) :a-b".
///
/// ENGINEERING DECISION ED-199 ("E3 CollectionDifference tie-break / SwiftDifference port", decisions_draft.md
/// E3, recorded on branch docs/b3, oracle O-67): <c>BidirectionalCollection.difference(from:by:)</c> is not
/// <c>@inlinable</c>, so an app calls the copy in the OS's <c>libswiftCore</c>; behaviour follows the Mac's OS
/// version, not the toolchain that built 1.7.0. macOS 26.0-26.3 run swift-6.2-RELEASE's greedy <c>_myers</c>;
/// macOS 26.4 and later, and macOS 27, run the swift-6.3-RELEASE/6.4.0-RELEASE linear-space, divide-and-conquer
/// <c>_linearSpaceMyers</c> ported here (6.3 and 6.4.0 differ by one <c>unsafe</c> keyword only). Both give edit
/// scripts of the same, minimal size; about a third of small tie cases choose different offsets. This file ports
/// the 6.3+/6.4 algorithm because it is what current macOS runs and is swift <c>main</c>. Tests whose expected
/// offsets depend on the tie carry <c>[Trait("Oracle", "O-67")]</c>; version-independent fixtures carry none.
///
/// The element comparer is supplied by the caller: <c>Core/Support/Text.swift:105-112</c> (B3,
/// droppy-code-v1.7.0) calls <c>newLines.difference(from: oldLines)</c>, whose default <c>by:</c> is String's
/// <c>==</c> (canonical equivalence); the C# caller (<c>Core/Support/Text.cs</c>, lane A1) is expected to pass
/// <see cref="SwiftString.Equal(string, string)"/>.
/// </summary>
public static class SwiftDifference
{
    /// <summary>
    /// One <c>CollectionDifference&lt;T&gt;.Change.remove(offset:element:associatedWith:)</c>.
    /// <c>associatedWith</c> is always nil here: the caller never calls <c>inferringMoves()</c>.
    /// </summary>
    public readonly record struct Removal<T>(int Offset, T Element);

    /// <summary>One <c>CollectionDifference&lt;T&gt;.Change.insert(offset:element:associatedWith:)</c>.</summary>
    public readonly record struct Insertion<T>(int Offset, T Element);

    /// <summary>
    /// <c>CollectionDifference&lt;T&gt;</c>, split into its two change kinds: <see cref="Removals"/> sorted by
    /// offset ascending, <see cref="Insertions"/> sorted by offset ascending, matching the ordering
    /// <c>CollectionDifference</c> itself guarantees for its <c>removals</c> and <c>insertions</c> collections.
    /// </summary>
    public readonly record struct Result<T>(IReadOnlyList<Removal<T>> Removals, IReadOnlyList<Insertion<T>> Insertions);

    /// <summary>
    /// <c>to.difference(from: from, by: areEquivalent)</c>: <c>BidirectionalCollection.difference(from:by:)</c>
    /// (Diffing.swift (swift-6.4.0-RELEASE) :146-152), which is exactly
    /// <c>_linearSpaceMyers(from: from, to: to, using: areEquivalent)</c> (:151).
    /// </summary>
    public static Result<T> Difference<T>(IReadOnlyList<T> from, IReadOnlyList<T> to, Func<T, T, bool> areEquivalent)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(areEquivalent);

        var changes = LinearSpaceMyers(from, to, areEquivalent);
        var removals = new List<Removal<T>>();
        var insertions = new List<Insertion<T>>();
        foreach (var change in changes)
        {
            if (change.IsInsertion) insertions.Add(new Insertion<T>(change.Offset, change.Element));
            else removals.Add(new Removal<T>(change.Offset, change.Element));
        }
        removals.Sort((x, y) => x.Offset.CompareTo(y.Offset));
        insertions.Sort((x, y) => x.Offset.CompareTo(y.Offset));
        return new Result<T>(removals, insertions);
    }

    /// <summary>Diffing.swift (swift-6.4.0-RELEASE) :590-630, the top-level <c>_linearSpaceMyers</c>.</summary>
    private static List<Change<T>> LinearSpaceMyers<T>(IReadOnlyList<T> old, IReadOnlyList<T> @new, Func<T, T, bool> cmp)
    {
        // :600-616: "splatting the collections into contiguous storage" for O(1) Int-indexed access. A .NET
        // array already is that storage, so materializing one (a no-op when the caller already passed a T[])
        // stands in for Swift's _withContiguousStorage/withUnsafeBufferPointer dance.
        var a = AsArray(old);
        var b = AsArray(@new);
        // :620-621
        var box = new EditGraphRect(0, 0, a.Length, b.Length).Shrunk(a, b, cmp);
        // :623-625: init?(initialSize:) returns nil only when size < 0; Shrunk keeps left <= right and
        // top <= bottom, so box.Size is never negative and this branch is defensive, transliterated as is.
        var myers = LinearMyers.Create(box.Size);
        if (myers is null) return [];
        // :626
        return myers.FindDifferences(box, a, b, cmp);
    }

    private static T[] AsArray<T>(IReadOnlyList<T> values) => values as T[] ?? [.. values];

    /// <summary>One <c>CollectionDifference&lt;T&gt;.Change</c> case, without the unused <c>associatedWith</c>.</summary>
    private readonly struct Change<T>
    {
        public bool IsInsertion { get; }
        public int Offset { get; }
        public T Element { get; }

        private Change(bool isInsertion, int offset, T element)
        {
            IsInsertion = isInsertion;
            Offset = offset;
            Element = element;
        }

        public static Change<T> Remove(int offset, T element) => new(false, offset, element);

        public static Change<T> Insert(int offset, T element) => new(true, offset, element);
    }

    /// <summary>
    /// Diffing.swift (swift-6.4.0-RELEASE) :273-343. A rectangle of the Myers edit graph: rows are <c>old</c>
    /// offsets (<see cref="Left"/>..<see cref="Right"/>), columns are <c>new</c> offsets
    /// (<see cref="Top"/>..<see cref="Bottom"/>).
    /// </summary>
    private readonly record struct EditGraphRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left; // :280-282
        public int Height => Bottom - Top; // :284-286
        public int Size => Width + Height; // :288-290
        public int Delta => Width - Height; // :292-294
        public bool IsEven => Delta % 2 == 0; // :296-298, Delta.isMultiple(of: 2)
        public bool IsOdd => Delta % 2 != 0; // :300-302
        public int Max => (Size + 1) / 2; // :304-306

        /// <summary>:308-312: shrinks so the bottom right corner becomes <paramref name="limit"/>'s top left.</summary>
        public EditGraphRect CroppedToTopLeftOf(EditGraphRect limit) => new(Left, Top, limit.Left, limit.Top);

        /// <summary>:314-318: shrinks so the top left corner becomes <paramref name="limit"/>'s bottom right.</summary>
        public EditGraphRect CroppedToBottomRightOf(EditGraphRect limit) => new(limit.Right, limit.Bottom, Right, Bottom);

        /// <summary>:320-343: trims a common prefix, then a common suffix, by following diagonals.</summary>
        public EditGraphRect Shrunk<T>(T[] old, T[] @new, Func<T, T, bool> cmp)
        {
            var r = this;
            while (r.Left < r.Right && r.Top < r.Bottom && cmp(old[r.Left], @new[r.Top])) // :329-334
                r = r with { Left = r.Left + 1, Top = r.Top + 1 };
            while (r.Right > r.Left && r.Bottom > r.Top && cmp(old[r.Right - 1], @new[r.Bottom - 1])) // :335-340
                r = r with { Right = r.Right - 1, Bottom = r.Bottom - 1 };
            return r;
        }
    }

    /// <summary>
    /// Diffing.swift (swift-6.4.0-RELEASE) :182-271: one allocation storing both "k-vectors" of the Myers
    /// search, indexed by a signed offset from the middle of each half.
    /// </summary>
    private sealed class DoubleKVector
    {
        private readonly int[] _buffer;
        private readonly int _forwardOffset;
        private readonly int _backwardOffset;

        /// <summary>:219-228.</summary>
        public DoubleKVector(int size)
        {
            var range = size * 2 + 1;
            _buffer = new int[range * 2];
            _forwardOffset = size;
            _backwardOffset = range + size;
        }

        // :234-236, :242-255
        public int Forward(int index) => _buffer[_forwardOffset + index];
        public void SetForward(int index, int value) => _buffer[_forwardOffset + index] = value;

        // :238-240, :257-270
        public int Backward(int index) => _buffer[_backwardOffset + index];
        public void SetBackward(int index, int value) => _buffer[_backwardOffset + index] = value;
    }

    /// <summary>Diffing.swift (swift-6.4.0-RELEASE) :345-579: the iterative divide-and-conquer search.</summary>
    private sealed class LinearMyers
    {
        private readonly DoubleKVector _kVector;

        private LinearMyers(int size) => _kVector = new DoubleKVector(size);

        /// <summary>:581-588.</summary>
        public static LinearMyers? Create(int size) => size >= 0 ? new LinearMyers(size) : null;

        /// <summary>:352-393.</summary>
        public List<Change<T>> FindDifferences<T>(EditGraphRect initial, T[] old, T[] @new, Func<T, T, bool> cmp)
        {
            var result = new List<Change<T>>();
            var stack = new List<EditGraphRect>(); // :359, a Swift array used as a stack via append/popLast
            var current = initial;

            while (true)
            {
                var (edit, snakeBox) = MiddleSnake(current, old, @new, cmp); // :363-364

                if (edit is { } e) result.Add(e); // :366-369

                if (snakeBox is not { } sb) // :371
                {
                    if (stack.Count == 0) return result; // :374-377
                    current = stack[^1];
                    stack.RemoveAt(stack.Count - 1); // :378, popLast
                    continue;
                }

                var headBox = current.CroppedToTopLeftOf(sb).Shrunk(old, @new, cmp); // :382-383
                var tailBox = current.CroppedToBottomRightOf(sb).Shrunk(old, @new, cmp); // :384-385
                current = headBox; // :388
                stack.Add(tailBox); // :390
            }
        }

        /// <summary>:396-434.</summary>
        private (Change<T>? Edit, EditGraphRect? SnakeBox) MiddleSnake<T>(
            EditGraphRect box, T[] old, T[] @new, Func<T, T, bool> cmp)
        {
            if (box.Size <= 1) // :401
            {
                if (box.Size == 0) return (null, null); // :403-404
                if (box.Width == 1) return (Change<T>.Remove(box.Left, old[box.Left]), null); // :405-406
                return (Change<T>.Insert(box.Top, @new[box.Top]), null); // :407-408
            }

            _kVector.SetForward(1, box.Left); // :413
            _kVector.SetBackward(1, box.Bottom); // :414

            for (var depth = 0; depth <= box.Max; depth++) // :416
            {
                if (box.IsOdd) // :417
                {
                    var result = ForwardSearch(box, depth, old, @new, cmp); // :418-421
                    if (result is { } r) return (r.Edit, r.SnakeBox);
                    _ = BackwardSearch(box, depth, old, @new, cmp); // :423
                }
                else
                {
                    ForwardSearch(box, depth, old, @new, cmp); // :425
                    var result = BackwardSearch(box, depth, old, @new, cmp); // :426-429
                    if (result is { } r) return (r.Edit, r.SnakeBox);
                }
            }
            throw new UnreachableException(); // :433
        }

        /// <summary>:437-506.</summary>
        private (Change<T> Edit, EditGraphRect SnakeBox)? ForwardSearch<T>(
            EditGraphRect box, int depth, T[] old, T[] @new, Func<T, T, bool> cmp)
        {
            for (var k = depth; k >= -depth; k -= 2) // :444
            {
                int newRight, newBottom, newLeft, newTop;
                bool isInsertion;
                int editIndex;

                // :456-464
                if (k == -depth || (k != depth && _kVector.Forward(k - 1) < _kVector.Forward(k + 1)))
                {
                    newRight = _kVector.Forward(k + 1);
                    newLeft = newRight;
                    newBottom = box.Top + (newRight - box.Left) - k;
                    isInsertion = true;
                    editIndex = newBottom - 1;
                }
                else // :465-472
                {
                    newLeft = _kVector.Forward(k - 1);
                    newRight = newLeft + 1;
                    newBottom = box.Top + (newRight - box.Left) - k;
                    isInsertion = false;
                    editIndex = newLeft;
                }
                newTop = depth == 0 || newRight != newLeft ? newBottom : newBottom - 1; // :473-477

                while (newRight < box.Right && newBottom < box.Bottom && cmp(old[newRight], @new[newBottom])) // :480-485
                {
                    newRight++;
                    newBottom++;
                }
                _kVector.SetForward(k, newRight); // :486

                if (!box.IsOdd) continue; // :489

                var c = k - box.Delta; // :493
                if (c >= -(depth - 1) && c <= depth - 1 && newBottom >= _kVector.Backward(c)) // :494
                {
                    var edit = isInsertion
                        ? Change<T>.Insert(editIndex, @new[editIndex]) // :495-496
                        : Change<T>.Remove(editIndex, old[editIndex]); // :497-498
                    return (edit, new EditGraphRect(newLeft, newTop, newRight, newBottom)); // :500-502
                }
            }
            return null; // :505
        }

        /// <summary>:508-578.</summary>
        private (Change<T> Edit, EditGraphRect SnakeBox)? BackwardSearch<T>(
            EditGraphRect box, int depth, T[] old, T[] @new, Func<T, T, bool> cmp)
        {
            for (var c = depth; c >= -depth; c -= 2) // :516
            {
                var k = c + box.Delta; // :517
                int newLeft, newTop, newRight, newBottom;
                bool isInsertion;
                int editIndex;

                // :529-537
                if (c == -depth || (c != depth && _kVector.Backward(c - 1) > _kVector.Backward(c + 1)))
                {
                    newTop = _kVector.Backward(c + 1);
                    newBottom = newTop;
                    newLeft = box.Left + (newTop - box.Top) + k;
                    isInsertion = false;
                    editIndex = newLeft;
                }
                else // :538-545
                {
                    newBottom = _kVector.Backward(c - 1);
                    newTop = newBottom - 1;
                    newLeft = box.Left + (newTop - box.Top) + k;
                    isInsertion = true;
                    editIndex = newTop;
                }
                newRight = depth == 0 || newTop != newBottom ? newLeft : newLeft + 1; // :546-550

                while (newLeft > box.Left && newTop > box.Top && cmp(old[newLeft - 1], @new[newTop - 1])) // :553-558
                {
                    newLeft--;
                    newTop--;
                }
                _kVector.SetBackward(c, newTop); // :559

                if (!box.IsEven) continue; // :562

                if (k >= -depth && k <= depth && newLeft <= _kVector.Forward(k)) // :566
                {
                    var edit = isInsertion
                        ? Change<T>.Insert(editIndex, @new[editIndex]) // :567-568
                        : Change<T>.Remove(editIndex, old[editIndex]); // :569-570
                    return (edit, new EditGraphRect(newLeft, newTop, newRight, newBottom)); // :572-574
                }
            }
            return null; // :577
        }
    }
}

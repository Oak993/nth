using System.Text;
using Marrow.App.Views.Terminal.Port;
using Marrow.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Views.Terminal;

// ED-NEW-lean-10 budgets (Debug build, headless; bytes from GC.GetAllocatedBytesForCurrentThread on the
// AllocationProbe thread, so only the buffer's own synchronous work counts). TerminalHostView.ApplyOutput takes one
// TerminalScreenBuffer.Snapshot per output frame; a frame here is one output read that rewrites one line, then that
// snapshot. The equivalence guard replays random escape-sequence scripts against a frozen copy of the buffer from
// before copy-on-write (LegacyTerminalScreenBuffer, whose Snapshot is a deep copy) and checks that no snapshot handed
// out earlier changes afterwards.
public sealed class TerminalSnapshotBudgetTests(ITestOutputHelper output)
{
    private const int Columns = 200;
    private const int ScreenRows = 50;
    private const int Frames = 100;

    // Measured on the unchanged code (before) and after the fix; see ED-NEW-lean-10.
    private const double FrameBytesLimit = 5_310_000;    // one output read that rewrites one line, plus the frame snapshot
    private const double IdleSnapshotBytesLimit = 5_310_000; // a snapshot with nothing changed since the last one

    [Fact]
    [Trait("Decision", "ED-NEW-lean-10")]
    public void A_frame_snapshot_with_full_scrollback_and_one_changed_line_copies_only_that_line()
    {
        var screen = FullScreen();
        var changes = Enumerable.Range(0, Frames + 40).Select(index => $"\x1b[10;1Hframe {index:D6}").ToArray();
        var next = 0;
        void Frame()
        {
            screen.Feed(changes[next++]);
            GC.KeepAlive(screen.Snapshot());
        }
        var frameBytes = AllocationProbe.Measure(
            () => { for (var i = 0; i < 40; i++) Frame(); },
            () => { for (var i = 0; i < Frames; i++) Frame(); }) / (double)Frames;
        var idleBytes = AllocationProbe.Measure(
            () => { for (var i = 0; i < 40; i++) GC.KeepAlive(screen.Snapshot()); },
            () => { for (var i = 0; i < Frames; i++) GC.KeepAlive(screen.Snapshot()); }) / (double)Frames;
        output.WriteLine($"{Columns} columns, {ScreenRows} rows, 500 scrollback lines: {frameBytes:F0} B per frame with one changed line, {idleBytes:F0} B per idle snapshot");
        var frame = screen.Snapshot();
        Assert.Equal(500, frame.HistoryCount);
        Assert.StartsWith("frame 000139", string.Concat(frame.Lines[frame.HistoryCount + 9].Select(cell => cell.Text ?? " ")), StringComparison.Ordinal);
        Assert.True(frameBytes <= FrameBytesLimit, $"a frame costs {frameBytes:F0} B");
        Assert.True(idleBytes <= IdleSnapshotBytesLimit, $"an idle snapshot costs {idleBytes:F0} B");
    }

    [Theory]
    [InlineData(1, 12, 4)]
    [InlineData(2, 20, 6)]
    [InlineData(3, 7, 3)]
    [InlineData(4, 31, 9)]
    [Trait("Decision", "ED-NEW-lean-10")]
    public void Random_escape_sequence_scripts_snapshot_exactly_like_the_deep_copying_buffer(int seed, int columns, int rows)
    {
        var random = new Random(seed);
        var screen = new TerminalScreenBuffer(columns, rows);
        var legacy = new LegacyTerminalScreenBuffer(columns, rows);
        var kept = new Queue<(TerminalFrame Frame, TerminalFrame Copy, int Step)>();
        var maxHistory = 0;
        for (var step = 0; step < 4000; step++)
        {
            if (random.Next(60) == 0)
            {
                var (width, height) = (Math.Max(1, columns + random.Next(-5, 6)), Math.Max(1, rows + random.Next(-2, 3)));
                screen.Resize(width, height);
                legacy.Resize(width, height);
            }
            else
            {
                var chunk = Chunk(random);
                screen.Feed(chunk);
                legacy.Feed(chunk);
            }
            if (random.Next(10) < 7)
            {
                var frame = screen.Snapshot();
                var reference = legacy.Snapshot();
                AssertSame(reference, frame, $"seed {seed}, step {step}");
                maxHistory = Math.Max(maxHistory, frame.HistoryCount);
                kept.Enqueue((frame, DeepCopy(frame), step));
                if (kept.Count > 24) kept.Dequeue();
            }
            Assert.Equal(legacy.CursorColumn, screen.CursorColumn);
            Assert.Equal(legacy.CursorRow, screen.CursorRow);
            Assert.Equal(legacy.IsAlternateScreen, screen.IsAlternateScreen);
            if (step % 25 == 0)
            {
                foreach (var (frame, copy, taken) in kept) AssertSame(copy, frame, $"seed {seed}, snapshot of step {taken} read at step {step}");
                Assert.Equal(legacy.AllText(), screen.AllText());
            }
        }
        foreach (var (frame, copy, taken) in kept) AssertSame(copy, frame, $"seed {seed}, snapshot of step {taken} read at the end");
        Assert.Equal(legacy.AllText(), screen.AllText());
        Assert.Equal(legacy.Title, screen.Title);
        Assert.True(maxHistory == 500, $"seed {seed} reached {maxHistory} scrollback lines; the script must fill the 500-line scrollback");
    }

    private static TerminalScreenBuffer FullScreen()
    {
        var screen = new TerminalScreenBuffer(Columns, ScreenRows);
        var filler = new string('x', 150);
        var builder = new StringBuilder();
        for (var line = 0; line < 600; line++) builder.Append("\x1b[3").Append(line % 8).Append("mline ").Append(line).Append(' ').Append(filler).Append("\x1b[0m\r\n");
        screen.Feed(builder.ToString());
        return screen;
    }

    private static readonly string[] Pieces =
    [
        "abc", "hello world", "\u4e16\u754c", "e\u0301", "\U0001F468\u200d\U0001F469", "\U0001F1EB\U0001F1F7", "\t", "\b", "\r", "\n", "\r\n",
        "\x1b[A", "\x1b[2B", "\x1b[3C", "\x1b[D", "\x1b[E", "\x1b[F", "\x1b[5G", "\x1b[2;3H", "\x1b[H", "\x1b[0J", "\x1b[1J", "\x1b[2J",
        "\x1b[K", "\x1b[1K", "\x1b[2K", "\x1b[31m", "\x1b[1;4;7m", "\x1b[0m", "\x1b[38;5;196m", "\x1b[48;2;10;20;30m", "\x1b[44m", "\x1b[49m",
        "\x1b[2;4r", "\x1b[r", "\x1b[S", "\x1b[2T", "\x1b[2@", "\x1b[P", "\x1b[3X", "\x1b[L", "\x1b[2M", "\x1b[s", "\x1b[u", "\x1b7", "\x1b8",
        "\x1bD", "\x1bE", "\x1bM", "\x1b[?1049h", "\x1b[?1049l", "\x1b[?47h", "\x1b[?47l", "\x1b[?25l", "\x1b[?25h", "\x1b]0;title\a",
        "\x1b]2;other\x1b\\", "\x1b[", "1;2", "m", "\x1b",
    ];

    private static string Chunk(Random random)
    {
        var builder = new StringBuilder();
        if (random.Next(400) == 0) return "\x1b[3J"; // clears the scrollback, so it stays rare
        if (random.Next(4) == 0)
        {
            // Bursts of plain lines fill the 500-line scrollback.
            var count = random.Next(1, 30);
            for (var i = 0; i < count; i++) builder.Append("line ").Append(random.Next(1000)).Append(new string('z', random.Next(0, 40))).Append("\r\n");
            return builder.ToString();
        }
        var pieces = random.Next(1, 8);
        for (var i = 0; i < pieces; i++) builder.Append(Pieces[random.Next(Pieces.Length)]);
        return builder.ToString();
    }

    private static TerminalFrame DeepCopy(TerminalFrame frame) =>
        frame with { Lines = frame.Lines.Select(line => line.ToArray()).ToArray(), Wrapped = frame.Wrapped.ToArray() };

    private static void AssertSame(TerminalFrame expected, TerminalFrame actual, string where)
    {
        Assert.True(expected.Columns == actual.Columns && expected.Rows == actual.Rows, $"{where}: size");
        Assert.True(expected.HistoryCount == actual.HistoryCount, $"{where}: history {expected.HistoryCount} vs {actual.HistoryCount}");
        Assert.True(expected.CursorColumn == actual.CursorColumn && expected.CursorRow == actual.CursorRow
            && expected.CursorVisible == actual.CursorVisible, $"{where}: cursor");
        Assert.True(expected.Wrapped.SequenceEqual(actual.Wrapped), $"{where}: wrapped flags");
        Assert.True(expected.Lines.Length == actual.Lines.Length, $"{where}: line count");
        for (var row = 0; row < expected.Lines.Length; row++)
            Assert.True(expected.Lines[row].AsSpan().SequenceEqual(actual.Lines[row]), $"{where}: line {row}");
    }
}

using System.Text;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// ED-NEW-lean-4 (lean pass, unit L4): the Pi and Command Code text tracker (SessionSupport.cs, StreamedTextTracker)
// appends each streamed delta without copying the text already streamed, so the bytes one delta costs do not grow
// with the reply. Measured with AllocationProbe on its own thread (R-TEST-5): the warm-up streams the reply up to
// the accumulated length, then 400 deltas of 40 characters are measured. Numbers are from the Debug build.
// Before the fix (string +=): message 36,064 B per delta at 10k and 216,070 B at 100k, thinking 36,064 and 216,064.
// After (StringBuilder): 80 B per delta at both lengths for both. ED-NEW-lean-4.
[Trait("Decision", "ED-NEW-lean-4")]
public sealed class StreamedTextTrackerBudgetTests
{
    private const int DeltaLength = 40;
    private const int MeasuredDeltas = 400;

    // 1.3x the 80 B per delta measured after the fix at both lengths (StringBuilder chunks; Debug build). The old
    // string += cost 36,064 B at 10k and about 216,070 B at 100k per delta.
    private const double LimitBytesPerDelta = 104;

    private static double BytesPerDelta(int accumulated, Action<StreamedTextTracker, string> append)
    {
        StreamedTextTracker? tracker = null;
        string[] deltas = [];
        var bytes = AllocationProbe.Measure(
            () =>
            {
                tracker = new StreamedTextTracker(n => $"message-{n}", n => $"thinking-{n}");
                var warm = new string('w', DeltaLength);
                for (var length = 0; length < accumulated; length += DeltaLength) append(tracker, warm);
                deltas = [.. Enumerable.Range(0, MeasuredDeltas).Select(i => new string((char)('a' + i % 26), DeltaLength))];
            },
            () =>
            {
                foreach (var delta in deltas) append(tracker!, delta);
            });
        GC.KeepAlive(tracker);
        return (double)bytes / MeasuredDeltas;
    }

    [Fact]
    public void A_message_delta_costs_the_same_at_10k_and_100k_streamed_characters()
    {
        var at10k = BytesPerDelta(10_000, (tracker, delta) => tracker.AppendMessage(delta));
        var at100k = BytesPerDelta(100_000, (tracker, delta) => tracker.AppendMessage(delta));

        Assert.True(at100k <= 2 * at10k, $"message delta: {at10k:F0} B at 10k, {at100k:F0} B at 100k (must stay within 2x)");
        Assert.True(at100k <= LimitBytesPerDelta, $"message delta: {at100k:F0} B at 100k, limit {LimitBytesPerDelta:F0} (10k: {at10k:F0})");
    }

    [Fact]
    public void A_thinking_delta_costs_the_same_at_10k_and_100k_streamed_characters()
    {
        var at10k = BytesPerDelta(10_000, (tracker, delta) => tracker.AppendThinking(delta));
        var at100k = BytesPerDelta(100_000, (tracker, delta) => tracker.AppendThinking(delta));

        Assert.True(at100k <= 2 * at10k, $"thinking delta: {at10k:F0} B at 10k, {at100k:F0} B at 100k (must stay within 2x)");
        Assert.True(at100k <= LimitBytesPerDelta, $"thinking delta: {at100k:F0} B at 100k, limit {LimitBytesPerDelta:F0} (10k: {at10k:F0})");
    }

    [Fact]
    public void The_text_read_at_any_point_is_the_plain_concatenation_of_the_deltas()
    {
        var random = new Random(4004);
        var tracker = new StreamedTextTracker(n => $"message-{n}", n => $"thinking-{n}");
        var message = new StringBuilder();
        var thinking = new StringBuilder();
        string[] pieces = ["a", "\r\n", "é", "\U0001F600", "́", "", " word", "\n"];
        for (var step = 0; step < 5_000; step++)
        {
            var piece = pieces[random.Next(pieces.Length)];
            switch (random.Next(12))
            {
                case 0:
                    tracker.StartMessage();
                    message.Clear();
                    break;
                case 1:
                    tracker.ResetThinking();
                    thinking.Clear();
                    break;
                case 2:
                    tracker.StartThinking();
                    thinking.Clear();
                    break;
                case 3:
                case 4:
                case 5:
                case 6:
                    tracker.AppendMessage(piece);
                    message.Append(piece);
                    break;
                default:
                    var resets = random.Next(2) == 0;
                    if (tracker.CurrentThinkingID is null && resets) thinking.Clear();
                    tracker.AppendThinking(piece, resets);
                    thinking.Append(piece);
                    break;
            }
            if (random.Next(3) == 0) tracker.EndThinking();
            if (random.Next(5) == 0)
            {
                Assert.Equal(message.ToString(), tracker.MessageText);
                Assert.Equal(thinking.ToString(), tracker.ThinkingText);
            }
        }
        Assert.Equal(message.ToString(), tracker.MessageText);
        Assert.Equal(thinking.ToString(), tracker.ThinkingText);
    }
}

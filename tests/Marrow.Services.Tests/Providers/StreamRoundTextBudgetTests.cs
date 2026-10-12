using System.Text;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// ED-NEW-lean-4 (lean pass, unit L4): PerformStream's reply text, reasoning text and tool-call argument buffers
// (OpenAICompatibleSession.StreamRoundText) append each streamed delta without copying what came before, and are
// flattened once at message end. Measured with AllocationProbe on its own thread (R-TEST-5): the warm-up streams
// up to the accumulated length, then 400 deltas of 40 characters are measured. Numbers are from the Debug build.
// Before the fix (string +=): about 36,064 B per delta at 10k and 216,070 B at 100k for text, reasoning and tool
// arguments alike. After (StringBuilder): 80 B per delta at both lengths. ED-NEW-lean-4.
[Trait("Decision", "ED-NEW-lean-4")]
public sealed class StreamRoundTextBudgetTests
{
    private const int DeltaLength = 40;
    private const int MeasuredDeltas = 400;

    // 1.3x the 80 B per delta measured after the fix at both lengths (StringBuilder chunks; Debug build). The old
    // string += cost 36,064 B at 10k and about 216,070 B at 100k per delta.
    private const double LimitBytesPerDelta = 104;

    private static double BytesPerDelta(int accumulated, Action<OpenAICompatibleSession.StreamRoundText, string> append)
    {
        OpenAICompatibleSession.StreamRoundText? text = null;
        string[] deltas = [];
        var bytes = AllocationProbe.Measure(
            () =>
            {
                text = new OpenAICompatibleSession.StreamRoundText();
                text.AppendToolCall(0, "call-0", "read_file", "");
                var warm = new string('w', DeltaLength);
                for (var length = 0; length < accumulated; length += DeltaLength) append(text, warm);
                deltas = [.. Enumerable.Range(0, MeasuredDeltas).Select(i => new string((char)('a' + i % 26), DeltaLength))];
            },
            () =>
            {
                foreach (var delta in deltas) append(text!, delta);
            });
        GC.KeepAlive(text);
        return (double)bytes / MeasuredDeltas;
    }

    private static void AssertFlat(string what, Action<OpenAICompatibleSession.StreamRoundText, string> append)
    {
        var at10k = BytesPerDelta(10_000, append);
        var at100k = BytesPerDelta(100_000, append);

        Assert.True(at100k <= 2 * at10k, $"{what}: {at10k:F0} B at 10k, {at100k:F0} B at 100k (must stay within 2x)");
        Assert.True(at100k <= LimitBytesPerDelta, $"{what}: {at100k:F0} B at 100k, limit {LimitBytesPerDelta:F0} (10k: {at10k:F0})");
    }

    [Fact]
    public void A_text_delta_costs_the_same_at_10k_and_100k_streamed_characters() =>
        AssertFlat("text delta", (text, delta) => text.AppendContent(delta));

    [Fact]
    public void A_reasoning_delta_costs_the_same_at_10k_and_100k_streamed_characters() =>
        AssertFlat("reasoning delta", (text, delta) => text.AppendReasoning(delta));

    [Fact]
    public void A_tool_argument_delta_costs_the_same_at_10k_and_100k_streamed_characters() =>
        AssertFlat("tool-argument delta", (text, delta) => text.AppendToolCall(0, "", "", delta));

    [Fact]
    public void The_flattened_text_and_calls_are_the_plain_concatenation_of_the_deltas()
    {
        var random = new Random(4040);
        var text = new OpenAICompatibleSession.StreamRoundText();
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        string[] pieces = ["{", "\"path\":", "\"a.txt\"", "}", "\r\n", "é", "\U0001F600", "́", ""];
        for (var step = 0; step < 5_000; step++)
        {
            var piece = pieces[random.Next(pieces.Length)];
            switch (random.Next(3))
            {
                case 0:
                    text.AppendContent(piece);
                    content.Append(piece);
                    break;
                case 1:
                    text.AppendReasoning(piece);
                    reasoning.Append(piece);
                    break;
                default:
                    var index = random.Next(-1, 6);
                    var id = random.Next(4) == 0 ? $"call-{random.Next(3)}" : "";
                    var name = random.Next(4) == 0 ? $"tool-{random.Next(3)}" : "";
                    text.AppendToolCall(index, id, name, piece);
                    (string Id, string Name, StringBuilder Arguments) acc = calls.TryGetValue(index, out var existing) ? existing : ("", "", new StringBuilder());
                    if (id.Length > 0) acc.Id = id;
                    if (name.Length > 0) acc.Name = name;
                    acc.Arguments.Append(piece);
                    calls[index] = acc;
                    break;
            }
            Assert.Equal(content.Length, text.ContentLength);
            Assert.Equal(reasoning.Length, text.ReasoningLength);
        }

        Assert.Equal(content.ToString(), text.Content());
        Assert.Equal(reasoning.ToString(), text.Reasoning());
        Assert.Equal(
            calls.Select(pair => (pair.Value.Id, pair.Value.Name, pair.Value.Arguments.ToString())).ToList(),
            text.ToolCalls().ToList());
    }
}

using System.Text;
using Marrow.Core.Models;
using Marrow.Testing;
using Xunit.Abstractions;

namespace Marrow.Core.Tests.Models;

// ED-NEW-lean-5 budget (Debug build, headless; AllocationProbe on a dedicated thread). A command's output arrives as
// many small appends: 1 MB in 4 KB chunks crosses the 60,000-byte ceiling again and again, so trimming is on for
// most of the run. Each append must still hand back one flat Output string (the record is immutable), so the floor
// is one copy of the new Output per append; the budget is what the trimming adds on top.
public sealed class ToolCallOutputBudgetTests(ITestOutputHelper output)
{
    // ED-NEW-lean-5: 48,723,112 B on the unchanged code (limit was 73,100,000), 20,270,040 B after the fix.
    private const long AppendMegabyteLimit = 26_360_000;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Appending_a_megabyte_of_tool_output_in_4_KB_chunks_stays_within_budget()
    {
        var chunk = Chunk();
        string? last = null;
        var bytes = AllocationProbe.Measure(
            warmup: () => Append(chunk),
            measured: () => last = Append(chunk));
        output.WriteLine($"1 MB in 4 KB chunks with trimming: {bytes:N0} B");
        Assert.StartsWith("…", last, StringComparison.Ordinal);
        Assert.True(bytes <= AppendMegabyteLimit, $"appending 1 MB of tool output in 4 KB chunks allocates {bytes:N0} B");
    }

    private static string Append(string chunk)
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run" };
        for (var i = 0; i < 256; i++) call = call.AppendOutput(chunk);
        return call.Output;
    }

    private static string Chunk()
    {
        var builder = new StringBuilder(4_096);
        var line = 0;
        while (builder.Length < 4_096) builder.Append("line ").Append(line++).Append(": build output — ok ✓\n");
        return builder.ToString(0, 4_096);
    }
}

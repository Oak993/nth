using System.Text;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit.Abstractions;

namespace Marrow.Core.Tests.Support;

// ED-NEW-lean-3: allocation budgets for the two text cleanups on their common, nothing-to-do input. StripANSI runs on every
// process-exit LastLines and its other callers, almost always over CLI text with no escape; WithoutEmDashes runs on every streamed
// delta, almost always without a long dash. Measured on a dedicated thread after warm-up (AllocationProbe), fixed seeds, no clock.
// Numbers are for the Debug build the test runs use; the ledger entry records the before and after values the limits come from.
[Trait("Decision", "ED-NEW-lean-3")]
public sealed class TextCleanupLeanBudgetTests(ITestOutputHelper output)
{
    private const int Megabyte = 1 << 20;
    private const int Deltas = 1_000;
    private const long StripAnsiBytesPerMegabyteLimit = 72_000_000; // Debug: 48,072,688 on the unchanged code
    private const long WithoutEmDashesBytesPerDeltaLimit = 144;      // Debug: 96 on the unchanged code

    [Fact]
    public void StripANSI_on_a_megabyte_of_escape_free_CLI_text_stays_within_its_allocation_budget()
    {
        string[] lines =
        [
            "   Compiling marrow-core v0.4.2 (E:\\Droppy\\port\\src\\Marrow.Core)\r\n",
            "  Restored E:/Droppy/port/tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj (in 412 ms).\n",
            "✓ 5,139 passed · 0 failed · 1 skipped — 41.2 s\n",
            "warning CS8618: Non-nullable property 'Name' must contain a non-null value when exiting constructor.\n",
            "npm WARN deprecated glob@7.2.3: Glob versions prior to v9 are no longer supported…\n",
            "error: could not compile `nth` (bin \"nth\") due to 2 previous errors; 1 warning emitted\r\n",
            "  café  naïve  日本語  🚀 deploy finished in 3m 12s\n",
        ];
        var random = new Random(5301);
        var builder = new StringBuilder(Megabyte + 256);
        while (builder.Length < Megabyte) builder.Append(lines[random.Next(lines.Length)]);
        var text = builder.ToString(0, Megabyte);
        if (char.IsHighSurrogate(text[^1])) text = text[..^1] + "x";
        Assert.DoesNotContain('\u001B', text);
        var warm = text[..4096];
        string? result = null;
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) result = TextCleanup.StripANSI(warm); },
            () => result = TextCleanup.StripANSI(text));
        Assert.Same(text, result);
        output.WriteLine($"StripANSI: {bytes} bytes for {text.Length} escape-free characters");
        Assert.True(bytes <= StripAnsiBytesPerMegabyteLimit, $"{bytes} bytes for StripANSI over {text.Length} escape-free characters");
    }

    [Fact]
    public void WithoutEmDashes_on_dash_free_streamed_deltas_stays_within_its_allocation_budget()
    {
        const string prose = "The build passed on the second try. ‘Quoted’ words, “curly quotes”, an ellipsis… and lines 10–20 stay as they are. " +
                             "Then it ran the tests: 5,139 passed, café and naïve and 日本語 and 🚀 too. ";
        var random = new Random(5302);
        var deltas = new string[Deltas];
        for (var i = 0; i < Deltas; i++)
        {
            var length = 1 + random.Next(120);
            var start = random.Next(prose.Length - length);
            if (char.IsLowSurrogate(prose[start])) start--;
            var end = start + length;
            if (char.IsHighSurrogate(prose[end - 1])) end++;
            deltas[i] = prose[start..end];
        }
        var same = 0;
        void Pass() { foreach (var delta in deltas) if (ReferenceEquals(delta, TextCleanup.WithoutEmDashes(delta))) same++; }
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) Pass(); }, Pass);
        Assert.Equal(Deltas * 21, same);
        var perDelta = (bytes + Deltas - 1) / Deltas; // rounded up: one stray byte is over a limit of 0
        output.WriteLine($"WithoutEmDashes: {perDelta} bytes per delta ({bytes} for {Deltas} deltas)");
        Assert.True(perDelta <= WithoutEmDashesBytesPerDeltaLimit, $"{perDelta} bytes per WithoutEmDashes delta ({bytes} for {Deltas} deltas)");
    }
}

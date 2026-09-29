using System.Text;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Audit fix (svc): Core/Support/StdioProcess.swift:106-112 `write(_ data: Data)` queues a closure over a Data value, so a caller
// that mutates its buffer after the call cannot change what the child receives (SOURCE FACT: Data has value semantics).
public sealed class StdioProcessWriteSnapshotTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Trait("Req", "PR-TERM-110")] // Core/Support/StdioProcess.swift:106-112 (B3)
    [Fact]
    public async Task A_queued_raw_write_sends_the_bytes_as_they_were_when_it_was_called()
    {
        using var runtime = new TestRuntime();
        var child = new StdioProcess(SystemTools.PowerShell,
            SystemTools.PowerShellScript("Start-Sleep -Milliseconds 1500; $i=[Console]::In; $o=[Console]::Out; while(($l=$i.ReadLine()) -ne $null){ $o.WriteLine($l); $o.Flush() }"),
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase),
            StdioFramer.Framing.Lines);
        child.Start();
        // The first write is larger than a pipe buffer and the child is not reading yet, so it holds the serial queue.
        child.Write(Encoding.UTF8.GetBytes("\"" + new string('x', 400_000) + "\"\n"));
        var bytes = Encoding.UTF8.GetBytes("\"AAA\"\n");
        child.Write(bytes);
        bytes[1] = (byte)'B';
        bytes[2] = (byte)'B';
        bytes[3] = (byte)'B';
        child.CloseInput();
        var texts = new List<string?>();
        await foreach (var message in child.Messages.WithCancellation(new CancellationTokenSource(Bound).Token)) texts.Add(message.JSONValueString);

        Assert.Equal(2, texts.Count);
        Assert.Equal("AAA", texts[1]);
    }
}

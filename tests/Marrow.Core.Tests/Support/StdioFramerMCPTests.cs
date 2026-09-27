using System.Reflection;
using System.Text;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class StdioFramerMCPTests
{
    private static StdioProcess Unstarted() => new("unused", [], Environment.CurrentDirectory,
        new Dictionary<string, string>());

    private static void Invoke(StdioProcess process, string member, params object[] arguments) =>
        typeof(StdioProcess).GetMethod(member, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(process, arguments);

    [Fact]
    [Trait("Req", "PR-MCP-112")] // Core/Support/StdioFramer.swift:19-34,63-69; Core/Support/StdioProcess.swift:142-157.
    public async Task Every_split_preserves_CRLF_JSON_and_the_unterminated_EOF_frame_dropping_empty_and_invalid_lines()
    {
        using var runtime = new TestRuntime();
        var bytes = Encoding.UTF8.GetBytes("banner\n \r\n{\"first\":1} \r \r\n{\"last\":2} \r ");
        for (var split = 0; split <= bytes.Length; split++)
        {
            var process = Unstarted();
            Invoke(process, "Consume", bytes[..split]);
            Invoke(process, "Consume", bytes[split..]);
            Invoke(process, "FinishOutput");
            var messages = new List<JSONValue>();
            await foreach (var message in process.Messages) messages.Add(message);
            Assert.Equal(2, messages.Count);
            Assert.Equal(1L, messages[0]["first"]?.JSONValueInt);
            Assert.Equal(2L, messages[1]["last"]?.JSONValueInt);
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-112")] // Core/Support/StdioProcess.swift:50-53,159-166: strictly >64000 keeps suffix(16000); errorTail suffix(4000).
    public void Stderr_retains_64000_at_the_boundary_and_exactly_the_last_16000_one_byte_over()
    {
        using var runtime = new TestRuntime();
        var process = Unstarted();
        var bytes = Enumerable.Range(0, 64_001).Select(i => (byte)(65 + i % 26)).ToArray();
        Invoke(process, "AppendError", bytes[..64_000]);
        var buffer = (List<byte>)typeof(StdioProcess).GetField("errorBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(process)!;
        Assert.Equal(bytes[..64_000], buffer.ToArray());
        Invoke(process, "AppendError", bytes[64_000..]);
        Assert.Equal(bytes[^16_000..], buffer.ToArray());
        Assert.Equal(Encoding.UTF8.GetString(bytes[^4_000..]), process.ErrorTail);
    }
}

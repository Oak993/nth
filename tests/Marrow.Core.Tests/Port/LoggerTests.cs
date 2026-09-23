using System.Diagnostics;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-163: os_log's Logger(subsystem:category:) on Windows.
public sealed class LoggerTests
{
    [Fact]
    [Trait("Decision", "ED-163")]
    public void Error_hands_the_verbatim_message_and_the_subsystem_slash_category_channel_to_the_sink()
    {
        var lines = new List<(string Message, string Channel)>();
        using (Logger.Sink.Override((message, channel) => lines.Add((message, channel))))
        {
            new Logger(Logger.Subsystem, "hang").Error("Main thread unresponsive for 4.0s, sampling");
            new Logger("iordv.marrow", "codex").Error("");
        }
        Assert.Equal("iordv.marrow", Logger.Subsystem);
        Assert.Equal(new (string, string)[]
        {
            ("Main thread unresponsive for 4.0s, sampling", "iordv.marrow/hang"),
            ("", "iordv.marrow/codex"),
        }, lines.ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-163")]
    public void The_default_sink_is_a_trace_line_in_the_channel()
    {
        using var text = new StringWriter();
        using var listener = new TextWriterTraceListener(text);
        Trace.Listeners.Add(listener);
        try
        {
            new Logger(Logger.Subsystem, "hang").Error("Hang report written to C:\\Logs\\hang-x.txt");
            listener.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
        Assert.Contains("iordv.marrow/hang: Hang report written to C:\\Logs\\hang-x.txt", text.ToString(), StringComparison.Ordinal);
    }
}

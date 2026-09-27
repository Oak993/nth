// PORT-ONLY: ED-163. os_log's Logger(subsystem:category:) (HangWatchdog.swift:18, CodexSession.swift:13) on Windows.
// B3 52bc792 adds an Info level: MCPProxy.swift:116 Self.log.info("listening on \(port)").
using System.Diagnostics;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY (ED-163). <c>Logger(subsystem:category:)</c>: <see cref="Error"/> and <see cref="Info"/> each hand the
/// verbatim message and the channel "subsystem/category" to <see cref="Sink"/>, a trace line by default (precedent
/// ResizeStretch.cs:70). Like os_log it has no UI; the sink is not told which level called it, matching how the one
/// level this type had before B3 already worked.
/// </summary>
public sealed class Logger(string subsystem, string category)
{
    /// <summary>
    /// The one subsystem of every os_log site: HangWatchdog "hang", CodexSession "codex", the window guards "window"
    /// and "notification". DEVIATION OD-0002: upstream "iordv.droppycode"; the identity follows Marrow (ED-163).
    /// </summary>
    public const string Subsystem = "iordv.marrow";

    /// <summary>Where log lines go: (message, channel). Tests override it for their own flow.</summary>
    public static Ambient<Action<string, string>> Sink { get; } = new((Action<string, string>)Default);

    private readonly string channel = subsystem + "/" + category;

    /// <summary><c>log.error(...)</c>.</summary>
    public void Error(string message) => Sink.Current(message, channel);

    /// <summary><c>log.info(...)</c> (MCPProxy.swift:116, B3 52bc792).</summary>
    public void Info(string message) => Sink.Current(message, channel);

    private static void Default(string message, string channel) => Trace.WriteLine(message, channel);
}

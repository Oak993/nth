using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A running-nth handshake wired to a pretend nth and a fake clock, with a log of every status it reports (nth installer PLAN.md 10, unit U4).
/// The root has a long user name with accents (the plan's test path), so a path comparison that only works for ASCII fails here.
/// </summary>
public sealed class HandshakeRig
{
    public const string Root = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth";

    private readonly object gate = new();
    private readonly List<RunningNthStatus> seen = [];

    public HandshakeRig(bool running = true, RunningNthTimings? timings = null)
    {
        Nth = new FakeNth(Time, Root);
        if (!running) Nth.Close();
        Handshake = new RunningNthHandshake(Root, Nth, Nth, Time, timings);
        Handshake.StatusChanged += status =>
        {
            lock (gate) seen.Add(status);
        };
    }

    public ManualTimeProvider Time { get; } = new();

    public FakeNth Nth { get; }

    public RunningNthHandshake Handshake { get; }

    /// <summary>Every status the handshake reported, in order.</summary>
    public IReadOnlyList<RunningNthStatus> Seen
    {
        get
        {
            lock (gate) return [.. seen];
        }
    }

    public IReadOnlyList<RunningNthState> States => Seen.Select(status => status.State).ToList();

    public static IReadOnlyList<NthWorkItem> TwoThreads { get; } =
        [new NthWorkItem("Refactor the sign-in module", NthWorkKind.Thread, 240), new NthWorkItem("Write the release notes", NthWorkKind.Thread, 60)];

    /// <summary>Two threads for 5 s, one until 9 s, then idle.</summary>
    public static IReadOnlyList<NthWorkItem> Dwindling(TimeSpan at) =>
        at < TimeSpan.FromSeconds(5) ? TwoThreads : at < TimeSpan.FromSeconds(9) ? TwoThreads.Take(1).ToList() : [];

    public Task<RunningNthStatus> AskAsync() => Time.RunAsync(Handshake.AskAsync());

    /// <summary>The fake clock's time since the rig was made.</summary>
    public TimeSpan Elapsed => Time.Elapsed;
}

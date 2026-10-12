using Marrow.Setup.Port;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// The handshake on a real named pipe and the real clock, with a pretend nth behind a fake pipe server (nth installer PLAN.md 10, unit U4). The
/// waits are short, so a hung nth is found in under a second.
/// </summary>
public sealed record PipeRig(FakeNth Nth, FakeNthPipeServer Server, RunningNthHandshake Handshake)
{
    public static readonly RunningNthTimings Quick = new(
        ExitGrace: TimeSpan.FromMilliseconds(800),
        ExitPoll: TimeSpan.FromMilliseconds(20),
        StartupPatience: TimeSpan.FromMilliseconds(500),
        AskRetry: TimeSpan.FromMilliseconds(50),
        WorkPoll: TimeSpan.FromMilliseconds(100),
        WorkFailureLimit: 3,
        MergePoll: TimeSpan.FromMilliseconds(100),
        WithdrawTimeout: TimeSpan.FromSeconds(1));

    public static PipeRig Create(bool running = true)
    {
        var nth = new FakeNth(TimeProvider.System, HandshakeRig.Root);
        if (!running) nth.Close();
        nth.ExitDelay = TimeSpan.FromMilliseconds(100);
        var name = FakeNthPipeServer.NewName();
        var server = FakeNthPipeServer.Backing(name, nth);
        var channel = new PipeNthControlChannel(name, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        return new PipeRig(nth, server, new RunningNthHandshake(HandshakeRig.Root, channel, nth, timings: Quick));
    }
}

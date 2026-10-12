using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

// ED-NEW-inst-c7-6: a previous connection may have accepted quit even if a later request is busy.
internal sealed class C7QuitReentryChannel(FakeNth nth, bool lostFirstAnswer) : INthControlChannel
{
    internal List<NthControlRequest> Requests { get; } = [];
    internal bool EarlierQuitPending { get; private set; }
    internal int WithdrawFailuresRemaining { get; set; }
    internal bool BlockWithdraw { get; set; }
    private string? earlierId;
    private int quits;
    public Task<NthControlExchange> SendAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        var pid = nth.Main.ProcessId;
        var exe = nth.Main.ExecutablePath;
        switch (request.Command)
        {
            case NthControlCommand.Status:
                return Task.FromResult(NthControlExchange.Answered(new(NthControlReply.Status, pid, exe, [], 0, Id: request.Id)));
            case NthControlCommand.Withdraw:
                if (BlockWithdraw) return BlockedAsync(cancellationToken);
                if (WithdrawFailuresRemaining > 0)
                {
                    WithdrawFailuresRemaining--;
                    return Task.FromResult(NthControlExchange.Failed(NthControlFailure.NoAnswer));
                }
                if (request.Id == earlierId) EarlierQuitPending = false;
                return Task.FromResult(NthControlExchange.Answered(new(NthControlReply.Ok, pid, exe, [], 0, Id: request.Id)));
            default:
                if (++quits == 1)
                {
                    EarlierQuitPending = true;
                    earlierId = request.Id;
                    return Task.FromResult(lostFirstAnswer ? NthControlExchange.Failed(NthControlFailure.NoAnswer)
                        : NthControlExchange.Answered(new(NthControlReply.Ok, pid, exe, [], 0, Id: request.Id)));
                }
                return Task.FromResult(NthControlExchange.Answered(new(NthControlReply.Busy, pid, exe, HandshakeRig.TwoThreads, 2, Id: request.Id)));
        }
    }
    private static async Task<NthControlExchange> BlockedAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return NthControlExchange.Failed(NthControlFailure.NoAnswer);
    }

}

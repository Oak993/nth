using System.Buffers.Binary;
using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

// PORT-ONLY: ED-NEW-inst-c7-5. Final ACK still permits a last Busy refusal before any termination.
internal static class PipeNthQuitCompletion
{
    internal static async Task<NthControlExchange?> ReadAsync(Stream pipe, RunningNthProcess peer,
        Func<RunningNthProcess, bool> samePeer, NthControlRequest request, CancellationToken token)
    {
        var header = new byte[sizeof(int)];
        try
        {
            if (await pipe.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false) == 0) return null;
        }
        catch (IOException error) when (Closed(error)) { return null; }
        await pipe.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > NthControl.MaxMessageBytes) return NthControlExchange.Failed(NthControlFailure.BadAnswer);
        var bytes = new byte[length];
        await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (NthControlResponse.Parse(bytes) is not { CommitRequired: false } outcome
            || outcome.Reply is not (NthControlReply.Busy or NthControlReply.Refused))
            return NthControlExchange.Failed(NthControlFailure.BadAnswer);
        if (outcome.Pid != peer.ProcessId || !samePeer(peer)) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
        await NthControl.WriteAckAsync(pipe, token).ConfigureAwait(false);
        var end = new byte[1];
        try
        {
            if (await pipe.ReadAsync(end, token).ConfigureAwait(false) != 0)
                return NthControlExchange.Failed(NthControlFailure.BadAnswer);
        }
        catch (IOException error) when (Closed(error)) { }
        return NthControlExchange.Answered(outcome with { Exe = peer.ExecutablePath });
    }

    private static bool Closed(IOException error) => (error.HResult & 0xffff) is 109 or 233;
}

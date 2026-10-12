using System.IO.Pipes;
using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

// PORT-ONLY: ED-NEW-inst-c7-4. The account ACL and OS server-process identity are independent checks.
public sealed class PipeNthControlChannel(string pipeName, TimeSpan connectTimeout, TimeSpan replyTimeout, string? installRoot = null) : INthControlChannel
{
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(5);

    public static PipeNthControlChannel? ForCurrentUser(bool development = false, string? installRoot = null) =>
        NthControl.CurrentUserSid() is { } sid ? new PipeNthControlChannel(NthControl.PipeName(sid, development), DefaultConnectTimeout, DefaultReplyTimeout, installRoot) : null;

    public async Task<NthControlExchange> SendAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connect.CancelAfter(connectTimeout);
                await client.ConnectAsync(connect.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return NthControlExchange.Failed(NthControlFailure.NoPipe);
            }

            using var reply = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            reply.CancelAfter(replyTimeout);
            try
            {
                var peer = PipeNthPeer.Describe(client);
                return await SendConnectedAsync(client, peer, request, reply.Token, installRoot,
                    process => PipeNthPeer.Describe(client) == process).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return NthControlExchange.Failed(NthControlFailure.NoAnswer);
            }
        }
        catch (UnauthorizedAccessException) { return NthControlExchange.Failed(NthControlFailure.NotAllowed); }
        catch (IOException) { return NthControlExchange.Failed(NthControlFailure.NoAnswer); }
    }

    // The pure stream seam is the production connected path too. The lifetime check is repeated
    // before the request and after every reply; a recycled PID or a peer that exited cannot authorize a quit.
    internal static async Task<NthControlExchange> SendConnectedAsync(Stream client, RunningNthProcess? peer,
        NthControlRequest request, CancellationToken cancellationToken, string? installRoot = null,
        Func<RunningNthProcess, bool>? samePeer = null)
    {
        if (peer is not { StartedAtTicks: > 0 } || !RunningNthPaths.IsInside(installRoot, peer.ExecutablePath)
            || samePeer is null || !samePeer(peer)) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
        await NthControl.WriteFrameAsync(client, request.ToJson(), cancellationToken).ConfigureAwait(false);
        var bytes = await NthControl.ReadFrameAsync(client, cancellationToken).ConfigureAwait(false);
        if (bytes is null) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
        if (NthControlResponse.Parse(bytes) is not { } response) return NthControlExchange.Failed(NthControlFailure.BadAnswer);
        if (response.Pid != peer.ProcessId || !samePeer(peer)) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
        if (request.Id is not null && !string.Equals(response.Id, request.Id, StringComparison.Ordinal))
            return NthControlExchange.Failed(NthControlFailure.BadAnswer);
        if (response.CommitRequired)
        {
            if (request.Command != NthControlCommand.Quit || response.Reply != NthControlReply.Ok)
                return NthControlExchange.Failed(NthControlFailure.BadAnswer);
            // First ACK proves receipt of idle-and-quit. The host then rechecks idle and answers Busy or final Ok.
            await NthControl.WriteAckAsync(client, cancellationToken).ConfigureAwait(false);
            bytes = await NthControl.ReadFrameAsync(client, cancellationToken).ConfigureAwait(false);
            if (bytes is null) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
            if (NthControlResponse.Parse(bytes) is not { CommitRequired: false } final)
                return NthControlExchange.Failed(NthControlFailure.BadAnswer);
            if (final.Reply is not (NthControlReply.Ok or NthControlReply.Busy or NthControlReply.Refused))
                return NthControlExchange.Failed(NthControlFailure.BadAnswer);
            if (final.Pid != peer.ProcessId || !samePeer(peer)) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
            if (request.Id is not null && !string.Equals(final.Id, request.Id, StringComparison.Ordinal))
                return NthControlExchange.Failed(NthControlFailure.BadAnswer);
            response = final;
            await NthControl.WriteAckAsync(client, cancellationToken).ConfigureAwait(false);
        }
        else if (request.Command == NthControlCommand.Quit && response.Reply == NthControlReply.Ok)
            await NthControl.WriteAckAsync(client, cancellationToken).ConfigureAwait(false);
        else await AcknowledgeAsync(client, cancellationToken).ConfigureAwait(false);
        if (request.Command == NthControlCommand.Quit && response.Reply == NthControlReply.Ok)
        {
            var completion = await PipeNthQuitCompletion.ReadAsync(client, peer, samePeer, request, cancellationToken).ConfigureAwait(false);
            if (completion is not null) return completion;
        }
        return NthControlExchange.Answered(response with { Exe = peer.ExecutablePath });
    }

    private static async Task AcknowledgeAsync(Stream client, CancellationToken cancellationToken)
    {
        try { await NthControl.WriteAckAsync(client, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // A status or refusal may already have closed; quit framing is made transactional by ED-NEW-inst-c7-5.
        }
    }
}

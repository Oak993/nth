using System.IO.Pipes;
using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). The setup's end of nth's control pipe, one connection per request.
/// The pipe is nth's alone to answer: <c>PipeOptions.CurrentUserOnly</c> makes this side check that the server belongs to this user, so
/// an elevated nth (owned by Administrators) or another user's is <see cref="NthControlFailure.NotAllowed"/> and a look-alike that is not
/// nth's cannot be handed a request. The connect time covers an nth still starting; the reply time covers a hung one.
/// </summary>
public sealed class PipeNthControlChannel(string pipeName, TimeSpan connectTimeout, TimeSpan replyTimeout) : INthControlChannel
{
    /// <summary>How long to wait for the pipe to exist and have a free instance.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait for nth's answer (its main thread has to run for it to come).</summary>
    public static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The channel to the release nth of the signed-in user, or null off Windows.</summary>
    public static PipeNthControlChannel? ForCurrentUser(bool development = false) =>
        NthControl.CurrentUserSid() is { } sid ? new PipeNthControlChannel(NthControl.PipeName(sid, development), DefaultConnectTimeout, DefaultReplyTimeout) : null;

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
                await NthControl.WriteFrameAsync(client, request.ToJson(), reply.Token).ConfigureAwait(false);
                var bytes = await NthControl.ReadFrameAsync(client, reply.Token).ConfigureAwait(false);
                if (bytes is null) return NthControlExchange.Failed(NthControlFailure.NoAnswer);
                if (NthControlResponse.Parse(bytes) is not { } response) return NthControlExchange.Failed(NthControlFailure.BadAnswer);
                await AcknowledgeAsync(client, reply.Token).ConfigureAwait(false);
                return NthControlExchange.Answered(response);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return NthControlExchange.Failed(NthControlFailure.NoAnswer);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return NthControlExchange.Failed(NthControlFailure.NotAllowed);
        }
        catch (IOException)
        {
            return NthControlExchange.Failed(NthControlFailure.NoAnswer);
        }
    }

    // nth quits for a quit request only once it has seen this byte (NthControl.AckByte), so it never quits for a setup that did not get the "ok".
    // A pipe that closed already (an answer that needed no acknowledgement) is not a failure.
    private static async Task AcknowledgeAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        try
        {
            await NthControl.WriteAckAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Nothing to acknowledge to.
        }
    }
}

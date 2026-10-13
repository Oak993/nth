using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Core.Support;

// PORT-ONLY: ED-NEW-inst-c7-7. Deadline/disconnect applies to the complete connection, not just its first frame.
internal static partial class NthControlConnection
{
    internal static async Task ServeAsync(Stream pipe, INthControlHost host, TimeSpan requestTimeout,
        TimeSpan lifetime, CancellationToken serverStop, CancellationToken disconnected)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(serverStop, disconnected);
        connection.CancelAfter(lifetime);
        using var close = connection.Token.Register(() => pipe.Dispose());
        var aborts = new List<Action>();
        var committed = false;
        try
        {
            byte[]? bytes;
            using (var requestWindow = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
            {
                requestWindow.CancelAfter(requestTimeout);
                bytes = await NthControl.ReadFrameAsync(pipe, requestWindow.Token).WaitAsync(requestWindow.Token).ConfigureAwait(false);
            }
            if (bytes is null) return;
            var request = NthControlRequest.Parse(bytes);
            var answer = request is null
                ? new NthControlAnswer(NthControlResponse.Refused(Environment.ProcessId, Environment.ProcessPath ?? string.Empty, "unsupported"))
                : await NthControlServer.HandleConnectedAsync(host, request, serverStop, connection.Token).ConfigureAwait(false);
            if (answer.Abort is { } abort) aborts.Add(abort);
            await NthControl.WriteFrameAsync(pipe, answer.Response.ToJson(), connection.Token).WaitAsync(connection.Token).ConfigureAwait(false);
            if (!await Acknowledged(pipe, connection.Token).ConfigureAwait(false)) return;
            if (answer.PrepareCommit is { } prepare)
            {
                answer = await prepare(connection.Token).WaitAsync(connection.Token).ConfigureAwait(false);
                if (answer.Abort is { } finalAbort) aborts.Add(finalAbort);
                await NthControl.WriteFrameAsync(pipe, answer.Response.ToJson(), connection.Token).WaitAsync(connection.Token).ConfigureAwait(false);
                if (!await Acknowledged(pipe, connection.Token).ConfigureAwait(false)) return;
            }
            connection.Token.ThrowIfCancellationRequested();
            if (answer.Commit is { } commit)
            {
                var outcome = await commit(connection.Token).WaitAsync(connection.Token).ConfigureAwait(false);
                if (outcome is not null)
                {
                    await NthControl.WriteFrameAsync(pipe, outcome.ToJson(), connection.Token).WaitAsync(connection.Token).ConfigureAwait(false);
                    if (!await Acknowledged(pipe, connection.Token).ConfigureAwait(false)) return;
                }
            }
            else answer.AfterReply?.Invoke();
            committed = true;
        }
        catch (Exception)
        {
            // The peer left, a phase/deadline expired, shutdown began, or the host failed.
        }
        finally
        {
            if (!committed)
                foreach (var abort in aborts)
                    try { abort(); } catch (Exception) { /* One failed callback cannot retain another reservation or the pipe. */ }
            pipe.Dispose();
        }
    }

    private static async Task<bool> Acknowledged(Stream pipe, CancellationToken connection)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(connection);
        window.CancelAfter(NthControlServer.AckTimeout);
        var one = new byte[1];
        try
        {
            return await pipe.ReadAsync(one, window.Token).AsTask().WaitAsync(window.Token).ConfigureAwait(false) == 1 && one[0] == NthControl.AckByte;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { return false; }
    }

    // Peek does not consume an ACK. The connection has exactly one protocol reader.
    internal static async Task WatchDisconnectAsync(NamedPipeServerStream pipe, CancellationTokenSource disconnected,
        CancellationToken lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                if (!PeekNamedPipe(pipe.SafePipeHandle, IntPtr.Zero, 0, out _, out _, out _))
                {
                    disconnected.Cancel();
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(50), lifetime).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
            if (!lifetime.IsCancellationRequested) disconnected.Cancel();
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekNamedPipe(SafePipeHandle pipe, IntPtr buffer, uint bufferSize,
        out uint bytesRead, out uint bytesAvailable, out uint bytesLeft);
}

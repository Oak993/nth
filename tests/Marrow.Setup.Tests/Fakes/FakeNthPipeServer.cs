using System.IO.Pipes;
using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A pretend nth on a real named pipe (nth installer PLAN.md 10, unit U4): it listens on a name the test chose and, for every connection, reads
/// the request frame and runs the behaviour the test gave it, which may answer properly or do what a broken nth would (send garbage, send
/// nothing, close at once, answer after a long wait). It does not use the app's own server, so the setup's client is tested against a server
/// that can misbehave.
/// </summary>
public sealed class FakeNthPipeServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly List<string> requests = [];
    private readonly Task loop;
    private byte[]? lastRequest;

    public FakeNthPipeServer(string pipeName, Func<NamedPipeServerStream, CancellationToken, Task> behaviour)
    {
        Name = pipeName;
        var first = Create(pipeName);
        loop = Task.Run(() => Run(first, behaviour));
    }

    public string Name { get; }

    /// <summary>The request texts it received, in order.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (gate) return [.. requests];
        }
    }

    /// <summary>
    /// Answers every request as this pretend nth would (its work, its refusals, its hangs): a silent nth is a pipe that never answers, and a
    /// missing one closes the connection. This puts the whole handshake on a real pipe.
    /// </summary>
    public static FakeNthPipeServer Backing(string pipeName, FakeNth nth)
    {
        FakeNthPipeServer? server = null;
        server = new FakeNthPipeServer(pipeName, async (pipe, token) =>
        {
            var request = NthControlRequest.Parse(server!.lastRequest ?? []);
            if (request is null) return;
            var exchange = await nth.SendAsync(request, token);
            if (exchange.Response is { } response) await NthControl.WriteFrameAsync(pipe, response.ToJson(), token);
            else if (exchange.Failure == Marrow.Setup.Support.NthControlFailure.NoAnswer) await Silent()(pipe, token);
        });
        return server;
    }

    public static string NewName() => "nth-fake-" + Guid.NewGuid().ToString("N") + "-control";

    /// <summary>Answers every request with this response.</summary>
    public static Func<NamedPipeServerStream, CancellationToken, Task> Answer(NthControlResponse response) =>
        (pipe, token) => NthControl.WriteFrameAsync(pipe, response.ToJson(), token);

    /// <summary>Sends these exact bytes instead of an answer.</summary>
    public static Func<NamedPipeServerStream, CancellationToken, Task> SendBytes(byte[] bytes) =>
        async (pipe, token) =>
        {
            await pipe.WriteAsync(bytes, token);
            await pipe.FlushAsync(token);
        };

    /// <summary>Reads the request and says nothing until the other side leaves.</summary>
    public static Func<NamedPipeServerStream, CancellationToken, Task> Silent() =>
        async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }
        };

    /// <summary>Reads the request and closes without answering.</summary>
    public static Func<NamedPipeServerStream, CancellationToken, Task> CloseAtOnce() => (_, _) => Task.CompletedTask;

    private static NamedPipeServerStream Create(string name) => new(
        name,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task Run(NamedPipeServerStream first, Func<NamedPipeServerStream, CancellationToken, Task> behaviour)
    {
        var pipe = first;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(stop.Token);
                var accepted = pipe;
                pipe = Create(Name);
                _ = Serve(accepted, behaviour);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Stopped.
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private async Task Serve(NamedPipeServerStream pipe, Func<NamedPipeServerStream, CancellationToken, Task> behaviour)
    {
        await using (pipe)
        {
            try
            {
                var bytes = await NthControl.ReadFrameAsync(pipe, stop.Token);
                lastRequest = bytes;
                if (bytes is not null) lock (gate) requests.Add(System.Text.Encoding.UTF8.GetString(bytes));
                await behaviour(pipe, stop.Token);
                if (OperatingSystem.IsWindows() && pipe.IsConnected) pipe.WaitForPipeDrain();
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The client left, or the test is over.
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Stopped.
        }
        stop.Dispose();
    }
}

using System.IO.Pipes;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>A bare client of nth's control pipe for the server's tests: it speaks the wire by hand, never through the installer's own client.</summary>
public static class NthControlTestClient
{
    /// <summary>A name no other test and no real nth uses.</summary>
    public static string NewPipeName() => "nth-test-" + Guid.NewGuid().ToString("N") + "-control";

    public static async Task<NamedPipeClientStream> ConnectAsync(string pipeName, int timeoutMs = 5000)
    {
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeoutMs);
        return client;
    }

    /// <summary>One request, one answer (null when the server closed without answering).</summary>
    public static async Task<NthControlResponse?> AskAsync(string pipeName, NthControlRequest request, int timeoutMs = 5000)
    {
        using var client = await ConnectAsync(pipeName, timeoutMs);
        using var window = new CancellationTokenSource(timeoutMs);
        await NthControl.WriteFrameAsync(client, request.ToJson(), window.Token);
        var bytes = await NthControl.ReadFrameAsync(client, window.Token);
        return bytes is null ? null : NthControlResponse.Parse(bytes);
    }

    /// <summary>Sends these exact bytes, then reads whatever answer comes (null when the server closed without one).</summary>
    public static async Task<byte[]?> SendRawAsync(string pipeName, byte[] bytes, int timeoutMs = 5000)
    {
        using var client = await ConnectAsync(pipeName, timeoutMs);
        using var window = new CancellationTokenSource(timeoutMs);
        await client.WriteAsync(bytes, window.Token);
        await client.FlushAsync(window.Token);
        return await NthControl.ReadFrameAsync(client, window.Token);
    }
}

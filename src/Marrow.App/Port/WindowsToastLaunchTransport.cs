// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :77-92.
// PORT-ONLY ED-002 / ED-220 (6): a protocol's second launch carries userInfo to the instance holding the lock.
// Tests use IToastLaunchTransport fakes, never these named pipes or foreground API calls.
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.App.Port;

public static class ToastLaunchTransport
{
    public static Ambient<IToastLaunchTransport> Current { get; } = new(DisabledToastLaunchTransport.Instance);
}

public sealed class DisabledToastLaunchTransport : IToastLaunchTransport
{
    public static readonly DisabledToastLaunchTransport Instance = new();
    private DisabledToastLaunchTransport() { }
    public bool Send(string name, string payload) => false;
    public IDisposable Listen(string name, Action<string> received) => new CancellationTokenSource();
}

public sealed class WindowsToastLaunchTransport : IToastLaunchTransport
{
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    public bool Send(string name, string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(10000); // permits a second launch while the first instance is still starting
            _ = AllowSetForegroundWindow(-1); // ED-002: the receiving app may take the foreground for this click
            var bytes = Encoding.UTF8.GetBytes(payload);
            if (bytes.Length > 64 * 1024) return false;
            using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            writer.Flush();
            return true;
        }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IDisposable Listen(string name, Action<string> received) => new Listener(name, received);

    private sealed class Listener : IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        private readonly string name;
        private readonly Action<string> received;
        private readonly IMainExecutor main;

        public Listener(string name, Action<string> received)
        {
            this.name = name;
            this.received = received;
            main = MainActor.Executor.Current;
            var first = CreatePipe(); // endpoint exists before launch creates any window
            loop = Run(first);
        }

        private NamedPipeServerStream CreatePipe() => new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        private async Task Run(NamedPipeServerStream first)
        {
            NamedPipeServerStream? pipe = first;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using (pipe)
                    {
                        try
                        {
                            await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                            using var message = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                            message.CancelAfter(TimeSpan.FromSeconds(10));
                            var lengthBytes = new byte[sizeof(int)];
                            await pipe.ReadExactlyAsync(lengthBytes, message.Token).ConfigureAwait(false);
                            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                            if (length is > 0 and <= 64 * 1024)
                            {
                                var bytes = new byte[length];
                                await pipe.ReadExactlyAsync(bytes, message.Token).ConfigureAwait(false);
                                var payload = Encoding.UTF8.GetString(bytes);
                                main.Enqueue(() => received(payload));
                            }
                        }
                        catch (IOException) { } // a disconnected/partial client never routes
                        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                    }
                    if (stop.IsCancellationRequested) break;
                    pipe = CreatePipe();
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { pipe?.Dispose(); }
        }

        public void Dispose()
        {
            stop.Cancel();
            loop.GetAwaiter().GetResult();
            stop.Dispose();
        }
    }
}

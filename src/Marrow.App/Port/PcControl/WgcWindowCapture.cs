// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-5, ED-NEW-pcctl-9; DESIGN.md 3.1 item 5, 3.2, critique
// C23). The first capture source: Windows.Graphics.Capture for one window, which reads a window behind others and never
// moves it. Each window in use keeps a persistent free-threaded frame pool on one shared Direct3D 11 device; a pool keeps
// only the newest frame, and a capture copies the window's frame (its top-left frame-sized region, a GPU crop) into a
// staging texture and reads it back. Pools close after 30 s unused. Where Windows has MinUpdateInterval the session is
// throttled to one frame per 200 ms; elsewhere a pool whose window ran above 5 frames per second closes after each grab
// (WgcPoolPolicy). The cursor is never captured; the yellow border is turned off only where Windows has the property and
// grants borderless access. A window that is minimized, gone, or refuses capture gives null, so the chain falls back to
// PrintWindow. Native runs only; tests use fake sources.
using System.Diagnostics;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Marrow.App.Port.PcControl;

public sealed class WgcWindowCapture : IWindowCapture, IDisposable
{
    private const string SessionClass = "Windows.Graphics.Capture.GraphicsCaptureSession";

    /// <summary>A content size within this many pixels of the window's frame still counts as the same size.</summary>
    public const int SizeSlack = 2;

    /// <summary>How often idle pools are looked for.</summary>
    public const int SweepMs = 5_000;

    private static readonly Lazy<WgcWindowCapture> shared = new(() => new WgcWindowCapture());
    private static readonly Lazy<bool> supported = new(Probe);
    private static readonly Lazy<bool> borderless = new(RequestBorderless);

    private readonly object gate = new();
    private readonly Dictionary<nint, Pool> pools = [];
    private WgcInterop.Device? device;
    private Timer? sweeper;
    private bool disposed;

    public static WgcWindowCapture Shared => shared.Value;

    /// <summary>True when this Windows has Windows.Graphics.Capture and allows it here.</summary>
    public static bool IsSupported => supported.Value;

    public string Name => "wgc";

    private static long Now => Environment.TickCount64;

    public async Task<PcFrame?> Capture(nint window, PcRect frame, CancellationToken cancellationToken)
    {
        if (!IsSupported || window == 0 || frame.Width <= 0 || frame.Height <= 0) return null;
        if (!WgcInterop.IsWindow(window) || WgcInterop.IsIconic(window)) return null;
        Pool? pool;
        try
        {
            pool = Open(window);
        }
        catch (Exception error) when (IsCaptureFailure(error))
        {
            Trace.TraceWarning("Tactus: window capture could not start: " + error.Message);
            return null;
        }
        if (pool is null) return null;
        try
        {
            var result = await pool.Grab(frame, cancellationToken).ConfigureAwait(false);
            if (pool.CloseAfterGrab()) Close(window, pool);
            return result;
        }
        catch (Exception error) when (IsCaptureFailure(error))
        {
            Trace.TraceWarning("Tactus: window capture failed: " + error.Message);
            Close(window, pool);
            if (error.HResult is DeviceRemoved or DeviceReset) Reset();
            return null;
        }
    }

    /// <summary>Closes every pool and the device.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
        }
        Reset();
    }

    private const int DeviceRemoved = unchecked((int)0x887A0005);
    private const int DeviceReset = unchecked((int)0x887A0007);

    private static bool IsCaptureFailure(Exception error) =>
        error is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException;

    private Pool? Open(nint window)
    {
        // The borderless request can wait up to 2 s once per process, so it is asked before the lock.
        var noBorder = ApiInformation.IsPropertyPresent(SessionClass, "IsBorderRequired") && borderless.Value;
        lock (gate)
        {
            if (disposed) return null;
            if (pools.TryGetValue(window, out var open)) return open;
            device ??= WgcInterop.Device.Create();
            var pool = Pool.Start(window, device, noBorder, () => Close(window, null));
            pools[window] = pool;
            sweeper ??= new Timer(_ => Sweep(), null, SweepMs, SweepMs);
            return pool;
        }
    }

    private void Close(nint window, Pool? which)
    {
        Pool? pool;
        lock (gate)
        {
            if (!pools.TryGetValue(window, out pool) || (which is not null && !ReferenceEquals(pool, which))) return;
            pools.Remove(window);
            StopSweeperIfEmpty();
        }
        pool.Dispose();
    }

    private void Sweep()
    {
        List<Pool> idle;
        lock (gate)
        {
            idle = [];
            foreach (var (window, pool) in pools.ToList())
            {
                if (!pool.Idle()) continue;
                pools.Remove(window);
                idle.Add(pool);
            }
            StopSweeperIfEmpty();
        }
        foreach (var pool in idle) pool.Dispose();
    }

    private void StopSweeperIfEmpty()
    {
        if (pools.Count > 0) return;
        sweeper?.Dispose();
        sweeper = null;
    }

    private void Reset()
    {
        List<Pool> all;
        WgcInterop.Device? old;
        lock (gate)
        {
            all = [.. pools.Values];
            pools.Clear();
            StopSweeperIfEmpty();
            old = device;
            device = null;
        }
        foreach (var pool in all) pool.Dispose();
        old?.Dispose();
    }

    private static bool Probe()
    {
        try
        {
            return ApiInformation.IsTypePresent(SessionClass) && GraphicsCaptureSession.IsSupported();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or TypeLoadException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks once per process for borderless capture (GraphicsCaptureAccess, Windows 11). Only then is a session's
    /// IsBorderRequired turned off; a refusal, a missing API or a slow answer (2 s) keeps the border.
    /// </summary>
    private static bool RequestBorderless()
    {
        if (!ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess")) return false;
        var statics = WgcInterop.Factory("Windows.Graphics.Capture.GraphicsCaptureAccess", WgcInterop.IidCaptureAccessStatics);
        nint operation = 0;
        nint info = 0;
        try
        {
            if (statics == 0) return false;
            if (WgcInterop.Slot<WgcInterop.RequestAccessFn>(statics, WgcInterop.SlotAccessStaticsRequestAccess)(statics, 0 /* Borderless */, out operation) < 0) return false;
            info = WgcInterop.Query(operation, WgcInterop.IidAsyncInfo);
            if (info == 0) return false;
            var status = WgcInterop.Slot<WgcInterop.GetInt32Fn>(info, WgcInterop.SlotAsyncInfoStatus);
            var waited = Stopwatch.StartNew();
            // AsyncStatus: Started 0, Completed 1, Canceled 2, Error 3.
            var state = 0;
            while ((status(info, out state) >= 0) && state == 0 && waited.ElapsedMilliseconds < 2_000) Thread.Sleep(10);
            if (state != 1) return false;
            // AppCapabilityAccessStatus.Allowed is 4.
            return WgcInterop.Slot<WgcInterop.GetInt32Fn>(operation, WgcInterop.SlotAsyncOperationGetResults)(operation, out var access) >= 0 && access == 4;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            WgcInterop.Release(ref info);
            WgcInterop.Release(ref operation);
            WgcInterop.Release(ref statics);
        }
    }

    /// <summary>One window's frame pool, session and newest frame.</summary>
    private sealed class Pool : IDisposable
    {
        private readonly object sync = new();
        private readonly WgcInterop.Device device;
        private readonly GraphicsCaptureItem item;
        private readonly Direct3D11CaptureFramePool framePool;
        private readonly GraphicsCaptureSession session;
        private readonly WgcPoolPolicy policy;
        private Windows.Graphics.SizeInt32 size;
        private Direct3D11CaptureFrame? newest;
        private TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint staging;
        private (int Width, int Height) stagingSize;
        private bool closed;

        private Pool(WgcInterop.Device device, GraphicsCaptureItem item, Direct3D11CaptureFramePool framePool, GraphicsCaptureSession session, bool throttled)
        {
            this.device = device;
            this.item = item;
            this.framePool = framePool;
            this.session = session;
            size = item.Size;
            policy = new WgcPoolPolicy(throttled);
            policy.Use(Now);
        }

        public static Pool Start(nint window, WgcInterop.Device device, bool noBorder, Action onClosed)
        {
            var item = CreateItem(window);
            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.Winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            GraphicsCaptureSession? session = null;
            try
            {
                session = framePool.CreateCaptureSession(item);
                if (ApiInformation.IsPropertyPresent(SessionClass, "IsCursorCaptureEnabled")) session.IsCursorCaptureEnabled = false;
                if (noBorder)
                    PutOn(session, WgcInterop.IidCaptureSession3, abi => WgcInterop.Slot<WgcInterop.PutBooleanFn>(abi, WgcInterop.SlotPutFirstProperty)(abi, 0));
                var throttled = ApiInformation.IsPropertyPresent(SessionClass, "MinUpdateInterval")
                    && PutOn(session, WgcInterop.IidCaptureSession5, abi => WgcInterop.Slot<WgcInterop.PutTimeSpanFn>(abi, WgcInterop.SlotPutFirstProperty)(abi, TimeSpan.FromMilliseconds(WgcPoolPolicy.MinUpdateIntervalMs).Ticks));
                var pool = new Pool(device, item, framePool, session, throttled);
                framePool.FrameArrived += (sender, _) => pool.OnFrame(sender);
                item.Closed += (_, _) => onClosed();
                session.StartCapture();
                return pool;
            }
            catch
            {
                session?.Dispose();
                framePool.Dispose();
                throw;
            }
        }

        public bool Idle()
        {
            lock (sync) return policy.Idle(Now);
        }

        public bool CloseAfterGrab()
        {
            lock (sync) return policy.CloseAfterGrab(Now);
        }

        /// <summary>
        /// The newest frame of the window as its frame-sized BGRA pixels, after waiting what the policy says for a frame
        /// still on its way; null when no frame came or its size does not match the window's frame (mid-resize).
        /// </summary>
        public async Task<PcFrame?> Grab(PcRect frame, CancellationToken cancellationToken)
        {
            Task next;
            int wait;
            lock (sync)
            {
                policy.Use(Now);
                wait = policy.WaitMs(Now);
                next = arrived.Task;
            }
            if (wait > 0)
            {
                try
                {
                    await next.WaitAsync(TimeSpan.FromMilliseconds(wait), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
            lock (sync)
            {
                if (closed || newest is null) return null;
                var content = newest.ContentSize;
                if (Math.Abs(content.Width - frame.Width) > SizeSlack || Math.Abs(content.Height - frame.Height) > SizeSlack) return null;
                var surface = newest.Surface;
                var access = WgcInterop.Query(WgcInterop.Abi(surface), WgcInterop.IidDxgiInterfaceAccess);
                nint texture = 0;
                try
                {
                    if (access == 0) return null;
                    var iid = WgcInterop.IidTexture2D;
                    WgcInterop.Check(WgcInterop.Slot<WgcInterop.GetInterfaceFn>(access, WgcInterop.SlotDxgiAccessGetInterface)(access, ref iid, out texture));
                    WgcInterop.Slot<WgcInterop.GetDescFn>(texture, WgcInterop.SlotTextureGetDesc)(texture, out var desc);
                    var width = Math.Min(Math.Min(content.Width, frame.Width), (int)desc.Width);
                    var height = Math.Min(Math.Min(content.Height, frame.Height), (int)desc.Height);
                    if (width <= 0 || height <= 0) return null;
                    if (stagingSize != (width, height))
                    {
                        WgcInterop.Release(ref staging);
                        staging = device.Staging(width, height);
                        stagingSize = (width, height);
                    }
                    return PcFrame.Packed(device.Read(texture, staging, width, height), width, height);
                }
                finally
                {
                    WgcInterop.Release(ref texture);
                    WgcInterop.Release(ref access);
                    GC.KeepAlive(surface);
                }
            }
        }

        /// <summary>A frame arrived (on the pool's own thread): keep the newest, recreate the pool when the window resized.</summary>
        private void OnFrame(Direct3D11CaptureFramePool sender)
        {
            TaskCompletionSource signal;
            lock (sync)
            {
                if (closed) return;
                var got = false;
                while (sender.TryGetNextFrame() is { } frame)
                {
                    newest?.Dispose();
                    newest = frame;
                    got = true;
                }
                if (!got) return;
                policy.Frame(Now);
                var content = newest!.ContentSize;
                if (content.Width != size.Width || content.Height != size.Height)
                {
                    size = content;
                    sender.Recreate(device.Winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                }
                signal = arrived;
                arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            signal.TrySetResult();
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (closed) return;
                closed = true;
                newest?.Dispose();
                newest = null;
                WgcInterop.Release(ref staging);
            }
            session.Dispose();
            framePool.Dispose();
            arrived.TrySetResult();
        }

        private static GraphicsCaptureItem CreateItem(nint window)
        {
            var interop = WgcInterop.Factory("Windows.Graphics.Capture.GraphicsCaptureItem", WgcInterop.IidCaptureItemInterop);
            nint abi = 0;
            try
            {
                if (interop == 0) throw new InvalidOperationException("Windows.Graphics.Capture has no window interop here.");
                var iid = WgcInterop.IidCaptureItem;
                WgcInterop.Check(WgcInterop.Slot<WgcInterop.CreateForWindowFn>(interop, WgcInterop.SlotItemInteropCreateForWindow)(interop, window, ref iid, out abi));
                return GraphicsCaptureItem.FromAbi(abi);
            }
            finally
            {
                WgcInterop.Release(ref abi);
                WgcInterop.Release(ref interop);
            }
        }

        /// <summary>Sets a property through a newer session interface; false when this Windows lacks it or refuses.</summary>
        private static bool PutOn(GraphicsCaptureSession session, Guid iid, Func<nint, int> put)
        {
            var abi = WgcInterop.Query(WgcInterop.Abi(session), iid);
            try
            {
                return abi != 0 && put(abi) >= 0;
            }
            finally
            {
                WgcInterop.Release(ref abi);
                GC.KeepAlive(session);
            }
        }
    }
}

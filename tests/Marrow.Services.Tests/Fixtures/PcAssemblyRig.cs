using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus's composition on the fake desktop (ED-NEW-pcctl-40). PcBackends made of fakes: the fake desktop for
// windows, UI Automation, change events, messages, security and the launcher; a healthy physical input monitor on the
// desktop's clock; a fake input hold and real-input host; capture through a fake source and encoder; one fake audit per
// chat thread; a manual wait clock. The settings start on and can be changed; the stop key is a fake. A test may give the
// lease and the monitor their own clock (a manual one, to act between the gate's polls) and a foreground protection
// (ED-NEW-pcctl-38).
public sealed class PcAssemblyRig : IDisposable
{
    public PcAssemblyRig(PcSettings? settings = null, IPcClock? clock = null, IForegroundProtection? protection = null)
    {
        Settings = settings ?? PcSettings.Default with { Enabled = true };
        var time = clock ?? Desktop.Clock;
        Input = new PhysicalInputMonitor(time, protection);
        Input.Installed(true);
        Input.LastInputSeen(time.NowMs);
        Backends = new PcBackends
        {
            Catalog = Desktop,
            Security = Desktop,
            Reader = Desktop,
            Feed = Desktop,
            Messages = Desktop,
            Foreground = Desktop,
            Own = Desktop,
            Launcher = Desktop,
            Clock = time,
            Input = Input,
            Hold = Hold,
            RealHost = new PcFakeRealInputHost(Input, time),
            Capture = new PcCapturePipeline([new PcFakeCapture("wgc", PcFakeCapture.Solid(1200, 800, 90))], new PcFakeEncoder()),
            Audit = thread =>
            {
                lock (Audits)
                {
                    if (!Audits.TryGetValue(thread, out var audit)) Audits[thread] = audit = new PcFakeAudit();
                    return audit;
                }
            },
            WaitClock = WaitClock,
            Now = () => PcEngineHarness.Today,
            Owned = [Owned],
        };
    }

    public PcFakeDesktop Desktop { get; } = new();
    public PhysicalInputMonitor Input { get; }
    public PcFakeInputHold Hold { get; } = new();
    public PcManualClock WaitClock { get; } = new();
    public PcFakeStopKey StopKey { get; } = new();
    public PcDisposeCount Owned { get; } = new();
    public Dictionary<string, PcFakeAudit> Audits { get; } = new(StringComparer.Ordinal);
    public PcBackends Backends { get; }
    public PcSettings Settings { get; set; }
    public int InterruptCalls { get; private set; }

    /// <summary>The assembly over these backends, with the fake stop key and the given surface.</summary>
    public PcAssembly Build(PcSurface? surface = null) => assembly ??= new PcAssembly(Backends, () => Settings, _ => StopKey, surface, () => InterruptCalls++);

    public void Dispose() => assembly?.Dispose();

    /// <summary>The engine harness's notepad-like app on this desktop: [2] a document field, [3] Save (Ctrl+S), [4] a checkbox.</summary>
    public PcFakeApp Notepad(nint handle = 0x1a, int processId = 101)
    {
        var app = Desktop.AddApp(handle, "Untitled - Notepad", "notepad.exe", processId);
        var x = app.Frame.X;
        var y = app.Frame.Y;
        var id = PcEngineHarness.Id(handle);
        app.Root.Add(new PcFakeNode("1." + id + ".2", "edit", "Text editor") { Actions = PcActions.Value | PcActions.Text | PcActions.Focus, Value = "", Bounds = new PcRect(x + 10, y + 60, 1100, 600) });
        app.Root.Add(new PcFakeNode("1." + id + ".3", "button", "Save") { Actions = PcActions.Invoke, Shortcut = "Ctrl+S", Bounds = new PcRect(x + 10, y + 10, 80, 30) });
        app.Root.Add(new PcFakeNode("1." + id + ".4", "check box", "Word wrap") { Actions = PcActions.Toggle, Bounds = new PcRect(x + 100, y + 10, 120, 30) });
        app.FocusControl = app.Root.Children[0];
        return app;
    }

    private PcAssembly? assembly;
}

/// <summary>Counts its disposals.</summary>
public sealed class PcDisposeCount : IDisposable
{
    public int Count { get; private set; }

    public void Dispose() => Count++;
}

/// <summary>A settings store whose every read fails.</summary>
public sealed class ThrowingSettingsStore : ISettingsStore
{
    public object? Object(string key) => throw new InvalidOperationException("the store is gone");

    public void Set(string key, object? value) => throw new InvalidOperationException("the store is gone");

    public void RemoveObject(string key) => throw new InvalidOperationException("the store is gone");
}

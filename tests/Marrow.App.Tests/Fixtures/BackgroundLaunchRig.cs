using Marrow.App.Assistant;
using Marrow.App.Tray;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): the launch of the duties over a real model, a temporary store and activity log, a fake stop chord, tray and Task Scheduler. Helper code only (R-TEST-3).</summary>
internal sealed class BackgroundLaunchRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly FakeStopChordHost chord = new();
    private readonly FakeTrayHost trayHost = new();
    private bool windowHidden = true;

    public BackgroundLaunchRig(AssistantTempRoot temp, Guid? identity = null)
    {
        Temp = temp;
        Key = new Marrow.App.Safety.GlobalStop(chord, () => null);
        Tray = new AppTray(trayHost, () => windowHidden, open: () => { });
        Model = harness.MakeModel();
        harness.PrepareBootstrap(Model);
        Seams = new BackgroundStartSeams
        {
            Roots = () => temp.Roots,
            Keys = () => temp.Keys,
            StopKey = Key,
            Tray = Tray,
            Host = new DutySeams
            {
                RunKey = new FakeRunKey(),
                TaskHost = new FakeTaskSchedulerHost(),
                Processes = new FakeProcessRunner((_, _) => new ProcessOutcome(0, "", "")),
                Desktop = new FakeDesktop(),
                Time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero)),
            },
        };
        var settings = new DextraSettings(new InMemorySettingsStore()) { Value = DextraPreferences.Default with { FirstRunDone = true, Id = identity } };
        harness.Main.OnMain(() => Model.AssistantHost = new DextraHost(Model, settings, startup: Seams));
    }

    /// <summary>Runs on the model's main thread, as the app's taps do.</summary>
    public T OnMain<T>(Func<T> work) => harness.Main.OnMain(work);

    public void OnMain(Action work) => harness.Main.OnMain(work);

    public void Run(Func<Task> work) => harness.Main.Loop.Run(work);

    public AssistantController? Start(DextraHost? host = null, StartupMode? mode = null)
    {
        var pending = OnMain(() => (host ?? Model.AssistantHost).EnsureStartedAsync(mode: mode));
        return Main.Loop.Run(() => pending);
    }

    public AssistantTempRoot Temp { get; }

    public AppModel Model { get; }
    public TestRuntime Main => harness.Main;

    public Marrow.App.Safety.GlobalStop Key { get; }

    public AppTray Tray { get; }

    public BackgroundStartSeams Seams { get; }

    public int Quits { get; private set; }

    public DutyHost? Run(StartupMode mode, out string? problem)
    {
        string? why = null;
        harness.Main.OnMain(() => Model.EnsureTray(() => windowHidden, null, () => { }, Seams.Tray).Refresh());
        var host = harness.Main.Loop.Run(() => BackgroundStart.RunAsync(Model, mode, () => Quits++, Seams));
        why = Model.AssistantHost.StartFault;
        problem = why;
        return host;
    }

    public void Dispose()
    {
        harness.Main.OnMain(Model.UnbindDextra);
        Model.DextraStore?.Dispose();
        Tray.Dispose();
        Key.Dispose();
        harness.Dispose();
    }
}

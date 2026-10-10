using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Voice;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Fixtures;

// VT-10 test fixture (voice-SPEC section 21, V5): one app model with a lead thread (and, on request, a head at work), the voice feature on,
// every voice seam scripted (no microphone, no engine, no model file: R-TEST-10), a hub of its own, a manual frame source and the shell's
// "open" call recorded. Built inside the headless flow, so its overrides reach the composers made in it.
internal sealed class VoiceComposerRig : IDisposable
{
    private readonly List<IDisposable> scopes = [];

    public VoiceComposerRig(VoiceOrbSettings? orb = null, VoiceTypingSettings? typing = null, bool workingHead = false)
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        Store = new AppModelHarness();
        var project = AppModelSamples.Project("Voice composer", Store.NewFolder("voice-composer"));
        var lead = AppModelSamples.Thread(project);
        var library = new List<ChatThread> { lead };
        ChatThread? head = null;
        if (workingHead)
        {
            head = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                Title = "head",
                ParentThreadID = lead.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped),
            };
            library.Add(head);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Marrow.Services.Store.Storage.LibraryURL)!);
        Store.WriteLibrary([project], library);
        foreach (var thread in library) AppModelHarness.WriteDocument(thread.Id);
        App = Store.MakeModel();
        Store.PrepareBootstrap(App);
        Main.OnMain(() =>
        {
            App.Settings.HydraQueueHeads = false;
            App.Settings.HydraAlwaysHeads = false;
            App.Settings.VoiceOrb = orb ?? VoiceOrbSettings.Default;
            App.Settings.VoiceTyping = typing ?? VoiceTypingSettings.Default;
        });
        Runtime = Main.OnMain(() => new ThreadRuntime(lead.Id, App));
        Main.Loop.Run(() => Runtime.EnsureLoaded(), AppModelHarness.Bound);
        if (head is not null)
            Main.OnMain(() => App.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } }));

        Frames = new ManualFrameSource();
        scopes.Add(FrameClock.Source.Override(Frames));
        scopes.Add(VoiceFeature.Switch.Override(new VoiceFeatureSwitch(true)));
        scopes.Add(VoiceSessionFactory.Current.Override(new VoiceSessionFactory(MakeSession)));
        scopes.Add(Workspace.Open.Override(url =>
        {
            Opened.Add(url.AbsoluteString);
            return true;
        }));
        Presenter = new RecordingPresenter();
        scopes.Add(Presentation.Presenter.Override(Presenter));
    }

    public AppModelHarness Store { get; }

    public AppModel App { get; }

    public ThreadRuntime Runtime { get; }

    public TestRuntime Main => Store.Main;

    public ManualFrameSource Frames { get; }

    public RecordingPresenter Presenter { get; }

    public VoiceSessionHub Hub { get; } = new();

    public ScriptedCapture Capture { get; set; } = new();

    public ScriptedMicrophone Microphone { get; } = new();

    public ScriptedVad Vad { get; set; } = ScriptedVad.Silence();

    public ScriptedEngine Engine { get; set; } = ScriptedEngine.Always(Hypothesis.Empty);

    public List<VoiceSession> Sessions { get; } = [];

    public List<string> Opened { get; } = [];

    private VoiceSession MakeSession(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings> store)
    {
        var session = new VoiceSession(() => settings() with { Model = new SpeechModel("tiny-q5_1", "model-tiny-q5_1.bin") }, store,
            new VoiceSeamSet(() => Capture, () => Vad, () => Engine, () => Microphone, () => new FailClosedStreamingResampler()), Hub);
        Sessions.Add(session);
        return session;
    }

    /// <summary>The speaker says <paramref name="script"/>: its signal, its speech spans, its hypotheses.</summary>
    public void Speak(VoiceScript script)
    {
        Capture.PlayOnStart(script.Signal(16000), TimeSpan.FromSeconds(script.SpeechEnd + 120));
        Vad = script.CreateVad();
        Engine = script.CreateEngine();
    }

    /// <summary>A quiet room: the capture delivers noise that is never exactly zero, and the VAD hears no speech.</summary>
    public void QuietRoom()
    {
        Capture.PlayOnStart(ScriptedSignals.RoomNoise, TimeSpan.FromSeconds(400));
        Vad = ScriptedVad.Silence();
    }

    /// <summary>The words every voice test says: four words over 2.5 s.</summary>
    public static VoiceScript SwitchTheme() => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8, FirstGuess: "team"),
        new ScriptedWord("please.", 2.0, 2.5),
    ]);

    public const string SwitchThemeText = "switch the theme please.";

    public ComposerView MakeView() => Main.OnMain(() =>
    {
        var view = new ComposerView(Runtime, workingDirectory: null);
        AppEnvironment.SetAppModel(view, App);
        Drain();
        return view;
    });

    /// <summary>A composer on screen in a window <paramref name="width"/> wide.</summary>
    public (ComposerView View, AvaloniaWindow Window) Open(double width = 600)
    {
        var view = MakeView();
        var window = Main.OnMain(() => Show(view, App, width));
        PumpWindow(window);
        return (view, window);
    }

    /// <summary>Track reactions land on the next main turn; layout and the text box's posted jobs run on the dispatcher.</summary>
    public void PumpWindow(AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++) Main.OnMain(() => Settle(window));
    }

    /// <summary>The frame a screen delivers while a transition runs: 60 Hz.</summary>
    private static readonly TimeSpan FrameStep = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>How long one call steps frame by frame at most before it jumps (every transition of the voice surfaces ends within a second).</summary>
    private static readonly TimeSpan SteppedSpan = TimeSpan.FromSeconds(2);

    /// <summary>A pill of the window is in a transition now (its width, opacity or labels move, a relabel is due, or its countdown glides).</summary>
    private bool PillMoving(AvaloniaWindow window) => Main.OnMain(() => window.GetVisualDescendants().OfType<VoicePill>().Any(pill => pill.IsMoving));

    /// <summary>
    /// Time passes: the manual clock (timers, the session), the frame source (transitions), then layout and the dispatcher. A running app never jumps the clock and delivers one frame
    /// through a running transition: the pill keeps its progress across a stalled frame (VoicePill, LookMotion.PreserveFrameProgress; it never snaps), so a gap of more than 50 ms is a
    /// stall to it. So while a pill transition runs, a frame comes every 1/60 s, for up to <see cref="SteppedSpan"/>; the rest of a long wait, in which nothing moves, passes in one jump
    /// (the exact total, in ticks, is unchanged).
    /// </summary>
    public void Elapse(AvaloniaWindow window, double seconds)
    {
        var remaining = TimeSpan.FromSeconds(seconds);
        var stepped = TimeSpan.Zero;
        while (remaining >= FrameStep && stepped < SteppedSpan && Frames.Pending > 0 && PillMoving(window))
        {
            Main.Advance(FrameStep);
            Main.OnMain(() => Frames.Advance(FrameStep));
            PumpWindow(window);
            remaining -= FrameStep;
            stepped += FrameStep;
        }
        if (remaining > TimeSpan.Zero || stepped == TimeSpan.Zero)
        {
            Main.Advance(remaining);
            Main.OnMain(() => Frames.Advance(remaining));
            PumpWindow(window);
        }
    }

    public void Close(AvaloniaWindow window) => Main.OnMain(() =>
    {
        Settle(window);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    });

    public void Dispose()
    {
        for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
        Store.Dispose();
    }
}

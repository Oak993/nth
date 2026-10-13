using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Port;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U8, ED-NEW-inst-u8-1). What Settings > General > "On this
/// PC" needs from this machine: the install root and the way to get the shell and the user's registry. It is bound once, in
/// the app's <c>Program.cs</c> and only in a Velopack install (<c>Program.InstalledByVelopack</c>), so everywhere else (a dev
/// build, a portable copy, a test) the section is <see cref="Unavailable"/> and hidden. The factories are called on the
/// model's worker thread, never the UI thread, and only when the first scan or change runs.
/// </summary>
public sealed class OnThisPcPlatform(string? root, Func<IShellLinks> shell, Func<IUserRegistry> registry)
{
    /// <summary>No install to manage: the section is hidden.</summary>
    public static OnThisPcPlatform Unavailable { get; } = new(null, static () => throw new InvalidOperationException("Not a Velopack install."), static () => throw new InvalidOperationException("Not a Velopack install."));

    /// <summary>The platform in force; <c>Program.cs</c> sets the process default, a test overrides it for its own flow.</summary>
    public static Ambient<OnThisPcPlatform> Slot { get; } = new(Unavailable);

    /// <summary>The install root (<c>%LOCALAPPDATA%\Programs\nth</c> by default); null when there is no Velopack install.</summary>
    public string? Root { get; } = root;

    public bool IsAvailable => Root is not null;

    public Func<IShellLinks> Shell { get; } = shell ?? throw new ArgumentNullException(nameof(shell));

    public Func<IUserRegistry> Registry { get; } = registry ?? throw new ArgumentNullException(nameof(registry));
}

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U8, ED-NEW-inst-u8-1 and -2). The state and the actions
/// behind the two rows of Settings > General > "On this PC": "Desktop shortcut" and "Open at sign-in".
/// <list type="bullet">
/// <item><b>The machine is the state.</b> There is no settings.json field: the desktop switch shows whether a shortcut nth made
/// exists (<see cref="DesktopShortcutProbe"/>), the sign-in switch whether nth's <c>Run</c> value will run
/// (<see cref="SignInEntry"/>). Each property holds the <i>last known</i> answer, so a page that opens shows something at once
/// and updates in place when a scan answers; <see cref="ChangeIsExternal"/> is true for such an update so the switch moves
/// without animation (PLAN.md 8a).</item>
/// <item><b>Never on the UI thread.</b> Every shell and registry call runs on one worker, in order (a toggle, then a scan, then
/// the next toggle); the results come back to the main actor.</item>
/// <item><b>Scans:</b> <see cref="Refresh"/> runs when the page opens and when the window is activated, at most once every 2 s
/// and never two at once; a user's change always rescans.</item>
/// <item><b>A tap acts at once and the machine has the last word.</b> The switch flips (optimistic); <c>Create</c> /
/// <c>RemoveMade</c> / the registry write run; the answer is the rescan. When the machine disagrees the switch springs back and
/// the detail line says why (<see cref="DesktopShortcutNote"/>, <see cref="SignInNote"/>). While a feature has a change in
/// flight, scans do not overwrite it.</item>
/// </list>
/// This file never constructs the real shell or registry: they come from <see cref="OnThisPcPlatform"/>.
/// </summary>
public sealed class OnThisPcModel : MainActorModel
{
    public const string DesktopShortcutDetail = "nth's icon on your desktop";
    public const string SignInDetail = "Starts nth when you sign in to Windows";

    /// <summary>A shortcut named "nth" that nth did not make is on the desktop (PLAN.md 8a, the <c>name-taken</c> wording).</summary>
    public const string ShortcutNameTaken = "Another \"nth\" shortcut is already on your desktop.";

    /// <summary>Any other refusal (PLAN.md 8a).</summary>
    public const string NotAllowed = "Windows didn't allow it. Try again.";

    /// <summary>A <c>Run</c> value named "nth" that is not nth's is already set (ED-NEW-inst-u8-1: the plan gives no wording for it).</summary>
    public const string SignInNameTaken = "Another start-up entry named \"nth\" is already set.";

    /// <summary>The least time between two scans (PLAN.md 8a).</summary>
    public static readonly TimeSpan ScanGap = TimeSpan.FromSeconds(2);

    private static readonly ConditionalWeakTable<OnThisPcPlatform, OnThisPcModel> Models = new();

    private readonly OnThisPcPlatform platform;
    private readonly TimeProvider time;
    private readonly Func<Action, Task> background;
    private readonly Action<Action> post;
    private readonly Queue<Action> jobs = new();
    private bool draining;
    private Backend? backend;
    private bool desktopOn, signInOn;
    private string? desktopNote, signInNote;
    private int desktopPending, signInPending;
    private bool scanning;
    private long? lastScan;

    /// <param name="background">Starts the worker; the default is <see cref="Task.Run(Action)"/>. A test passes one that it controls.</param>
    /// <param name="post">Hands a result back to the main actor; the default is <see cref="MainActor.Post"/>.</param>
    public OnThisPcModel(OnThisPcPlatform platform, TimeProvider? time = null, Func<Action, Task>? background = null, Action<Action>? post = null)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.time = time ?? TimeProvider.System;
        this.background = background ?? (work => Task.Run(work));
        this.post = post ?? MainActor.Post;
    }

    /// <summary>The one model of a platform, so what the first scan learned is still there when the page opens again.</summary>
    public static OnThisPcModel For(OnThisPcPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return Models.GetValue(platform, static p => new OnThisPcModel(p));
    }

    public bool IsAvailable => platform.IsAvailable;

    /// <summary>The last known state of the desktop switch (a shortcut nth made exists).</summary>
    public bool DesktopShortcutOn { get => Read(desktopOn); private set => Set(ref desktopOn, value); }

    /// <summary>Why the last desktop change did not hold, or null for the normal detail line.</summary>
    public string? DesktopShortcutNote { get => Read(desktopNote); private set => Set(ref desktopNote, value); }

    /// <summary>The last known state of the sign-in switch (nth's entry will run).</summary>
    public bool SignInOn { get => Read(signInOn); private set => Set(ref signInOn, value); }

    public string? SignInNote { get => Read(signInNote); private set => Set(ref signInNote, value); }

    /// <summary>
    /// True when the latest change of a switch came from a scan (the machine changed outside nth), so the switch should move
    /// without animation. False for the user's own tap and for the spring-back after a refusal.
    /// </summary>
    public bool ChangeIsExternal { get; private set; }

    /// <summary>Scans in flight or waiting (for tests).</summary>
    public bool IsScanning => scanning;

    /// <summary>
    /// Looks at the machine again (the page opened, or the window was activated). <paramref name="pageOpened"/> also clears
    /// the detail lines' notes. A scan already running, or one that started less than <see cref="ScanGap"/> ago, is not
    /// repeated. Main actor only.
    /// </summary>
    public void Refresh(bool pageOpened = false)
    {
        if (!platform.IsAvailable) return;
        if (pageOpened)
        {
            ChangeIsExternal = false;
            DesktopShortcutNote = null;
            SignInNote = null;
        }
        if (scanning) return;
        var now = time.GetTimestamp();
        if (lastScan is { } last && time.GetElapsedTime(last, now) < ScanGap) return;
        lastScan = now;
        scanning = true;
        Enqueue(static b => b.Scan(), outcome =>
        {
            scanning = false;
            Apply(outcome);
        }, fallback: null);
    }

    /// <summary>The user switched "Desktop shortcut". The switch has flipped already; this makes it true (or springs it back).</summary>
    public void RequestDesktopShortcut(bool on)
    {
        if (!platform.IsAvailable) return;
        ChangeIsExternal = false;
        DesktopShortcutNote = null;
        DesktopShortcutOn = on;
        desktopPending++;
        Enqueue(b => b.ChangeDesktopShortcut(on), result =>
        {
            desktopPending--;
            if (desktopPending > 0) return; // a later tap is queued behind this one and has the last word
            ChangeIsExternal = false;
            DesktopShortcutNote = result.Note;
            DesktopShortcutOn = result.On;
        }, fallback: new DesktopResult(!on, NotAllowed));
    }

    /// <summary>The user switched "Open at sign-in".</summary>
    public void RequestSignIn(bool on)
    {
        if (!platform.IsAvailable) return;
        ChangeIsExternal = false;
        SignInNote = null;
        SignInOn = on;
        signInPending++;
        Enqueue(b => b.ChangeSignIn(on), result =>
        {
            signInPending--;
            if (signInPending > 0) return;
            ChangeIsExternal = false;
            SignInNote = result.Note;
            SignInOn = result.On;
        }, fallback: new SignInResult(!on, NotAllowed));
    }

    private void Apply(ScanResult? outcome)
    {
        if (outcome is null) return;
        if (desktopPending == 0 && outcome.Desktop is { Failed: false } desktop && desktop.Present != desktopOn)
        {
            ChangeIsExternal = true;
            DesktopShortcutNote = null;
            DesktopShortcutOn = desktop.Present;
        }
        if (signInPending == 0 && outcome.SignIn is { } signIn && signIn.IsOn != signInOn)
        {
            ChangeIsExternal = true;
            SignInNote = null;
            SignInOn = signIn.IsOn;
        }
    }

    // ---- the worker: one job at a time, in the order they were asked ----

    private void Enqueue<T>(Func<Backend, T> work, Action<T> done, T? fallback) where T : class
    {
        void Job()
        {
            T? result;
            try { result = work(backend ??= Backend.Create(platform)); }
            catch (Exception) { result = fallback; } // a call the platform could not make is a refusal, never a crash
            post(() => done(result!));
        }

        lock (jobs)
        {
            jobs.Enqueue(Job);
            if (draining) return;
            draining = true;
        }
        _ = background(Drain);
    }

    private void Drain()
    {
        while (true)
        {
            Action job;
            lock (jobs)
            {
                if (!jobs.TryDequeue(out job!))
                {
                    draining = false;
                    return;
                }
            }
            job();
        }
    }

    private sealed record DesktopResult(bool On, string? Note);

    private sealed record SignInResult(bool On, string? Note);

    private sealed record ScanResult(DesktopShortcutProbeResult? Desktop, SignInState? SignIn);

    /// <summary>The shell, the registry and the logic over them. Made on the worker, used only by it. It lives as long as the app: its STA thread is a background thread.</summary>
    private sealed class Backend
    {
        private readonly string root;
        private readonly DesktopShortcut shortcut;
        private readonly DesktopShortcutProbe probe;
        private readonly IUserRegistry registry;

        private Backend(string root, IShellLinks shell, IUserRegistry registry)
        {
            this.root = root;
            this.registry = registry;
            shortcut = new DesktopShortcut(shell, root);
            probe = new DesktopShortcutProbe(shell, root);
        }

        public static Backend Create(OnThisPcPlatform platform) =>
            new(platform.Root ?? throw new InvalidOperationException("Not a Velopack install."), platform.Shell(), platform.Registry());

        public ScanResult Scan()
        {
            var desktop = probe.Probe();
            SignInState? signIn = null;
            try { signIn = SignInEntry.Read(registry, root); }
            catch (Exception error) when (DesktopShortcut.IsShellFailure(error) || error is System.Security.SecurityException) { } // keep the last answer
            return new ScanResult(desktop, signIn);
        }

        public DesktopResult ChangeDesktopShortcut(bool on)
        {
            // The machine has the last word: the change is made, the desktop is looked at again, and the answer is what is there.
            // A note is set only when the machine contradicts what was asked.
            var created = on ? shortcut.Create(ShortcutScan.Full).DesktopShortcut : (DesktopShortcutOutcome?)null;
            if (!on) shortcut.RemoveMade();
            probe.Invalidate();
            var seen = probe.Probe();
            if (seen.Failed && created != DesktopShortcutOutcome.Created) return new DesktopResult(!on, NotAllowed); // cannot tell: back to what it was
            var present = seen.Present || created == DesktopShortcutOutcome.Created;
            if (present == on) return new DesktopResult(on, null);
            if (!on) return new DesktopResult(true, NotAllowed); // still there after the removal
            // asked for a shortcut and there is none: a "nth" shortcut that is not nth's is in the way, or Windows refused
            return new DesktopResult(false, created is DesktopShortcutOutcome.NameTaken or DesktopShortcutOutcome.Kept ? ShortcutNameTaken : NotAllowed);
        }

        public SignInResult ChangeSignIn(bool on)
        {
            var change = on
                ? SignInEntry.Enable(registry, root, shortcut.CurrentLaunchTarget())
                : SignInEntry.Disable(registry, root);
            var state = SignInEntry.Read(registry, root);
            if (change.Outcome == SignInChangeOutcome.Foreign) return new SignInResult(state.IsOn, SignInNameTaken);
            if (change.Outcome == SignInChangeOutcome.Failed) return new SignInResult(state.IsOn, NotAllowed);
            return new SignInResult(state.IsOn, state.IsOn == on ? null : NotAllowed);
        }
    }
}

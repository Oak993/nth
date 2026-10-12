// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6 "One stop chord, one owner", 16.2; unit AS-6; ED-NEW-dextra-as6-2). No Swift twin.
// The one stop chord of nth (Ctrl+Alt+Esc by default), shared by Tactus and the Assistant. It is built to pc-control's P5
// rules: the chord is configurable and checked by ChordValidator (not a single key, not a Windows-key chord, types no
// character on any installed layout); it is registered whenever ANY work holds a lease (a mission, a head of a mission,
// a duty run, a Tactus stream, a teach recording, a voice reply) and released when none does; a chord another app holds
// fails closed (nothing starts, the line says so, the tray Stop still works); and a press only sets flags: every
// subscriber is called at once on the chord's own thread, never waiting on a worker or the UI. Stop works without Tactus;
// Tactus later registers as a subscriber and gives its stop path here.
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Safety;

/// <summary>Where a stop came from (the strip says: "Stop sent in 4 ms (Ctrl+Alt+Esc)").</summary>
public enum StopSource { Chord, Tray, Header, Voice, Row, Program }

/// <summary>One stop, as every subscriber receives it.</summary>
public sealed record StopPress(StopSource Source, long Number);

/// <summary>
/// The thread the chord lives on and what it can ask Windows. Its <see cref="Pressed"/> event is raised on that thread. The
/// app binds <see cref="StopChordThread"/>; tests bind a fake.
/// </summary>
public interface IStopChordHost : IDisposable
{
    /// <summary>RegisterHotKey on the host's thread; false when another app holds the chord.</summary>
    bool Register(int id, PcHotkey chord);

    void Unregister(int id);

    /// <summary>Whether the chord types a printable character on any installed keyboard layout.</summary>
    bool TypesCharacter(PcHotkey chord);

    event Action<int>? Pressed;
}

/// <summary>Whether the stop chord works now.</summary>
public sealed record StopKeyStatus(bool Armed, string Chord, bool Taken, PcChordProblem Problem)
{
    /// <summary>True unless the last try to register the chord failed (another app holds it, or it is not a valid chord).</summary>
    public bool Works => !Taken && Problem == PcChordProblem.None;

    /// <summary>The line the board shows when work cannot start (12.6), or null while the key works.</summary>
    public string? Unavailable => Works ? null : ControlWords.StopKeyUnavailable(Chord);
}

public sealed class GlobalStop : IDisposable
{
    public const int StopId = 1;

    /// <summary>How often a start that waits for the key tries to register it again.</summary>
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private readonly object armGate = new();
    private readonly IStopChordHost host;
    private readonly Func<string?> chordSetting;
    private readonly TimeProvider time;
    private readonly Dictionary<long, string> leases = [];
    private readonly List<Waiter> waiters = [];
    private Subscription[] subscribers = [];
    private PcHotkey armedChord = PcHotkey.StopDefault;
    private bool armed;
    private bool taken;
    private PcChordProblem problem;
    private long lease;
    private long presses;
    private ITimer? retry;
    private bool disposed;

    private sealed class Waiter(string scope, TaskCompletionSource<IDisposable> source, CancellationTokenRegistration registration)
    {
        public string Scope { get; } = scope;
        public TaskCompletionSource<IDisposable> Source { get; } = source;
        public CancellationTokenRegistration Registration { get; set; } = registration;
    }

    private sealed class Subscription(string name, Action<StopPress> handler, GlobalStop owner) : IDisposable
    {
        public string Name { get; } = name;
        public Action<StopPress> Handler { get; } = handler;
        public void Dispose() => owner.Unsubscribe(this);
    }

    private sealed class Lease(GlobalStop owner, long id) : IDisposable
    {
        private int ended;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0) owner.Release(id);
        }
    }

    /// <param name="host">The chord's thread.</param>
    /// <param name="chordSetting">The owner's chord text from Settings ("Ctrl+Alt+Esc"); null, empty or unreadable text means the default.</param>
    /// <param name="time">The clock the retry timer runs on.</param>
    public GlobalStop(IStopChordHost host, Func<string?> chordSetting, TimeProvider? time = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.chordSetting = chordSetting ?? throw new ArgumentNullException(nameof(chordSetting));
        this.time = time ?? TimeProvider.System;
        host.Pressed += OnPressed;
    }

    /// <summary>Raised when the key becomes registered or stops working (on whatever thread changed it).</summary>
    public event Action<StopKeyStatus>? StatusChanged;

    /// <summary>A subscriber threw; the rest were still told. The text names the subscriber.</summary>
    public event Action<string>? Faulted;

    /// <summary>The chord as Settings has it now, or as it is registered while armed.</summary>
    public string Chord
    {
        get
        {
            lock (gate) return (armed ? armedChord : Parse(chordSetting())).Format();
        }
    }

    public StopKeyStatus Status
    {
        get
        {
            lock (gate) return new StopKeyStatus(armed, (armed ? armedChord : Parse(chordSetting())).Format(), taken, problem);
        }
    }

    /// <summary>Whether the chord is registered now (some work holds a lease and the chord works).</summary>
    public bool Armed
    {
        get
        {
            lock (gate) return armed;
        }
    }

    /// <summary>The scopes holding a lease now (for the tests and the board).</summary>
    public IReadOnlyList<string> Scopes
    {
        get
        {
            lock (gate) return [.. leases.Values];
        }
    }

    /// <summary>
    /// Takes a lease for one piece of work: the chord is registered while any lease lives. False means the chord cannot be
    /// registered (another app holds it, or it is not a valid chord): the work must not start (fail closed, 12.6).
    /// </summary>
    public bool TryAcquire(string scope, out IDisposable? held)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        held = null;
        lock (armGate)
        {
            if (disposed || !Arm()) return false;
            lock (gate)
            {
                var id = ++lease;
                leases[id] = scope;
                held = new Lease(this, id);
            }
            return true;
        }
    }

    /// <summary>
    /// Waits until a lease can be taken: at once when the key works, else when it registers (the owner changed the chord,
    /// the other app let go), tried again every <see cref="RetryEvery"/>. Cancelling stops the wait.
    /// </summary>
    public Task<IDisposable> AcquireAsync(string scope, CancellationToken cancellationToken = default)
    {
        if (TryAcquire(scope, out var now)) return Task.FromResult(now!);
        var source = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = new Waiter(scope, source, default);
        lock (gate)
        {
            waiters.Add(waiter);
            retry ??= time.CreateTimer(_ => RetryWaiters(), null, RetryEvery, RetryEvery);
        }
        if (cancellationToken.CanBeCanceled)
            waiter.Registration = cancellationToken.Register(() =>
            {
                bool removed;
                lock (gate)
                {
                    removed = waiters.Remove(waiter);
                    StopRetryIfIdle();
                }
                if (removed) source.TrySetCanceled(cancellationToken);
            });
        return source.Task;
    }

    /// <summary>Looks again at the waiting starts now (Settings changed the chord, the owner tapped Change).</summary>
    public void RetryWaiters()
    {
        List<Waiter> pending;
        lock (gate) pending = [.. waiters];
        foreach (var waiter in pending)
        {
            if (!TryAcquire(waiter.Scope, out var got)) break;
            bool still;
            lock (gate)
            {
                still = waiters.Remove(waiter);
                StopRetryIfIdle();
            }
            waiter.Registration.Dispose();
            if (!still || !waiter.Source.TrySetResult(got!)) got!.Dispose();
        }
    }

    /// <summary>
    /// Subscribes to every stop: each subscriber stops its own scope (Tactus its streams, the Assistant its work, voice its
    /// speech). The handler is called on the chord's own thread and must only set flags.
    /// </summary>
    public IDisposable Subscribe(string name, Action<StopPress> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(name, handler, this);
        lock (gate) subscribers = [.. subscribers, subscription];
        return subscription;
    }

    /// <summary>
    /// A stop from anywhere (the chord's press, the tray, a button): every subscriber is called, one at a time, in the order
    /// they subscribed. One that throws is reported through <see cref="Faulted"/> and the rest are still told.
    /// </summary>
    public StopPress Press(StopSource source)
    {
        var press = new StopPress(source, Interlocked.Increment(ref presses));
        Subscription[] now;
        lock (gate) now = subscribers;
        foreach (var subscription in now)
        {
            try { subscription.Handler(press); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Faulted?.Invoke($"{subscription.Name} could not stop: {error.Message}");
            }
        }
        return press;
    }

    public void Dispose()
    {
        lock (armGate)
        {
            if (disposed) return;
            disposed = true;
            host.Pressed -= OnPressed;
            bool wasArmed;
            lock (gate)
            {
                wasArmed = armed;
                armed = false;
                leases.Clear();
                subscribers = [];
                retry?.Dispose();
                retry = null;
            }
            if (wasArmed) host.Unregister(StopId);
        }
        List<Waiter> left;
        lock (gate)
        {
            left = [.. waiters];
            waiters.Clear();
        }
        foreach (var waiter in left)
        {
            waiter.Registration.Dispose();
            waiter.Source.TrySetCanceled();
        }
    }

    private void OnPressed(int id)
    {
        if (id == StopId) Press(StopSource.Chord);
    }

    // Registers the chord when nothing holds it yet (under armGate: a host call never waits under gate).
    private bool Arm()
    {
        lock (gate)
        {
            if (armed) return true;
        }
        var chord = Parse(chordSetting());
        var check = ChordValidator.Check(chord, [], host.TypesCharacter);
        var registered = check == PcChordProblem.None && host.Register(StopId, chord);
        StopKeyStatus status;
        lock (gate)
        {
            armed = registered;
            if (registered) armedChord = chord;
            taken = check == PcChordProblem.None && !registered;
            problem = check;
            status = new StopKeyStatus(armed, chord.Format(), taken, problem);
        }
        StatusChanged?.Invoke(status);
        return registered;
    }

    private void Release(long id)
    {
        lock (armGate)
        {
            bool last;
            lock (gate) last = leases.Remove(id) && leases.Count == 0;
            if (!last) return;
            lock (gate)
            {
                if (!armed) return;
                armed = false;
            }
            host.Unregister(StopId);
        }
        StatusChanged?.Invoke(Status);
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (gate) subscribers = [.. subscribers.Where(s => !ReferenceEquals(s, subscription))];
    }

    private void StopRetryIfIdle()
    {
        if (waiters.Count > 0) return;
        retry?.Dispose();
        retry = null;
    }

    private static PcHotkey Parse(string? text) => PcHotkey.TryParse(text, out var chord) ? chord : PcHotkey.StopDefault;
}

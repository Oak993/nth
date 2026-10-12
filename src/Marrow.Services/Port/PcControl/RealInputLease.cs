// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 "Borrow sequence" and "Watchdog", 10.1, 10.2;
// critiques C3, C4, C9, C10, C11, C17). The one shared real mouse and keyboard. Borrows queue first come, first served
// (one waiting borrow per stream); the head waits for the gate (PcInputGate, with the unseen-input tick and nth's own
// card); then the lease saves the user's state, holds their input (swallowing first, then re-verifying that nothing
// arrived in the gap, else it lets go, replays and waits again), brings the target in front by the proven way, checks
// that the target holds the foreground (and focus) before any key goes out, sends each array under a fresh tag that only
// this borrow's ledger accepts, and puts everything back: every up it owes, the user's window and control (3 tries in
// 300 ms), the target's Z-order and placement, the cursor plus the user's own held movement. The held input is replayed
// with a separate tag into the user's window while it is the one in front; keys that cannot go back are kept for "Put
// them back", and after a failed restore the user's keys stay held until the user's own click moves the foreground. A
// watchdog owns the release path: past the hold cap plus 100 ms it lets go of everything and marks the borrow failed.
// Esc in a borrow stops that stream. A cursor-free step in an app the user works in waits for a still moment through the
// same gate, words and answers, holding nothing (ED-NEW-pcctl-19). Tests drive it on fakes; the app binds the Win32 host
// and the hooks.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using NativeKind = Marrow.Services.Port.PcControl.Native.PcInputKind;

namespace Marrow.Services.Port.PcControl;

public sealed class RealInputLease
{
    public const int ClickCapMs = 150;
    public const int DragCapMs = 600;
    public const int WatchdogSlackMs = 100;
    public const int PollMs = 25;
    public const int UnhealthyWaitMs = 2_000;
    public const int RestoreTries = 3;
    public const int RestoreGapMs = 100;
    public const int CatchUpMs = 50;
    public const int ReplayChunk = 16;
    public const int KeyHoldPollMs = 50;

    private readonly Lock gate = new();
    private readonly List<Entry> queue = [];
    private readonly List<Entry> stills = [];
    private readonly AsyncLocal<Action<int>?> actionSent = new();
    private readonly AsyncLocal<Func<bool>?> actionPermission = new();
    private readonly AsyncLocal<Func<long, CancellationToken, Task<PcUiaOutcome>>?> actionValidation = new();
    private readonly AsyncLocal<Func<PcBorrowRequest, PcBorrowRequest>?> actionRefresh = new();
    private readonly Dictionary<(nint Window, nint Focus), List<PcRawInput>> kept = [];
    private readonly IPhysicalInput input;
    private readonly IPcInputHold hold;
    private readonly IRealInputHost host;
    private readonly IPcClock clock;
    private readonly PcCapabilities capabilities;
    private readonly Func<bool> ownCardUp;
    private readonly PcRunControl? control;
    private int holdingKeys;

    public RealInputLease(IPhysicalInput input, IPcInputHold hold, IRealInputHost host, IPcClock clock, PcCapabilities capabilities, Func<bool>? ownCardUp = null,
        PcRunControl? control = null)
    {
        this.control = control;
        this.input = input;
        this.hold = hold;
        this.host = host;
        this.clock = clock;
        this.capabilities = capabilities;
        this.ownCardUp = ownCardUp ?? (() => false);
    }

    /// <summary>A borrow started or changed its wait (the pill's "Hands off for a moment").</summary>
    public event Action<PcBorrowWait>? Waiting;

    /// <summary>A borrow's wait ended (it ran, was declined, or its call ended).</summary>
    public event Action<PcBorrowWait>? WaitEnded;

    /// <summary>A borrow ran at once, with no wait: the pill's 1 s chip ("Used the mouse for one click in Paint").</summary>
    public event Action<string, string>? Used;

    /// <summary>Esc pressed while a borrow held the user's input: that stream stops (DESIGN.md 4.4).</summary>
    public event Action<string>? EscapeInBorrow;

    /// <summary>The kept keys changed: their count (the pill's "Tactus kept 6 keys you typed. Put them back").</summary>
    public event Action<int>? KeptChanged;

    /// <summary>The user's keys are held after a failed restore (the pill's "Click where you were working to keep typing").</summary>
    public event Action<bool>? HoldingKeysChanged;

    public IPcClock Clock => clock;

    public IRealInputHost Host => host;

    public PcCapabilities Capabilities => capabilities;

    public int KeptKeys
    {
        get
        {
            lock (gate) return kept.Values.Sum(keys => keys.Count(k => k.Kind == PcRawKind.Key && k.Down));
        }
    }

    public bool HoldingKeys => Volatile.Read(ref holdingKeys) == 1;

    /// <summary>The borrows waiting now, in queue order, then the steps waiting for a still moment.</summary>
    public IReadOnlyList<PcBorrowWait> Waits
    {
        get
        {
            lock (gate) return queue.Concat(stills).Select(e => e.Wait).ToList();
        }
    }

    /// <summary>
    /// Whether a key-down (its code and the modifiers physically down) is one of Tactus's chords: the chords act from the
    /// hook while a borrow swallows them (DESIGN.md 4.4), so they are not replayed into the user's window as well.
    /// </summary>
    public Func<ushort, int, bool>? Chord { get; set; }

    /// <summary>Null when a borrow may run for this exe and kind; else the needs_real_input reason.</summary>
    public string? Unproven(string exe, PcBorrowKind kind) => PcBorrowProofs.Unproven(capabilities, exe, kind);

    /// <summary>"Put them back": replays the kept keys once the window they were typed in is in front again.</summary>
    public bool PutBack()
    {
        List<PcRawInput> keys;
        (nint Window, nint Focus) origin;
        lock (gate)
        {
            if (kept.Count == 0 || HoldingKeys || queue.Any(e => e.Running)) return false;
            origin = host.Focus();
            if (!kept.Remove(origin, out keys!)) return false;
        }
        Replay(keys, origin.Window, origin.Focus, out var left);
        // Replay alone owns retaining any unsent keys; left only reports completion.
        KeptChanged?.Invoke(KeptKeys);
        return left.Count == 0;
    }

    /// <summary>Drops the kept keys unsent (the pill's notice closed).</summary>
    public void Forget()
    {
        lock (gate)
        {
            foreach (var keys in kept.Values)
                for (var i = 0; i < keys.Count; i++) keys[i] = default;
            kept.Clear();
        }
        KeptChanged?.Invoke(0);
    }

    // The engine's grant follows its async action into R4 without changing the shared request/assembly seams.
    internal async Task<PcUiaOutcome> RunWithPermission(Func<bool>? permitted, Func<Task<PcUiaOutcome>> run,
        Func<long, CancellationToken, Task<PcUiaOutcome>>? validate = null, Func<PcBorrowRequest, PcBorrowRequest>? refresh = null, Action<int>? sent = null)
    {
        var previousSent = actionSent.Value;
        actionSent.Value = sent;
        var previous = actionPermission.Value;
        var previousValidation = actionValidation.Value;
        var previousRefresh = actionRefresh.Value;
        actionPermission.Value = permitted;
        actionValidation.Value = validate;
        actionRefresh.Value = refresh;
        try { return await run().ConfigureAwait(false); }
        finally { actionSent.Value = previousSent; actionPermission.Value = previous; actionValidation.Value = previousValidation; actionRefresh.Value = previousRefresh; }
    }

    public async Task<PcBorrowOutcome> Borrow(PcBorrowRequest request, CancellationToken cancellationToken, Func<bool>? permitted = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validate = actionValidation.Value;
        if (actionPermission.Value is { } grant)
        {
            var realInput = permitted;
            permitted = () => grant() && realInput?.Invoke() != false;
        }
        if (permitted?.Invoke() == false) return RealInputOff();
        if (Unproven(request.Exe, request.Kind) is { } unproven) return new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, unproven);
        if (request.Chunks.Count == 0 || request.Chunks.Any(c => c.Count == 0)) throw new ArgumentException("a borrow sends at least one event in each array", nameof(request));
        var entry = new Entry(new PcBorrowWait(request.Stream, request.Detail));
        lock (gate)
        {
            if (queue.Any(e => e.Wait.Stream == request.Stream))
                return new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, "this app already waits for the real mouse or keyboard");
            queue.Add(entry);
        }
        IDisposable? mark = null;
        var waited = false;
        var queuedAt = clock.NowMs;
        try
        {
            while (true)
            {
                var opened = await Gate(entry, still: false, cancellationToken, () =>
                {
                    waited = true;
                    mark ??= request.Waiting?.Invoke(PcWaitReason.HandsOff);
                }, permitted).ConfigureAwait(false);
                if (opened.Outcome is { } refused) return refused;
                var outcome = await Run(entry, request, opened.Delivered, opened.Tick, cancellationToken, permitted, validate, waited ? clock.NowMs - queuedAt : -1).ConfigureAwait(false);
                if (outcome is null) continue;
                if (outcome.Done && !waited && request.Detail.Length > 0) Used?.Invoke(request.Stream, request.Detail);
                return outcome;
            }
        }
        finally
        {
            mark?.Dispose();
            bool shown;
            lock (gate)
            {
                queue.Remove(entry);
                shown = entry.Shown;
            }
            if (shown) WaitEnded?.Invoke(entry.Wait);
        }
    }

    /// <summary>
    /// A still moment for a cursor-free step in an app the user is working in (DESIGN.md 2.9 "Shared app state"): the
    /// borrow's gate, words and answers, with nothing held, no queue, and no protected-window or own-card wait (the step
    /// takes neither the pointer nor the keyboard). Done once the user is still (or chose "Do it now"); Deferred when they
    /// chose "Not now". While the hooks are unhealthy it keeps waiting: the step's 25 s rule ends a long wait.
    /// </summary>
    public async Task<PcBorrowStatus> Still(string stream, string detail, Func<string, IDisposable>? waiting, CancellationToken cancellationToken)
    {
        var entry = new Entry(new PcBorrowWait(stream, detail));
        lock (gate) stills.Add(entry);
        IDisposable? mark = null;
        try
        {
            var opened = await Gate(entry, still: true, cancellationToken, () => mark ??= waiting?.Invoke(PcWaitReason.HandsOff)).ConfigureAwait(false);
            return opened.Outcome?.Status ?? PcBorrowStatus.Done;
        }
        finally
        {
            mark?.Dispose();
            bool shown;
            lock (gate)
            {
                stills.Remove(entry);
                shown = entry.Shown;
            }
            if (shown) WaitEnded?.Invoke(entry.Wait);
        }
    }

    // The head of the queue waits for the gate (DESIGN.md 2.7 step 2). Returns the hook's delivered count and the input tick
    // read with the check that opened it, or the outcome that ends the borrow. A still wait is always its own head.
    private async Task<(PcBorrowOutcome? Outcome, long Delivered, long? Tick)> Gate(Entry entry, bool still, CancellationToken cancellationToken, Action onWait, Func<bool>? permitted = null)
    {
        long? unhealthySince = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!still && permitted?.Invoke() == false) return (RealInputOff(), 0, null);
            var now = clock.NowMs;
            var delivered = hold.Delivered;
            var tick = host.LastInputMs();
            if (tick is { } seen) input.LastInput(seen);
            var protection = still ? PcForegroundProtection.None : input.Protection();
            PcGateCheck check;
            bool head;
            lock (gate) head = still || (queue.FirstOrDefault(e => !e.Running) == entry && !queue.Any(e => e.Running));
            if (!still && (ownCardUp() || HoldingKeys)) check = new PcGateCheck(PcGateBlock.Held);
            else check = PcInputGate.Check(input.Snapshot(), now, protection, entry.Wait.DoItNowChosen);
            if (entry.Wait.NotNowChosen)
            {
                return check.Block == PcGateBlock.Protected
                    ? (new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, PcRealInputReason.ForegroundProtected), 0, null)
                    : (new PcBorrowOutcome(PcBorrowStatus.Deferred, "the user chose Not now"), 0, null);
            }
            if (head && check.Open)
            {
                lock (gate) entry.Running = !still;
                return (null, delivered, tick);
            }
            if (check.Block == PcGateBlock.Unhealthy && !still)
            {
                unhealthySince ??= now;
                if (now - unhealthySince.Value >= UnhealthyWaitMs) return (new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, PcRealInputReason.MonitorUnhealthy), 0, null);
            }
            else
            {
                unhealthySince = null;
            }
            var wait = entry.Wait;
            var changed = !entry.Shown || wait.Check != check || wait.Queued != !head;
            wait.Check = check;
            wait.Queued = !head;
            lock (gate) entry.Shown = true;
            onWait();
            if (changed) Waiting?.Invoke(wait);
            await clock.Delay(PollMs, cancellationToken).ConfigureAwait(false);
        }
    }

    // Steps 3 to 7. Null: something arrived between the gate and the hold, so the borrow let go and waits again.
    private async Task<PcBorrowOutcome?> Run(Entry entry, PcBorrowRequest request, long deliveredAtGate, long? tickAtGate, CancellationToken cancellationToken, Func<bool>? permitted,
        Func<long, CancellationToken, Task<PcUiaOutcome>>? validate, long waitedMs)
    {
        if (validate is not null)
        {
            var checkedTarget = await validate(waitedMs, cancellationToken).ConfigureAwait(false);
            if (!checkedTarget.Done)
                return new PcBorrowOutcome(PcBorrowStatus.NotSent, checkedTarget.Reason) { Code = checkedTarget.Error };
            // ED-NEW-pcctl-cx-11: resolve before holding input, then use the newly resolved focus/point.
            if (actionRefresh.Value is { } refresh) request = refresh(request);
            // The cold read held no input. If its wait reopened a safety gate, queue and validate again.
            if (ownCardUp() || HoldingKeys || !PcInputGate.Check(input.Snapshot(), clock.NowMs, input.Protection(), entry.Wait.DoItNowChosen).Open)
            {
                lock (gate) entry.Running = false;
                return null;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (permitted?.Invoke() == false) return RealInputOff();
        var borrow = new Loan(request, host.Save(request.Target), permitted);
        var escaped = 0;
        using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var unhealthy = new TaskCompletionSource<PcBorrowOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        void HealthChanged(bool healthy)
        {
            if (healthy) return;
            borrow.LoseMonitor();
            if (!borrow.TryFinish()) return;
            _ = actionCancellation.CancelAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    var restored = await Release(borrow).ConfigureAwait(false);
                    unhealthy.TrySetResult(MonitorUnhealthy() with { KeptKeys = restored.KeptKeys });
                }
                catch (Exception exception) { unhealthy.TrySetException(exception); }
            });
        }
        var chord = Chord;
        var dropped = new HashSet<ushort>();
        void OnSwallowed(PcRawInput raw)
        {
            if (raw.Kind == PcRawKind.Key && raw.Code == PhysicalInputMonitor.Escape)
            {
                // Esc in a borrow stops that stream; the Esc itself is not replayed.
                if (raw.Down && Interlocked.Exchange(ref escaped, 1) == 0)
                {
                    EscapeInBorrow?.Invoke(request.Stream);
                    // Off the hook reader's thread: the stop cancels this borrow, whose release waits on the reader.
                    if (control is { } run) _ = Task.Run(() => run.Stop(PcScope.App(request.Stream), PcStopSource.Escape));
                }
                borrow.Held.Drop();
                return;
            }
            if (raw.Kind == PcRawKind.Key && raw.Code != 0)
            {
                // A Tactus chord acts from the hook; its key (down, repeats and up) is not replayed. Its modifiers are.
                lock (dropped)
                {
                    if (raw.Down && chord?.Invoke(raw.Code, raw.Modifiers) == true) dropped.Add(raw.Code);
                    if (dropped.Contains(raw.Code))
                    {
                        if (!raw.Down) dropped.Remove(raw.Code);
                        borrow.Held.Drop();
                        return;
                    }
                }
            }
            borrow.Held.Add(raw);
        }
        input.Swallowed += OnSwallowed;
        borrow.SwallowedBase = hold.SwallowedCount;
        hold.BeginSwallow();
        try
        {
            if (Gap(borrow, deliveredAtGate, tickAtGate))
            {
                await CatchUp(borrow).ConfigureAwait(false);
                MoveCursor(borrow, always: false);
                await LetGo(borrow).ConfigureAwait(false);
                lock (gate) entry.Running = false;
                return null;
            }
            var activation = PcBorrowProofs.Activation(capabilities, request.Exe, request.Kind)!.Value;
            List<PcInputEvent> caption = [];
            if (activation == PcActivation.Caption)
            {
                if (host.Caption(request.Target) is not { } point)
                    return await NotSent(borrow, "the window has no caption on screen to bring it in front").ConfigureAwait(false);
                caption = PcInputPlan.Click(point, host.Desktop(), PcMouseButton.Left, 1, [], host.ScanCode);
            }
            borrow.Tag = PcInjectionLedger.NewTag();
            borrow.Ledger = new PcInjectionLedger(borrow.Tag, clock.NowMs, PcBorrowEvents.Ledger([caption, .. request.Chunks]));
            input.Open(borrow.Ledger);
            using var watchdog = new CancellationTokenSource();
            var dog = Watchdog(borrow, watchdog.Token);
            input.HealthChanged += HealthChanged;
            var act = Act(borrow, activation, caption, actionCancellation.Token);
            // Stop never waits on a worker: when the watchdog lets go first (a hung focus call), the worker is abandoned.
            var winner = await Task.WhenAny(act, dog, unhealthy.Task).ConfigureAwait(false);
            if (winner == unhealthy.Task)
            {
                await watchdog.CancelAsync().ConfigureAwait(false);
                _ = act.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return await unhealthy.Task.ConfigureAwait(false);
            }
            if (winner == dog && borrow.WatchdogOutcome is { } timedOut)
            {
                _ = act.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return timedOut;
            }
            PcBorrowOutcome outcome;
            try
            {
                outcome = await act.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!borrow.TryFinish())
                {
                    await watchdog.CancelAsync().ConfigureAwait(false);
                    await dog.ConfigureAwait(false);
                    if (borrow.WatchdogOutcome is null)
                    {
                        var ended = await unhealthy.Task.ConfigureAwait(false);
                        if (!cancellationToken.IsCancellationRequested) return ended;
                    }
                    throw;
                }
                await watchdog.CancelAsync().ConfigureAwait(false);
                await Release(borrow).ConfigureAwait(false);
                throw;
            }
            if (!borrow.TryFinish())
            {
                // The watchdog let go first: this result is abandoned.
                await watchdog.CancelAsync().ConfigureAwait(false);
                await dog.ConfigureAwait(false);
                return borrow.WatchdogOutcome ?? await unhealthy.Task.ConfigureAwait(false);
            }
            await watchdog.CancelAsync().ConfigureAwait(false);
            var restored = await Release(borrow).ConfigureAwait(false);
            return outcome.Status == PcBorrowStatus.Done
                ? outcome with { KeptKeys = restored.KeptKeys, ForegroundRestored = restored.ForegroundRestored, ZOrderRestored = restored.ZOrderRestored }
                : outcome with { KeptKeys = restored.KeptKeys };
        }
        finally
        {
            input.HealthChanged -= HealthChanged;
            input.Swallowed -= OnSwallowed;
            borrow.Held.Zero();
        }
    }

    // The re-verify right after swallowing went on (step 4): any input in the gap means let go and wait again.
    private bool Gap(Loan borrow, long deliveredAtGate, long? tickAtGate)
    {
        if (hold.Delivered != deliveredAtGate || hold.AbortRequested || host.Held()) return true;
        if (host.LastInputMs() is { } tick && tickAtGate is { } before && tick > before) return true;
        if (input.Snapshot().Composition != 0) return true;
        return host.Focus().Foreground != borrow.Saved.Foreground;
    }

    // Step 5: bring the target in front by the proven way, check it before any key, send each array whole.
    private async Task<PcBorrowOutcome> Act(Loan borrow, PcActivation activation, List<PcInputEvent> caption, CancellationToken cancellationToken)
    {
        var request = borrow.Request;
        if (borrow.Permitted?.Invoke() == false) return RealInputOff();
        if (borrow.MonitorLost || !input.Snapshot().Healthy) return MonitorUnhealthy();
        if (request.Kind == PcBorrowKind.Pointer)
        {
            if (request.Point is { } point && host.WindowAt(point) != request.Target)
            {
                host.Raise(request.Target);
                if (host.WindowAt(point) != request.Target) return new PcBorrowOutcome(PcBorrowStatus.NotSent, "the point is covered by another window") { Code = PcErrorCode.OccludedPoint };
            }
        }
        else
        {
            if (activation == PcActivation.Caption)
            {
                if (!Send(borrow, caption)) return Failed(borrow);
            }
            else
            {
                host.SetForeground(request.Target);
            }
            if (request.Focus is { } focus && !await focus(cancellationToken).ConfigureAwait(false))
                return new PcBorrowOutcome(PcBorrowStatus.NotSent, "the control could not take the focus");
            var (front, focused) = host.Focus();
            if (front != request.Target) return new PcBorrowOutcome(PcBorrowStatus.NotSent, "the window did not come in front; nothing was typed");
            if (request.FocusControl != 0 && focused != request.FocusControl)
                return new PcBorrowOutcome(PcBorrowStatus.NotSent, "the control did not take the focus; nothing was typed");
        }
        for (var i = 0; i < request.Chunks.Count; i++)
        {
            if (borrow.Permitted?.Invoke() == false) return RealInputOff();
            if (borrow.MonitorLost || !input.Snapshot().Healthy) return MonitorUnhealthy();
            if (i > 0 && i - 1 < request.GapsMs.Count && request.GapsMs[i - 1] > 0)
                await clock.Delay(request.GapsMs[i - 1], cancellationToken).ConfigureAwait(false);
            if (hold.AbortRequested) return new PcBorrowOutcome(PcBorrowStatus.Failed, "a key held before the borrow repeated; Tactus let go");
            if (request.Kind == PcBorrowKind.Focus && host.Focus().Foreground != request.Target)
                return new PcBorrowOutcome(i == 0 ? PcBorrowStatus.NotSent : PcBorrowStatus.Failed, "the window left the foreground while Tactus typed");
            if (!Send(borrow, request.Chunks[i])) return Failed(borrow);
        }
        return new PcBorrowOutcome(PcBorrowStatus.Done);
    }

    private static PcBorrowOutcome RealInputOff() => new(PcBorrowStatus.NeedsRealInput, PcRealInputReason.Off);

    private static PcBorrowOutcome MonitorUnhealthy() => new(PcBorrowStatus.NeedsRealInput, PcRealInputReason.MonitorUnhealthy);

    private PcBorrowOutcome Failed(Loan borrow) => borrow.Permitted?.Invoke() == false ? RealInputOff()
        : borrow.MonitorLost || !input.Snapshot().Healthy ? MonitorUnhealthy() : borrow.Finished
        ? new PcBorrowOutcome(PcBorrowStatus.Failed, "the borrow ran past its hold cap; Tactus let go") { Code = PcErrorCode.Timeout }
        : new PcBorrowOutcome(PcBorrowStatus.Failed, "Windows did not take all of the input");

    // One array, under the send lock, only while the borrow is not finished; the ups it owes are tracked before it goes.
    private bool Send(Loan borrow, IReadOnlyList<PcInputEvent> events)
    {
        lock (borrow.SendGate)
        {
            if (borrow.Finished || borrow.Permitted?.Invoke() == false || borrow.MonitorLost || !input.Snapshot().Healthy) return false;
            borrow.Pressed.Track(events);
            var accepted = host.Send(events, borrow.Tag);
            if (accepted > 0) actionSent.Value?.Invoke(events.Take(accepted).Count(e => e.Kind == NativeKind.Key
                && (e.Flags & PcInputPlan.KEY_UNICODE) != 0 && (e.Flags & PcInputPlan.KEY_UP) == 0));
            return accepted == events.Count;
        }
    }

    private async Task Watchdog(Loan borrow, CancellationToken cancellationToken)
    {
        try
        {
            await clock.Delay(borrow.Request.HoldCapMs + WatchdogSlackMs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!borrow.TryFinish()) return;
        var restored = await Release(borrow).ConfigureAwait(false);
        borrow.WatchdogOutcome = new PcBorrowOutcome(PcBorrowStatus.Failed, "the borrow ran past its hold cap; Tactus let go")
        {
            Code = PcErrorCode.Timeout,
            KeptKeys = restored.KeptKeys,
        };
    }

    private async Task<PcBorrowOutcome> NotSent(Loan borrow, string reason)
    {
        borrow.TryFinish();
        var restored = await Release(borrow).ConfigureAwait(false);
        return new PcBorrowOutcome(PcBorrowStatus.NotSent, reason) { KeptKeys = restored.KeptKeys };
    }

    // Steps 6 and 7, run once by whoever finished the borrow (the worker or the watchdog).
    private async Task<PcBorrowOutcome> Release(Loan borrow)
    {
        var request = borrow.Request;
        var saved = borrow.Saved;
        var ups = borrow.Pressed.TakeUps();
        if (ups.Count > 0)
        {
            borrow.Ledger?.Expect(ups.SelectMany(PcBorrowEvents.Ledger));
            host.Send(ups, borrow.Tag);
        }
        var foreground = true;
        if (host.Focus().Foreground != saved.Foreground)
        {
            foreground = false;
            for (var attempt = 0; attempt < RestoreTries && !foreground; attempt++)
            {
                if (attempt > 0) await clock.Delay(RestoreGapMs, CancellationToken.None).ConfigureAwait(false);
                host.Restore(saved.Foreground, saved.Focus);
                foreground = host.Focus().Foreground == saved.Foreground;
            }
        }
        var zorder = host.RestoreZOrder(request.Target, saved);
        if (saved.Minimized) host.RestorePlacement(request.Target, saved);
        if (borrow.Ledger is { } ledger) input.Close(ledger);
        await CatchUp(borrow).ConfigureAwait(false);
        MoveCursor(borrow, always: true);
        var keptBefore = KeptKeys;
        if (foreground)
        {
            await LetGo(borrow).ConfigureAwait(false);
        }
        else
        {
            // The target still holds the foreground: the user's keys stay held (the mouse is free) until their own click
            // moves the foreground; every held key joins the kept keys and never reaches the target.
            var held = borrow.Held.Take();
            Keep(held, saved.Foreground, saved.Focus);
            SendReplay(held.Where(e => e.Kind != PcRawKind.Key).ToList(), PcInjectionLedger.NewTag());
            hold.BeginSwallow(keysOnly: true);
            Volatile.Write(ref holdingKeys, 1);
            HoldingKeysChanged?.Invoke(true);
            _ = HoldKeys(borrow, request.Target);
        }
        return new PcBorrowOutcome(PcBorrowStatus.Done) { ForegroundRestored = foreground, ZOrderRestored = zorder, KeptKeys = KeptKeys - keptBefore };
    }

    // Waits (briefly) until the reader has handed on every event the hook swallowed for this borrow.
    private async Task CatchUp(Loan borrow)
    {
        for (var waited = 0; borrow.Held.Received < hold.SwallowedCount - borrow.SwallowedBase && waited < CatchUpMs; waited += 5)
            await clock.Delay(5, CancellationToken.None).ConfigureAwait(false);
    }

    // The cursor to where the user's own held movement takes it from the saved point (or from where it was last put),
    // clamped to a screen that still exists. A borrow that moved the cursor always puts it back.
    private void MoveCursor(Loan borrow, bool always)
    {
        var delta = borrow.Held.TakeDelta();
        if (!always && delta == default) return;
        borrow.Cursor = host.Clamp(new PcPoint(borrow.Cursor.X + delta.X, borrow.Cursor.Y + delta.Y));
        host.SetCursor(borrow.Cursor);
    }

    // Replays what was held, turns swallowing off, then replays whatever the hook held in between (step 7).
    private async Task LetGo(Loan borrow)
    {
        var window = borrow.Saved.Foreground;
        Replay(borrow.Held.Take(), window, borrow.Saved.Focus, out _);
        hold.EndSwallow();
        await CatchUp(borrow).ConfigureAwait(false);
        MoveCursor(borrow, always: false);
        Replay(borrow.Held.Take(), window, borrow.Saved.Focus, out _);
    }

    // Replays held events into the user's window in chunks, checking before each that it is still the one in front with
    // the same focus; keys that cannot go back are kept. Mouse events go back where the user's cursor is.
    private void Replay(List<PcRawInput> held, nint window, nint focus, out List<PcRawInput> left)
    {
        left = [];
        if (held.Count == 0) return;
        var tag = PcInjectionLedger.NewTag();
        for (var start = 0; start < held.Count; start += ReplayChunk)
        {
            var part = held.Skip(start).Take(ReplayChunk).ToList();
            var (front, focused) = host.Focus();
            if (front != window || (focus != 0 && focused != focus))
            {
                var rest = held.Skip(start).ToList();
                var keys = rest.Where(e => e.Kind == PcRawKind.Key).ToList();
                Keep(keys, window, focus);
                left = keys;
                SendReplay(rest.Where(e => e.Kind != PcRawKind.Key).ToList(), tag);
                return;
            }
            SendReplay(part, tag);
        }
    }

    private void SendReplay(List<PcRawInput> part, ulong tag)
    {
        var events = part.Select(e => PcBorrowEvents.Replay(e, host.ScanCode)).OfType<PcInputEvent>().ToList();
        if (events.Count == 0) return;
        var ledger = new PcInjectionLedger(tag, clock.NowMs, events.SelectMany(PcBorrowEvents.Ledger), replay: true);
        input.Open(ledger);
        host.Send(events, tag);
        input.Close(ledger);
    }

    private void Keep(List<PcRawInput> keys, nint window, nint focus)
    {
        var added = keys.Where(k => k.Kind == PcRawKind.Key).ToList();
        if (added.Count == 0) return;
        lock (gate)
        {
            var origin = (window, focus);
            if (!kept.TryGetValue(origin, out var pending)) kept[origin] = pending = [];
            pending.AddRange(added);
        }
        KeptChanged?.Invoke(KeptKeys);
    }

    // After a failed restore: hold the user's keys until the foreground moves off the target (their own click), or Esc.
    private async Task HoldKeys(Loan borrow, nint target)
    {
        var escaped = 0;
        void OnSwallowed(PcRawInput raw)
        {
            if (raw.Kind != PcRawKind.Key) return;
            if (raw.Code == PhysicalInputMonitor.Escape)
            {
                Volatile.Write(ref escaped, 1);
                return;
            }
            Keep([raw], borrow.Saved.Foreground, borrow.Saved.Focus);
        }
        input.Swallowed += OnSwallowed;
        try
        {
            while (Volatile.Read(ref escaped) == 0 && host.Focus().Foreground == target)
                await clock.Delay(KeyHoldPollMs, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            hold.EndSwallow();
            input.Swallowed -= OnSwallowed;
            Volatile.Write(ref holdingKeys, 0);
            HoldingKeysChanged?.Invoke(false);
        }
    }

    private sealed class Entry(PcBorrowWait wait)
    {
        public PcBorrowWait Wait { get; } = wait;

        public bool Running { get; set; }

        public bool Shown { get; set; }
    }

    private sealed class Loan(PcBorrowRequest request, PcBorrowSaved saved, Func<bool>? permitted)
    {
        private int finished;
        private int monitorLost;

        public bool MonitorLost => Volatile.Read(ref monitorLost) == 1;

        public void LoseMonitor() => Volatile.Write(ref monitorLost, 1);

        /// <summary>Where the cursor is put back to (the saved point, moved by the user's held movement).</summary>
        public PcPoint Cursor { get; set; } = saved.Cursor;

        public Func<bool>? Permitted { get; } = permitted;

        public PcBorrowRequest Request { get; } = request;

        public PcBorrowSaved Saved { get; } = saved;

        public PcHeldInput Held { get; } = new();

        public PcPressed Pressed { get; } = new();

        public Lock SendGate { get; } = new();

        public ulong Tag { get; set; }

        public PcInjectionLedger? Ledger { get; set; }

        public long SwallowedBase { get; set; }

        public PcBorrowOutcome? WatchdogOutcome { get; set; }

        public bool Finished => Volatile.Read(ref finished) == 1;

        /// <summary>The first caller (the worker or the watchdog) owns the release; under the send lock, so no array follows.</summary>
        public bool TryFinish()
        {
            lock (SendGate) return Interlocked.Exchange(ref finished, 1) == 0;
        }
    }
}

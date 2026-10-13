// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 12.6, 16.1, 16.2; unit AS-19; ED-NEW-dextra-as19-1, -2, -8). No Swift twin.
// One teach recording while it runs. It records a frame on every click and one a second beside, the element under each click, the text typed
// into a field (a password and anything the privacy patterns would change becomes "[secret]", a field nth cannot look up keeps only a
// length), the shortcuts, the owner's spoken lines from the local speech engine and the browser steps Visus gives. Nothing from a window nth
// does not record (a password manager, a private browser window, the security prompts) is kept, and nothing from nth's own windows. The
// time it counts is the time it ran, not the time it was paused; it ends itself at 60 minutes. While it runs it holds a lease on the one
// stop chord, so a recording cannot start when Ctrl+Alt+Esc is taken, and the chord and Stop all end it with what was recorded kept.
// All work happens on one task in the order it arrived; the control methods only queue.
using System.Security.Cryptography;
using System.Threading.Channels;
using Marrow.Core.Port.Assistant;
using Marrow.App.Safety;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>How a recording stands, for the strip: its state, the time it has run, the time left once it is short, and what it has kept.</summary>
public sealed record TeachRecorderStatus(TeachRecordingState State, TimeSpan Elapsed, TimeSpan Remaining, int Events, int Frames, IReadOnlyList<string> Notes)
{
    /// <summary>The strip shows the time left.</summary>
    public bool CountingDown => Remaining <= TeachLimits.CountdownFrom;
}

/// <summary>A start: the recording that began, or why none did.</summary>
public sealed record TeachStartResult(TeachRecordingInfo? Info, string? Refusal)
{
    public bool Started => Info is not null;
}

public sealed class TeachRecorderOptions
{
    public required TeachRecordings Recordings { get; init; }

    public required ITeachInput Input { get; init; }

    public required IElementProbe Probe { get; init; }

    public required IFrameSource Frames { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The one stop chord of nth (12.6). Null only where there is none (a test): then a recording needs no lease.</summary>
    public GlobalStop? StopKey { get; init; }

    /// <summary>Makes the narration for a recording from its clock, the place a heard line goes and the place a note goes. Null: no narration.</summary>
    public Func<Func<TimeSpan>, Action<TeachNarrationLine>, Action<string>, ITeachNarration>? Narration { get; init; }

    public IBrowserSteps? Browser { get; init; }

    public TeachSource Source { get; init; } = TeachSource.Direct;

    /// <summary>The frames of a click only, from this many bytes of frames (the one a second stops). Tests make it small.</summary>
    public long SoftBytes { get; init; } = TeachLimits.FrameSoftBytes;

    /// <summary>The recording ends cleanly at this many bytes of frames.</summary>
    public long HardBytes { get; init; } = TeachLimits.FrameHardBytes;

    /// <summary>The recording ends cleanly at this many events.</summary>
    public int MaxEvents { get; init; } = TeachLimits.MaxEvents;

    /// <summary>Called once, on the recorder's task, when the recording has ended and its manifest is written.</summary>
    public Action<TeachRecordingInfo>? Ended { get; init; }

    /// <summary>A store failure worth the activity log (a class name, never content).</summary>
    public Action<string>? Faulted { get; init; }
}

public sealed class TeachRecorder : IDisposable
{
    /// <summary>A second private event this close to the last is the same stretch in the same window: not another step.</summary>
    public static readonly TimeSpan PrivateStretch = TimeSpan.FromSeconds(60);

    private abstract record Work
    {
        public sealed record Input(TeachInput Value, TimeSpan At) : Work;

        public sealed record Browser(TeachBrowserStep Step, TimeSpan At) : Work;

        public sealed record Tick : Work;

        public sealed record Pause : Work;

        public sealed record Resume : Work;

        public sealed record Stop(TeachStopReason Reason, TaskCompletionSource<TeachRecordingInfo> Done) : Work;

        public sealed record Barrier(TaskCompletionSource Reached) : Work;
    }

    private readonly TeachRecorderOptions o;
    private readonly TimeProvider time;
    private readonly Channel<Work> work = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object gate = new();
    private readonly object clockGate = new();
    private readonly List<string> notes = [];
    private readonly TaskCompletionSource<TeachRecordingInfo> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TeachRecordingInfo? info;
    private ITeachNarration? narration;
    private IDisposable? lease;
    private IDisposable? stopSubscription;
    private IDisposable? browserSubscription;
    private ITimer? frameTimer;
    private ITimer? idleTimer;
    private Task? consumer;
    private bool started;
    private bool ended;
    private TeachRecordingState state = TeachRecordingState.Recording;
    private TimeSpan banked;
    private long? runningSince;
    private int events;
    private int frameCount;
    private int frameNumber;
    private long frameBytes;
    private int narrationCount;
    private byte[]? lastFrameHash;
    private TimeSpan? lastPrivateAt;
    private bool framesHeld;
    private long lastSaved;
    private TeachFocus? burstFocus;

    public TeachRecorder(TeachRecorderOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        time = o.Time;
    }

    /// <summary>The recording's id once it has started.</summary>
    public string? Id => info?.Id;

    /// <summary>Raised when the state, the counts or the notes change (on whatever thread changed them): the strip reads <see cref="Status"/>.</summary>
    public event Action<TeachRecorderStatus>? Changed;

    public TeachRecordingState State
    {
        get
        {
            lock (gate) return state;
        }
    }

    /// <summary>The time the recording has run, pauses not counted.</summary>
    public TimeSpan Elapsed => ElapsedAt(time.GetTimestamp());

    public TeachRecorderStatus Status
    {
        get
        {
            var elapsed = Elapsed;
            lock (gate) return new TeachRecorderStatus(state, elapsed, Left(elapsed), events, frameCount, [.. notes]);
        }
    }

    private static TimeSpan Left(TimeSpan elapsed) => elapsed >= TeachLimits.Recording ? TimeSpan.Zero : TeachLimits.Recording - elapsed;

    private TimeSpan ElapsedAt(long timestamp)
    {
        lock (clockGate) return banked + (runningSince is { } since ? time.GetElapsedTime(since, timestamp) : TimeSpan.Zero);
    }

    /// <summary>Another recording task, queued: all work before it has been done when it completes. For the strip's last read and for tests.</summary>
    public Task Flush()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.Writer.TryWrite(new Work.Barrier(reached))) reached.TrySetResult();
        return reached.Task;
    }

    // ---- starting ----

    /// <summary>
    /// Begins: a lease on the stop chord (none, no recording), a folder and a manifest, the hooks, the narration. A refusal says why and
    /// leaves nothing behind.
    /// </summary>
    public TeachStartResult Start()
    {
        lock (gate)
        {
            if (started) throw new InvalidOperationException("A recorder records once.");
            started = true;
        }
        var made = o.Recordings.Create(time.GetUtcNow(), o.Source);
        info = made;
        if (o.StopKey is { } key)
        {
            if (!key.TryAcquire("teach:" + made.Id, out lease))
            {
                o.Recordings.Delete(made.Id);
                return Refuse(key.Status.Unavailable ?? ControlWords.StopKeyUnavailable(key.Chord));
            }
            stopSubscription = key.Subscribe("teach:" + made.Id, press => RequestStop(press.Source == StopSource.Chord ? TeachStopReason.StopKey : TeachStopReason.StopAll));
        }
        lock (clockGate) runningSince = time.GetTimestamp();
        try
        {
            o.Input.Start(OnInput);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Release();
            o.Recordings.Delete(made.Id);
            return Refuse("Windows would not let nth watch the mouse and keyboard, so nothing was recorded.");
        }
        if (o.Narration is { } make)
        {
            narration = make(() => Elapsed, OnLine, Note);
            if (narration.Start() is { } why) Note(why);
        }
        if (o.Browser is { } browser) browserSubscription = browser.Subscribe(step => Post(new Work.Browser(step, Elapsed)));
        frameTimer = time.CreateTimer(_ => Post(new Work.Tick()), null, TeachLimits.FrameEvery, TeachLimits.FrameEvery);
        consumer = Task.Run(RunAsync);
        Persist();
        Raise();
        return new TeachStartResult(made, null);
    }

    private TeachStartResult Refuse(string reason)
    {
        lock (gate) ended = true;
        work.Writer.TryComplete();
        finished.TrySetResult(info!);
        return new TeachStartResult(null, reason);
    }

    // ---- control: all of it only queues ----

    public void Pause() => Post(new Work.Pause());

    public void Resume() => Post(new Work.Resume());

    /// <summary>Ends the recording; the task completes with the manifest once it is written. What was recorded is kept.</summary>
    public Task<TeachRecordingInfo> Stop(TeachStopReason reason = TeachStopReason.Owner)
    {
        var done = new TaskCompletionSource<TeachRecordingInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.Writer.TryWrite(new Work.Stop(reason, done))) return finished.Task;
        return done.Task;
    }

    /// <summary>For the stop chord and Stop all: a flag and a queued item, nothing that waits (12.6).</summary>
    public void RequestStop(TeachStopReason reason) => _ = Stop(reason);

    private void Post(Work item) => work.Writer.TryWrite(item);

    // The hook's thread: a time and a queue entry.
    private void OnInput(TeachInput input) => Post(new Work.Input(input, Elapsed));

    // The narrator's thread: a line goes straight to the file (the store is thread-safe), so none is lost between a stop and the last write.
    private void OnLine(TeachNarrationLine line)
    {
        try
        {
            o.Recordings.AppendNarration(info!.Id, line);
            Interlocked.Increment(ref narrationCount);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            Fault(error);
        }
    }

    private void Note(string text)
    {
        lock (gate)
        {
            if (notes.Contains(text)) return;
            notes.Add(text);
        }
        Raise();
    }

    // ---- the one task ----

    private async Task RunAsync()
    {
        await foreach (var item in work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await HandleAsync(item).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
            {
                // Disk full, folder gone: what was written is kept, and the recording ends rather than lose steps without saying so.
                Fault(error);
                if (!ended) await FinishAsync(TeachStopReason.Full, null).ConfigureAwait(false);
            }
            if (ended) break;
        }
    }

    private async Task HandleAsync(Work item)
    {
        switch (item)
        {
            case Work.Barrier barrier:
                barrier.Reached.TrySetResult();
                return;
            case Work.Stop stop:
                await FinishAsync(stop.Reason, stop.Done).ConfigureAwait(false);
                return;
            case Work.Pause:
                PauseNow();
                return;
            case Work.Resume:
                ResumeNow();
                return;
        }
        if (State != TeachRecordingState.Recording) return; // arrived after a pause was queued: heard while paused is not recorded
        switch (item)
        {
            case Work.Input input:
                Record(input.Value, input.At);
                break;
            case Work.Browser browser:
                RecordBrowser(browser.Step, browser.At);
                break;
            case Work.Tick:
                Periodic();
                break;
        }
        if (Elapsed >= TeachLimits.Recording) await FinishAsync(TeachStopReason.Cap, null).ConfigureAwait(false);
        else if (events >= o.MaxEvents || frameBytes >= o.HardBytes) await FinishAsync(TeachStopReason.Full, null).ConfigureAwait(false);
        else if (Math.Abs(time.GetElapsedTime(lastSaved).TotalSeconds) >= 15) Persist();
        Raise();
    }

    // ---- recording what the owner does ----

    private void Record(TeachInput input, TimeSpan at)
    {
        switch (input)
        {
            case TeachInput.Click click:
            {
                var element = o.Probe.At(click.X, click.Y) ?? o.Probe.Foreground();
                if (element is not null && TeachGuards.IsOwnApp(element.App)) return;
                if (TeachGuards.IsPrivate(element)) { AddPrivate(at, click.Verb); return; }
                var frame = TakeFrame(at, periodic: false);
                Add(new TeachEvent(0, at, TeachEventKind.Click) { Verb = click.Verb, Element = element, Frame = frame });
                break;
            }
            case TeachInput.TypingStarted:
                burstFocus ??= o.Probe.Focused();
                break;
            case TeachInput.Typing typing:
            {
                // The field that had the focus when the burst began, whatever has it now; read its text only now, and never a password's.
                var focus = burstFocus ?? o.Probe.Focused();
                burstFocus = null;
                var element = focus?.Element ?? o.Probe.Foreground();
                if (element is not null && TeachGuards.IsOwnApp(element.App)) return;
                if (TeachGuards.IsPrivate(element)) { AddPrivate(at, "type"); return; }
                var observed = focus is { Element.IsPassword: false } ? ReadValue(focus) : null;
                var kept = TeachTyped.Record(focus?.Element, observed, typing.Count);
                Add(new TeachEvent(0, at, TeachEventKind.Type) { Verb = "type", Element = element, Text = kept.Text, Length = kept.Length, Secret = kept.Secret });
                break;
            }
            case TeachInput.Chord chord:
            {
                var element = o.Probe.Foreground();
                if (element is not null && TeachGuards.IsOwnApp(element.App)) return;
                if (TeachGuards.IsPrivate(element)) { AddPrivate(at, "press"); return; }
                Add(new TeachEvent(0, at, TeachEventKind.Shortcut) { Verb = "press", Element = element, Text = TeachDraft.Clean(chord.Text, 40) });
                break;
            }
        }
    }

    // The text a field holds. A control that cannot answer, or hangs, is a null: the step keeps only how much was typed.
    private string? ReadValue(TeachFocus focus)
    {
        try { return focus.ReadValue(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Fault(error);
            return null;
        }
    }

    private void RecordBrowser(TeachBrowserStep step, TimeSpan at)
    {
        var verb = TeachDraft.Clean(step.Verb, 20).ToLowerInvariant();
        if (verb.Length == 0) return;
        var element = step.Element is null ? null : step.Element with { IsPassword = step.Element.IsPassword || step.IsPassword };
        if (TeachGuards.IsPrivate(element)) { AddPrivate(at, verb); return; }
        string? text = TeachDraft.Clean(step.Text, TeachLimits.TypedChars);
        var length = step.Length;
        var secret = false;
        if (verb == "type")
        {
            var kept = TeachTyped.Record(element ?? new TeachElement("browser", "", "edit", "field") { IsPassword = step.IsPassword }, step.Text, step.Length);
            (text, length, secret) = (kept.Text, kept.Length, kept.Secret);
        }
        else if (text.Length == 0)
        {
            text = null;
        }
        Add(new TeachEvent(0, at, TeachEventKind.Browser) { Verb = verb, Element = element, Text = text, Length = length, Secret = secret, Url = PageAddress(step.Url) });
    }

    /// <summary>A page address with no query and no fragment (they carry tokens and ids); only http and https; null for anything else.</summary>
    public static string? PageAddress(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return uri.GetLeftPart(UriPartial.Path);
    }

    private void AddPrivate(TimeSpan at, string verb)
    {
        if (lastPrivateAt is { } last && at - last < PrivateStretch)
        {
            lastPrivateAt = at;
            return;
        }
        lastPrivateAt = at;
        Add(new TeachEvent(0, at, TeachEventKind.Private) { Verb = verb });
    }

    private void Add(TeachEvent e)
    {
        var numbered = e with { Seq = events + 1 };
        o.Recordings.AppendEvent(info!.Id, numbered);
        events++;
        if (e.Kind != TeachEventKind.Private) lastPrivateAt = null;
    }

    private void Periodic()
    {
        if (frameBytes >= o.SoftBytes)
        {
            if (!framesHeld)
            {
                framesHeld = true;
                Note("The frames are using a lot of space, so only the ones on clicks are kept from here.");
            }
            return;
        }
        TakeFrame(Elapsed, periodic: true);
    }

    // The frame of the screen, unless the window in front is one nth does not record, or nothing can be taken now.
    private string? TakeFrame(TimeSpan at, bool periodic)
    {
        var foreground = o.Probe.Foreground();
        if (foreground is not null && (TeachGuards.IsPrivate(foreground))) return null;
        var jpeg = o.Frames.Capture();
        if (jpeg is null || jpeg.Length == 0) return null;
        if (periodic)
        {
            var hash = SHA256.HashData(jpeg);
            if (lastFrameHash is not null && hash.AsSpan().SequenceEqual(lastFrameHash)) return null;
            lastFrameHash = hash;
        }
        var name = o.Recordings.WriteFrame(info!.Id, ++frameNumber, at, jpeg);
        frameBytes += jpeg.Length;
        frameCount++;
        return name;
    }

    // ---- pause, resume, end ----

    private void PauseNow()
    {
        if (State != TeachRecordingState.Recording) return;
        lock (clockGate)
        {
            banked = ElapsedAt(time.GetTimestamp());
            runningSince = null;
        }
        KeepTail();
        lock (gate) state = TeachRecordingState.Paused;
        narration?.Pause();
        idleTimer?.Dispose();
        idleTimer = time.CreateTimer(_ => RequestStop(TeachStopReason.PausedTooLong), null, TeachLimits.PausedIdle, Timeout.InfiniteTimeSpan);
        Persist();
        Raise();
    }

    // The hooks come off, and the typing that had not ended when they did is recorded at the time it ended: not lost to a pause or a stop.
    private void KeepTail()
    {
        TeachInput? tail;
        try { tail = o.Input.Stop(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Fault(error);
            return;
        }
        if (tail is not null && State == TeachRecordingState.Recording) Record(tail, Elapsed);
        burstFocus = null;
    }

    private void ResumeNow()
    {
        if (State != TeachRecordingState.Paused) return;
        try
        {
            o.Input.Start(OnInput);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Note("Windows would not let nth watch the mouse and keyboard again, so the recording stays paused. Stop it to keep what it has.");
            return;
        }
        idleTimer?.Dispose();
        idleTimer = null;
        lock (clockGate) runningSince = time.GetTimestamp();
        lock (gate) state = TeachRecordingState.Recording;
        narration?.Resume();
        Persist();
        Raise();
    }

    private async Task FinishAsync(TeachStopReason reason, TaskCompletionSource<TeachRecordingInfo>? done)
    {
        if (ended) return;
        lock (gate) ended = true;
        lock (clockGate)
        {
            banked = ElapsedAt(time.GetTimestamp());
            runningSince = null;
        }
        frameTimer?.Dispose();
        idleTimer?.Dispose();
        try { KeepTail(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidDataException) { Fault(error); }
        browserSubscription?.Dispose();
        if (narration is not null)
        {
            try { await narration.StopAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { Fault(error); }
        }
        lock (gate) state = TeachRecordingState.Recorded;
        var final = Compose(TeachRecordingState.Recorded) with { Ended = reason };
        try { o.Recordings.SaveInfo(final); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException) { Fault(error); }
        info = final;
        Release();
        try { o.Ended?.Invoke(final); }
        catch (Exception error) when (error is not OutOfMemoryException) { Fault(error); }
        finished.TrySetResult(final);
        done?.TrySetResult(final);
        // A stop or a barrier queued behind the end is answered with it, after everything the end does is done.
        work.Writer.TryComplete();
        while (work.Reader.TryRead(out var rest))
        {
            if (rest is Work.Stop another) another.Done.TrySetResult(final);
            else if (rest is Work.Barrier barrier) barrier.Reached.TrySetResult();
        }
        Raise();
    }

    private void Release()
    {
        stopSubscription?.Dispose();
        stopSubscription = null;
        lease?.Dispose();
        lease = null;
    }

    private TeachRecordingInfo Compose(TeachRecordingState current) => info! with
    {
        State = current,
        Elapsed = ElapsedAt(time.GetTimestamp()),
        Events = events,
        Frames = frameCount,
        FrameBytes = frameBytes,
        Narration = Volatile.Read(ref narrationCount),
        Changed = time.GetUtcNow(),
    };

    private void Persist()
    {
        try
        {
            var current = Compose(State);
            o.Recordings.SaveInfo(current);
            info = current;
            lastSaved = time.GetTimestamp();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Fault(error);
        }
    }

    private void Raise()
    {
        var handler = Changed;
        if (handler is null) return;
        try { handler(Status); }
        catch (Exception error) when (error is not OutOfMemoryException) { Fault(error); }
    }

    private void Fault(Exception error) => o.Faulted?.Invoke("teach recording: " + error.GetType().Name);

    /// <summary>Ends the recording if it still runs (the app is closing): what was recorded is kept.</summary>
    public void Dispose()
    {
        bool running;
        lock (gate) running = started && !ended;
        if (running)
        {
            RequestStop(TeachStopReason.Closed);
            try { finished.Task.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { /* the recording failed to end cleanly; its files stay as written */ }
        }
        frameTimer?.Dispose();
        idleTimer?.Dispose();
    }
}

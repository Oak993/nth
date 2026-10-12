// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 "Borrow sequence", critiques C3, C9, C11, C17).
// What a borrow of the real mouse or keyboard (rung R4) is made of: the request a rung hands the lease (the stream, the
// target, the exact SendInput arrays, the hold cap), what came of it, what was saved to put back, the native seam the
// lease drives, the wait the pill shows ("Hands off for a moment", with Do it now and Not now), and the activation and
// restore ways. Since ED-NEW-pcctl-45 those are learned at runtime per target (exe, version and framework, DESIGN.md 2.6
// "Self-check"): a way not tried yet on a target may be probed, a way that worked is used, a way that failed is not used
// for 7 days, and the results are saved with the capabilities. The older per-exe proofs (or "*" for every app) stay for
// the lease's current call sites; nothing loads them from disk.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Port.PcControl;

/// <summary>A pointer step (click, wheel, drag) or a focus step (keys, text).</summary>
public enum PcBorrowKind
{
    Pointer,
    Focus,
}

/// <summary>How a borrow brings its target in front (DESIGN.md 2.7 step 5, critique C9).</summary>
public enum PcActivation
{
    /// <summary>A pointer step: no activation call; the injected click activates the window as a user's click does.</summary>
    Pointer,

    /// <summary>A focus step: an injected click on the target's own caption, then UI Automation focus on the control.</summary>
    Caption,

    /// <summary>A focus step: SetForegroundWindow from nth, then UI Automation focus on the control.</summary>
    Foreground,
}

/// <summary>
/// Which activation and restore ways work, kept in the capability store under rung R4. Per target (exe, version,
/// framework): learned by the borrows themselves and saved with the capabilities. Per exe (or "*" for every app): the
/// older form the lease still calls, which nothing loads from disk, so it answers "activation unproven" until proven in
/// memory.
/// </summary>
public static class PcBorrowProofs
{
    public const string AnyApp = "*";
    public const string Pointer = "activate_pointer";
    public const string Caption = "activate_caption";
    public const string Foreground = "activate_foreground";

    /// <summary>SetForegroundWindow back to the user's window, then UI Automation focus on the user's control.</summary>
    public const string Restore = "restore_foreground";

    public static PcCapabilityKey Key(string exe, string method) => new(exe, "", PcRung.R4, method, "");

    public static bool Proven(PcCapabilities capabilities, string exe, string method) =>
        capabilities.State(Key(exe, method)) == PcLearned.Passed || capabilities.State(Key(AnyApp, method)) == PcLearned.Passed;

    /// <summary>The proven way to bring the target in front for this kind of step, or null.</summary>
    public static PcActivation? Activation(PcCapabilities capabilities, string exe, PcBorrowKind kind)
    {
        if (kind == PcBorrowKind.Pointer) return Proven(capabilities, exe, Pointer) ? PcActivation.Pointer : null;
        if (Proven(capabilities, exe, Caption)) return PcActivation.Caption;
        return Proven(capabilities, exe, Foreground) ? PcActivation.Foreground : null;
    }

    /// <summary>Null when a borrow may run for this exe and kind; else the needs_real_input reason.</summary>
    public static string? Unproven(PcCapabilities capabilities, string exe, PcBorrowKind kind) =>
        Activation(capabilities, exe, kind) is null || !Proven(capabilities, exe, Restore) ? PcRealInputReason.ActivationUnproven : null;

    /// <summary>The method name a way is learned under.</summary>
    public static string MethodOf(PcActivation activation) => activation switch
    {
        PcActivation.Pointer => Pointer,
        PcActivation.Caption => Caption,
        _ => Foreground,
    };

    /// <summary>Whether a method is one of the borrow's learned ways (the three activations and the restore).</summary>
    public static bool IsProofMethod(string method) => method is Pointer or Caption or Foreground or Restore;

    /// <summary>One way on one target: rung R4, the target's exe and version, and its framework in the control slot.</summary>
    public static PcCapabilityKey Key(PcProofTarget target, string method) =>
        new(target.Exe, target.Version, PcRung.R4, method, target.Framework.ToString());

    /// <summary>
    /// The way a borrow on this target brings it in front: a way that worked on it, else one not tried on it yet (the
    /// self-check's probe), in DESIGN.md's order (b caption, then c SetForegroundWindow, for a focus step); null when every
    /// way is refused or the target has no identity. Never a proof from another exe, version or framework.
    /// </summary>
    public static PcActivation? Activation(PcCapabilities capabilities, PcProofTarget target, PcBorrowKind kind)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Known) return null;
        if (kind == PcBorrowKind.Pointer) return Usable(capabilities, target, Pointer) ? PcActivation.Pointer : null;
        if (State(capabilities, target, Caption) == PcLearned.Passed) return PcActivation.Caption;
        if (State(capabilities, target, Foreground) == PcLearned.Passed) return PcActivation.Foreground;
        if (State(capabilities, target, Caption) == PcLearned.Unknown) return PcActivation.Caption;
        return State(capabilities, target, Foreground) == PcLearned.Unknown ? PcActivation.Foreground : null;
    }

    /// <summary>Null when a borrow may run on this target for this kind (proven, or a probe); else the needs_real_input reason.</summary>
    public static string? Unproven(PcCapabilities capabilities, PcProofTarget target, PcBorrowKind kind) =>
        Activation(capabilities, target, kind) is null || !Usable(capabilities, target, Restore) ? PcRealInputReason.ActivationUnproven : null;

    /// <summary>
    /// What one borrow on this target taught (the self-check, DESIGN.md 2.6): a borrow that finished proves its activation,
    /// one whose target did not come in front refuses it for 7 days; a finished borrow proves the restore when the user's
    /// window came back in front and refuses it when it did not. Anything else (Not now, the gate, a covered point, a
    /// control that would not take the focus) teaches nothing.
    /// </summary>
    public static void Learn(PcCapabilities capabilities, PcProofTarget target, PcActivation activation, PcBorrowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(outcome);
        if (!target.Known) return;
        var way = Key(target, MethodOf(activation));
        if (outcome.Activated == false) capabilities.Refuse(way);
        else if (outcome.Done || outcome.Activated == true) capabilities.Passed(way);
        if (!outcome.Done) return;
        if (outcome.ForegroundRestored) capabilities.Passed(Key(target, Restore));
        else capabilities.Refuse(Key(target, Restore));
    }

    private static PcLearned State(PcCapabilities capabilities, PcProofTarget target, string method) => capabilities.State(Key(target, method));

    private static bool Usable(PcCapabilities capabilities, PcProofTarget target, string method) =>
        State(capabilities, target, method) is PcLearned.Passed or PcLearned.Unknown;
}

/// <summary>
/// Whose borrow proofs: the target's exe (lower-case file name), its file version and its UI framework. A proof learned on
/// one target is never used for another exe, version or framework.
/// </summary>
public sealed record PcProofTarget(string Exe, string Version, PcFramework Framework)
{
    public static PcProofTarget Of(PcWindowInfo window, PcFramework framework)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new(window.ExeName, window.Version, framework);
    }

    /// <summary>A real exe (not empty and not the old "every app" mark).</summary>
    public bool Known => Exe.Length > 0 && Exe != PcBorrowProofs.AnyApp;
}

/// <summary>One borrow: whose it is, the target window, and the SendInput arrays it sends (one array each, in order).</summary>
public sealed record PcBorrowRequest(string Stream, PcBorrowKind Kind, nint Target, string Exe)
{
    public IReadOnlyList<IReadOnlyList<PcInputEvent>> Chunks { get; init; } = [];

    /// <summary>How long the user's input may be held: 150 ms for a click, a chord or a text chunk; 600 ms for a drag.</summary>
    public int HoldCapMs { get; init; } = RealInputLease.ClickCapMs;

    /// <summary>The gap between two arrays (a drag's steps, its hold before the first move).</summary>
    public IReadOnlyList<int> GapsMs { get; init; } = [];

    /// <summary>A pointer step's point: the target must be the window under it.</summary>
    public PcPoint? Point { get; init; }

    /// <summary>A focus step's control, when it has a window of its own: it must hold the focus before any key is sent.</summary>
    public nint FocusControl { get; init; }

    /// <summary>A focus step's way to focus its control (UI Automation); null when the keys go to the app's own focus.</summary>
    public Func<CancellationToken, Task<bool>>? Focus { get; init; }

    /// <summary>"one click in Paint": the pill's chip when the borrow ran at once.</summary>
    public string Detail { get; init; } = "";

    /// <summary>The engine's wait mark (the 25 s rule): called with "hands_off" while the borrow waits for the gate.</summary>
    public Func<string, IDisposable>? Waiting { get; init; }

    /// <summary>The target whose learned ways the borrow uses and teaches; null on the older per-exe proofs.</summary>
    public PcProofTarget? Proof { get; init; }
}

public enum PcBorrowStatus
{
    Done,
    NeedsRealInput,

    /// <summary>The user chose Not now.</summary>
    Deferred,

    /// <summary>Nothing reached the target: it was not in front, or its point was covered.</summary>
    NotSent,

    /// <summary>It started and could not finish (the watchdog, Windows refused events, a key held before the hold).</summary>
    Failed,
}

public sealed record PcBorrowOutcome(PcBorrowStatus Status, string? Reason = null)
{
    public bool Done => Status == PcBorrowStatus.Done;

    /// <summary>The error the step answers with, when the status alone does not name it (a covered point, the hold cap).</summary>
    public PcErrorCode? Code { get; init; }

    /// <summary>The user's keys that could not go back to their window (the pill offers "Put them back").</summary>
    public int KeptKeys { get; init; }

    public bool ForegroundRestored { get; init; } = true;

    public bool ZOrderRestored { get; init; } = true;

    /// <summary>Whether the target came in front by the borrow's way: false when it did not; null when not checked.</summary>
    public bool? Activated { get; init; }
}

/// <summary>What a borrow saves before it holds the user's input, to put back after.</summary>
public sealed record PcBorrowSaved(PcPoint Cursor, nint Foreground, nint Focus)
{
    /// <summary>The window just above the target (GetWindow GW_HWNDPREV).</summary>
    public nint Neighbour { get; init; }

    /// <summary>The top-level windows above the target, nearest first (when the neighbour is gone).</summary>
    public IReadOnlyList<nint> Above { get; init; } = [];

    public bool Minimized { get; init; }
}

/// <summary>The Windows side of a borrow. The app binds the Win32 host; tests bind a fake. No automated test sends input.</summary>
public interface IRealInputHost
{
    /// <summary>The virtual desktop, for absolute mouse coordinates.</summary>
    PcRect Desktop();

    ushort ScanCode(ushort vk);

    PcBorrowSaved Save(nint target);

    /// <summary>The foreground window and its thread's focused control.</summary>
    (nint Foreground, nint Focus) Focus();

    /// <summary>Any key or button physically down (GetAsyncKeyState), the gate's cross-check.</summary>
    bool Held();

    /// <summary>GetLastInputInfo on the lease's clock; null when it cannot be read.</summary>
    long? LastInputMs();

    /// <summary>The top-level window under a screen point.</summary>
    nint WindowAt(PcPoint point);

    /// <summary>A point on the target's caption (hit-tested HTCAPTION); null when it has none on screen.</summary>
    PcPoint? Caption(nint target);

    /// <summary>Raises the target above a window covering the point, without activating it.</summary>
    void Raise(nint target);

    bool SetForeground(nint window);

    /// <summary>One SendInput array, every event carrying the tag in dwExtraInfo; returns how many Windows inserted.</summary>
    int Send(IReadOnlyList<PcInputEvent> events, ulong tag);

    /// <summary>The user's window back in front, and focus on their control (the proven restore way).</summary>
    bool Restore(nint window, nint focus);

    /// <summary>The target back under its neighbour (or the nearest saved window still there, or the user's window).</summary>
    bool RestoreZOrder(nint target, PcBorrowSaved saved);

    void RestorePlacement(nint target, PcBorrowSaved saved);

    /// <summary>A point clamped to the virtual screen and to a monitor that still exists.</summary>
    PcPoint Clamp(PcPoint point);

    void SetCursor(PcPoint point);
}

/// <summary>A borrow waiting for the gate, as the pill shows it: what it waits for, and the user's two answers.</summary>
public sealed class PcBorrowWait(string stream, string detail)
{
    private int now;
    private int declined;

    public string Stream { get; } = stream;

    public string Detail { get; } = detail;

    /// <summary>What the gate waits for now.</summary>
    public PcGateCheck Check { get; internal set; }

    /// <summary>Waiting behind another app's borrow (first come, first served).</summary>
    public bool Queued { get; internal set; }

    public bool DoItNowChosen => Volatile.Read(ref now) == 1;

    public bool NotNowChosen => Volatile.Read(ref declined) == 1;

    /// <summary>"Do it now": the stillness part of the gate passes at once (never a held key, a composition or a protected window).</summary>
    public void DoItNow() => Volatile.Write(ref now, 1);

    /// <summary>"Not now": the step returns deferred_by_user (needs_real_input "foreground protected" while that is the wait).</summary>
    public void NotNow() => Volatile.Write(ref declined, 1);
}

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 "Borrow sequence", critiques C3, C9, C11, C17).
// What a borrow of the real mouse or keyboard (rung R4) is made of: the request a rung hands the lease (the stream, the
// target, the exact SendInput arrays, the hold cap), what came of it, what was saved to put back, the native seam the
// lease drives, the wait the pill shows ("Hands off for a moment", with Do it now and Not now), and the activation and
// restore ways, which are used only once the NP-4 probe proved them (per exe, or for every app).
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
/// Which activation and restore ways the NP-4 probe proved, kept in the capability store under rung R4 per exe (or "*" for
/// every app). Nothing is proven until the probe's results are imported: until then R4 answers "activation unproven".
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

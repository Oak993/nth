// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-17; DESIGN.md 2.7 "The gate", 2.9; critiques C1,
// C2, C4). The still moment a borrow (and an app-global step in an app the user is active in) waits for: the pointer
// still 350 ms, the keyboard still 700 ms (2 s with an IME language that gives no composition signal), no input the hooks
// did not see (GetLastInputInfo, for elevated windows) within the keyboard threshold, nothing held, no open composition,
// a foreground that may be borrowed, and healthy hooks. "Do it now" passes only the stillness part.
namespace Marrow.Services.Port.PcControl;

public enum PcGateBlock
{
    None,
    Unhealthy,
    Protected,
    Held,
    Composition,
    Pointer,
    Keys,
    Unseen,
}

public readonly record struct PcGateCheck(PcGateBlock Block, PcForegroundProtection Protection = PcForegroundProtection.None)
{
    public bool Open => Block == PcGateBlock.None;

    /// <summary>What the pill says while the step waits (DESIGN.md 2.7, 4.5).</summary>
    public string Waiting => Block switch
    {
        PcGateBlock.Unhealthy => "Real mouse and keyboard off for a moment: restarting the input check",
        PcGateBlock.Protected => Protection is PcForegroundProtection.Fullscreen or PcForegroundProtection.Presentation or PcForegroundProtection.Busy
            ? "Waiting until you leave the full-screen app"
            : Protection is PcForegroundProtection.SecureDesktop or PcForegroundProtection.Locked ? "Waiting until Windows' prompt closes" : "Waiting until you leave the admin window",
        _ => "Hands off for a moment",
    };
}

public static class PcInputGate
{
    public const int PointerStillMs = 350;
    public const int KeysStillMs = 700;
    public const int ImeKeysStillMs = 2_000;

    /// <summary>The gate at <paramref name="nowMs"/>; <paramref name="doItNow"/> is the user's "Do it now" (stillness passes).</summary>
    public static PcGateCheck Check(PcPhysicalSnapshot state, long nowMs, PcForegroundProtection protection, bool doItNow = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        // A protected foreground first: the hooks cannot see input to an elevated window, so they read unhealthy there too.
        if (protection != PcForegroundProtection.None) return new(PcGateBlock.Protected, protection);
        if (!state.Healthy) return new(PcGateBlock.Unhealthy);
        if (state.HeldKeys > 0 || state.HeldButtons > 0) return new(PcGateBlock.Held);
        if (state.Composition != 0) return new(PcGateBlock.Composition);
        if (doItNow) return new(PcGateBlock.None);
        if (state.LastPointerMs is { } pointer && nowMs - pointer < PointerStillMs) return new(PcGateBlock.Pointer);
        var keys = state.ImeLanguage ? ImeKeysStillMs : KeysStillMs;
        if (state.LastKeyMs is { } key && nowMs - key < keys) return new(PcGateBlock.Keys);
        if (state.LastUnseenMs is { } unseen && nowMs - unseen < keys) return new(PcGateBlock.Unseen);
        return new(PcGateBlock.None);
    }
}

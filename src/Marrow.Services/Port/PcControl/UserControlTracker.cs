// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9 "The user's control"; critique C6). Whose control a
// write is aimed at, read from the physical input monitor. A control is the user's while it has its window's keyboard focus
// and the user physically typed into it within the last 2 s, or while the user's pointer is pressed on it. Tactus never
// writes into the user's control: the step waits and answers user_typing_here after 10 s. Tactus's own input never counts
// (the monitor tells it apart by tag). While the hooks cannot see the keyboard, a focused control in the window in front
// counts as the user's until their last input is 2 s old (fail closed).
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>Why a control is the user's now.</summary>
public enum PcUserHold
{
    None,

    /// <summary>The user typed into it within the last 2 s and it still has the focus.</summary>
    Typing,

    /// <summary>The user's pointer is pressed on it now.</summary>
    Pressed,

    /// <summary>The hooks cannot see the keyboard; the control has the focus of the window in front.</summary>
    Unseen,
}

/// <summary>The answer: why the control is the user's, and the moment to look again if nothing new happens.</summary>
public readonly record struct PcUserControlCheck(PcUserHold Hold, long AgainMs)
{
    public bool Users => Hold != PcUserHold.None;
}

/// <summary>
/// A write's target as the engine sees it now: the window, the element (null for keys and text sent to the app's own
/// focus), whether the window is in front, the foreground thread's focused window, and whether UI Automation reports the
/// element focused.
/// </summary>
public readonly record struct PcWriteTarget(nint Window, PcUiNode? Element, bool InFront, nint FocusNow, bool Focused);

public static class UserControlTracker
{
    /// <summary>A control stays the user's for 2 s after their last key into it (DESIGN.md 2.9).</summary>
    public const int TypingMs = 2000;

    /// <summary>How often a held button or an unhealthy monitor is looked at again.</summary>
    public const int PollMs = 250;

    public static PcUserControlCheck Check(PcPhysicalSnapshot state, long nowMs, PcWriteTarget target)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Healthy)
        {
            if (!target.InFront || !(target.Focused || Owns(target.Element, target.FocusNow))) return default;
            // Unseen input ages like typing; with none seen at all, Tactus cannot tell, so the control counts as the user's.
            if (state.LastUnseenMs is { } unseen && nowMs - unseen >= TypingMs) return default;
            return new(PcUserHold.Unseen, nowMs + PollMs);
        }
        if (state.HeldButtons > 0 && state.PressedWindow == target.Window && PressedOn(target, state.PressedPoint))
            return new(PcUserHold.Pressed, nowMs + PollMs);
        if (state.KeyWindow != target.Window || state.KeyWindowMs is not { } key || nowMs - key >= TypingMs) return default;
        return TypedInto(state, target) ? new(PcUserHold.Typing, key + TypingMs) : default;
    }

    // The key went where the target is: the target's own window had the focus, or (an element with no window of its own)
    // UI Automation reports it focused and the focus has not moved to another window since the key.
    private static bool TypedInto(PcPhysicalSnapshot state, PcWriteTarget target)
    {
        if (target.Element is not { } element) return true;
        if (element.NativeWindow != 0) return state.KeyFocus == element.NativeWindow;
        if (!target.Focused) return false;
        return !target.InFront || state.KeyFocus == 0 || target.FocusNow == 0 || state.KeyFocus == target.FocusNow;
    }

    private static bool PressedOn(PcWriteTarget target, PcPoint point) => target.Element is not { } element
        ? target.InFront
        : !element.Bounds.IsEmpty && element.Bounds.Contains(point);

    private static bool Owns(PcUiNode? element, nint focus) => element is null || (element.NativeWindow != 0 && element.NativeWindow == focus);
}

// PORT-ONLY: Tactus pill v2, the card and the keyboard (OD-0145, ED-NEW-pcctl-20 item 8, ED-NEW-pcctl-26; DESIGN.md 10.4 "It
// never steals focus", 2.7 the borrow while the card has the keyboard). The overlay is no-activate; the card takes the
// keyboard only when the user clicks the reply field (WM_MOUSEACTIVATE answers MA_ACTIVATE there, and that click is the
// user's own input, so the foreground-lock rules allow it). In order:
// - when the click is answered with MA_ACTIVATE, before Windows activates the overlay, the foreground window and its
//   focused element are saved;
// - 100 ms after that click, if the overlay is not the foreground window (a foreground lock nth cannot pass), the card
//   says "Type your answer in the chat" with Open chat (the draft goes with it); nth never uses AttachThreadInput or a
//   synthetic Alt;
// - when the card is answered, folded with Later, or loses its last ask while it has the keyboard, the keyboard goes back
//   only if the overlay is still the foreground window, the saved window still exists, is the same window and is not
//   minimized or cloaked, and no borrow runs or is about to start for it; then SetForegroundWindow on it, then UIA
//   SetFocus on the saved element if focus did not land there; otherwise nothing is activated;
// - a click on another app while the card has the keyboard only takes the keyboard away: later hand-backs do nothing;
// - while the card has the keyboard no borrow starts, and a queued borrow waits 300 ms after the hand-back.
// The Win32 and UIA calls are behind ICardFocusNative; the rules run on a fake.
namespace Marrow.App.Port.PcControl;

/// <summary>The window the keyboard came from and its focused element (GetGUIThreadInfo, UIA focus).</summary>
/// <param name="Element">The focused element as the UIA reader names it (its runtime id); null when unknown.</param>
public sealed record CardFocusTarget(nint Window, int ProcessId, string ClassName, string? Element);

public interface ICardFocusNative
{
    nint Foreground();

    /// <summary>The window and its focused element; null when the window cannot be read.</summary>
    CardFocusTarget? Describe(nint window);

    /// <summary>The saved window still exists and is the same window (its process and class; section 2.1 identity).</summary>
    bool Same(CardFocusTarget target);

    /// <summary>Minimized or cloaked.</summary>
    bool Hidden(nint window);

    bool SetForeground(nint window);

    /// <summary>The UIA focus is the saved element.</summary>
    bool FocusIs(CardFocusTarget target);

    /// <summary>UIA SetFocus on the saved element.</summary>
    void Focus(CardFocusTarget target);
}

/// <summary>What a hand-back did, and why nothing was activated when it did not.</summary>
public enum CardHandBack
{
    /// <summary>The saved window is in front again (and its element focused).</summary>
    Done,

    /// <summary>The card did not have the keyboard (nothing to give back).</summary>
    NotHeld,

    /// <summary>The user clicked elsewhere: the overlay is no longer the foreground window.</summary>
    Moved,

    /// <summary>The saved window is gone, another window took its handle, or nothing was saved.</summary>
    Gone,

    /// <summary>The saved window is minimized or cloaked.</summary>
    Hidden,

    /// <summary>A borrow runs or is about to start for the saved window.</summary>
    Borrowing,
}

public sealed class CardFocus
{
    public const double ActivationCheckSeconds = 0.1;
    public const double TypingSeconds = 2;
    public const double BorrowWaitSeconds = 0.3;

    private readonly ICardFocusNative native;
    private readonly Func<nint> overlay;
    private readonly Func<nint, bool> borrowing;
    private readonly TimeProvider clock;
    private readonly Action<double, Action> after;
    private double lastTyped = double.NegativeInfinity;
    private double handedBackAt = double.NegativeInfinity;
    private int clicks;

    /// <param name="overlay">The input band window that shows the card.</param>
    /// <param name="borrowing">Whether a borrow runs or is queued for a window.</param>
    /// <param name="after">Runs an action later on the UI thread (seconds).</param>
    public CardFocus(ICardFocusNative native, Func<nint> overlay, Func<nint, bool> borrowing, TimeProvider clock, Action<double, Action> after)
    {
        this.native = native;
        this.overlay = overlay;
        this.borrowing = borrowing;
        this.clock = clock;
        this.after = after;
    }

    /// <summary>The window and element the keyboard goes back to; null when nothing is saved.</summary>
    public CardFocusTarget? Saved { get; private set; }

    /// <summary>The card's overlay has the keyboard.</summary>
    public bool HasKeyboard { get; private set; }

    /// <summary>The overlay did not come to the front after the click: the card says "Type your answer in the chat".</summary>
    public event Action? ActivationFailed;

    private double Now => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    /// <summary>
    /// The reply field's click was answered with MA_ACTIVATE (before Windows activates the overlay): the foreground window
    /// and its focus are saved, unless the overlay already has the keyboard; the 100 ms check starts.
    /// </summary>
    public void Activating()
    {
        var front = native.Foreground();
        if (!HasKeyboard && front != overlay() && front != 0) Saved = native.Describe(front);
        var click = ++clicks;
        after(ActivationCheckSeconds, () =>
        {
            if (click != clicks) return;
            if (native.Foreground() == overlay()) HasKeyboard = true;
            else ActivationFailed?.Invoke();
        });
    }

    /// <summary>The overlay window was activated.</summary>
    public void Activated() => HasKeyboard = true;

    /// <summary>The overlay lost activation: the user clicked another app. The card keeps its draft; later hand-backs do nothing.</summary>
    public void Deactivated()
    {
        if (!HasKeyboard) return;
        HasKeyboard = false;
        Saved = null;
    }

    /// <summary>A key was typed in the card.</summary>
    public void Typed() => lastTyped = Now;

    /// <summary>The user is typing in the card: a draft is open, or a key was typed in the last 2 s.</summary>
    public bool Typing(bool draftOpen) => draftOpen || Now - lastTyped < TypingSeconds;

    /// <summary>
    /// The card was answered, folded with Later, or lost its last ask: the keyboard goes back to the saved window when every
    /// rule holds; otherwise nothing is activated.
    /// </summary>
    public CardHandBack HandBack()
    {
        if (!HasKeyboard) return CardHandBack.NotHeld;
        var saved = Saved;
        HasKeyboard = false;
        Saved = null;
        handedBackAt = Now;
        if (native.Foreground() != overlay()) return CardHandBack.Moved;
        if (saved is null || !native.Same(saved)) return CardHandBack.Gone;
        if (native.Hidden(saved.Window)) return CardHandBack.Hidden;
        if (borrowing(saved.Window)) return CardHandBack.Borrowing;
        if (!native.SetForeground(saved.Window)) return CardHandBack.Moved;
        if (saved.Element is not null && !native.FocusIs(saved)) native.Focus(saved);
        return CardHandBack.Done;
    }

    /// <summary>
    /// How long a borrow must wait before it may start: never while the card has the keyboard (null), and 300 ms after a
    /// hand-back (DESIGN.md 10.4, 2.7).
    /// </summary>
    public TimeSpan? BorrowWait()
    {
        if (HasKeyboard) return null;
        var left = handedBackAt + BorrowWaitSeconds - Now;
        return left > 0 ? TimeSpan.FromSeconds(left) : TimeSpan.Zero;
    }
}

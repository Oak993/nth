// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): the line as the pill will read it.
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Port.Voice.Countdown;

/// <summary>
/// The draining line over one voice session: <see cref="Frame"/> is what the pill draws now (null outside the countdown), read from the session's own clock so the line and
/// the cap cannot disagree. VoicePillSlot feeds it into the mounted pill. Two polite screen-reader announcements go with it
/// (ED-351, the page's proposal: "once at 0:30 and once at 0:10"): "30 seconds left." when the countdown starts and "10 seconds left." 20 s later; a session that
/// ends first makes no later one.
/// </summary>
public sealed class CapCountdownLine : IDisposable
{
    public const string ThirtySeconds = "30 seconds left.";
    public const string TenSeconds = "10 seconds left.";

    private readonly VoiceSession session;
    private readonly Func<bool> reduceMotion;
    private readonly Func<CountdownPalette> palette;
    private WorkItem? tenSeconds;

    public CapCountdownLine(VoiceSession session, Func<bool> reduceMotion, Func<CountdownPalette> palette)
    {
        this.session = session;
        this.reduceMotion = reduceMotion;
        this.palette = palette;
        session.CapCountdownStarted += OnStarted;
        session.StateChanged += OnState;
    }

    /// <summary>A polite announcement for the screen reader.</summary>
    public event Action<string>? Announced;

    /// <summary>The line this instant, or null when the session is not in the last 30 s before the cap.</summary>
    public CapCountdownFrame? Frame() =>
        session.CapCountdownRemaining is { } left ? CapCountdown.Frame(left, reduceMotion(), palette()) : null;

    public void Dispose()
    {
        session.CapCountdownStarted -= OnStarted;
        session.StateChanged -= OnState;
        tenSeconds?.Cancel();
    }

    private void OnStarted()
    {
        Announced?.Invoke(ThirtySeconds);
        tenSeconds?.Cancel();
        // Anchor the second announcement to the session's cap, even if this callback was delivered late.
        var delay = (session.CapCountdownRemaining ?? CapCountdown.Length) - TimeSpan.FromSeconds(10);
        tenSeconds = MainActor.AsyncAfter(delay, () => Announced?.Invoke(TenSeconds));
    }

    private void OnState(VoiceSessionState from, VoiceSessionState to)
    {
        if (to != VoiceSessionState.Listening) tenSeconds?.Cancel();
    }
}

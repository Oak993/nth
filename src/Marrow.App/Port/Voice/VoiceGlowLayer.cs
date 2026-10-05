// PORT-ONLY, owner-ruled addition (OD-0091 second amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-13.3): the voice glow in a real composer.
using Avalonia.Controls;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Hosts the composer's <see cref="VoiceGlowView"/> (VT-9) and feeds it from the voice session: <c>active</c> is Listening or Processing, <c>processing</c> is Processing, and the raw level <c>all</c>
/// (V-5.3, unsmoothed; the driver smooths it) is written once per frame while the session listens (it is 0 in Processing, the page's <c>level: () =&gt; vlev</c> after the capture has stopped). It stands over the
/// composer's glass and light and under its text and controls (V-13.3), as large as the composer's rounded shape (corner 22).
/// </summary>
internal sealed class VoiceGlowLayer : Decorator
{
    private readonly ComposerVoiceController voice;
    private DisplayLink? link;

    public VoiceGlowLayer(ComposerVoiceController voice)
    {
        this.voice = voice;
        IsHitTestVisible = false;
        Glow = new VoiceGlowView();
        Child = Glow;
        this.Track(() => voice.State, state =>
        {
            Glow.Active = state is VoiceSessionState.Listening or VoiceSessionState.Processing;
            Glow.Processing = state == VoiceSessionState.Processing;
            Feed();
        });
        AttachedToVisualTree += (_, _) => Feed();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>The glow.</summary>
    public VoiceGlowView Glow { get; }

    private void Feed()
    {
        Glow.Level.Value = voice.Session.AudioLevel.All;
        if (link is null && voice.State == VoiceSessionState.Listening && FrameClock.IsBound && VisualRoot is not null)
        {
            link = FrameClock.DisplayLink(Step);
            link.Add();
        }
    }

    private void Step()
    {
        Glow.Level.Value = voice.Session.AudioLevel.All;
        if (voice.State == VoiceSessionState.Listening) return;
        Stop();
    }

    private void Stop()
    {
        link?.Invalidate();
        link = null;
        Glow.Level.Value = 0;
    }
}

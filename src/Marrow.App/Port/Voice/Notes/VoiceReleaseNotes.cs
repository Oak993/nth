// PORT-ONLY: OD-0106 Notes A. Bridges the picked notes to the composer's existing transitioned note surfaces.
using Marrow.Core.Port.Voice.Session;
namespace Marrow.App.Port.Voice.Notes;

internal sealed class VoiceReleaseNotes : IDisposable
{
    private readonly VoiceNoteBridge bridge;
    private readonly VoiceSession session;
    private VoiceNoteContent? current;
    private long occurrence;
    private readonly Action<VoiceNoteContent?> show;
    private readonly Action<string> announce;
    public VoiceReleaseNotes(VoiceSession session, Func<string?> draft, Func<bool> downloading, Func<bool> pillShowsDownload, IProcessorFeatures processor,
        Action<VoiceNoteContent?> show, Action<string> announce)
    {
        this.session = session;
        this.show = show;
        this.announce = announce;
        bridge = new VoiceNoteBridge(session, draft, downloading, pillShowsDownload, processor);
        bridge.Changed += Changed;
        session.StateChanged += OnState;
    }
    public void RefreshSurface()
    {
        // The generic composer writer can refresh first. Reapply the cached object after it;
        // moving between the two surfaces does not create another note or announcement.
        bridge.RefreshSurface();
        if (current is not null) show(current);
    }
    public void Dispose() { session.StateChanged -= OnState; bridge.Changed -= Changed; bridge.Dispose(); }
    private void OnState(VoiceSessionState from, VoiceSessionState to)
    {
        if (to is VoiceSessionState.Asking or VoiceSessionState.Listening) bridge.Dismiss();
    }
    private void Changed(VoiceNotePresentation? presentation)
    {
        // The controller owns blocked, cancelled, no-speech and privacy notes and their announcements.
        if (presentation is null || presentation.Note.Case == VoiceNoteCase.Blocked)
        {
            current = null;
            show(null);
            return;
        }
        var appeared = occurrence != bridge.Occurrence;
        if (appeared) { occurrence = bridge.Occurrence; current = Content(presentation.Note); }
        show(current);
        if (appeared) announce(presentation.Announcement);
    }
    public static VoiceNoteContent Content(VoiceNote note) => new("voice.error." + note.Case, note.Before, note.Link, Target(note.Destination), VoiceNotes.DisplayLength, true);
    internal static string Target(VoiceNoteDestination destination) => destination switch
    {
        VoiceNoteDestination.PrivacyMicrophone => "ms-settings:privacy-microphone",
        VoiceNoteDestination.SoundSettings => "ms-settings:sound",
        VoiceNoteDestination.VoiceTypingMicrophone => "voice:microphone",
        VoiceNoteDestination.VoiceTypingSpeechModel => "voice:speechModel",
        VoiceNoteDestination.VoiceTypingEngine => "voice:engine",
        VoiceNoteDestination.VoiceTypingOpenAiKey => "voice:openAiKey",
        VoiceNoteDestination.VoiceTypingPage => "voice:page",
    };
}

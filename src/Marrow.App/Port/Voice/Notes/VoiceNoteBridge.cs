// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the session's notes, resolved and placed.
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Port.Voice.Notes;

/// <summary>
/// Listens to one voice session and says which note the composer shows now, on which surface, and for how long (V-14.6: 5.2 s here for all twelve). The composer's
/// view draws <see cref="Current"/>; it is NOT DONE here (the field's note layer and the notice line's link are VT-12's and VT-13's drawing).
/// A device removed mid-session ends the session like a click and the note comes after its final pass (V-4.4); a changed default device or format stops it with no
/// note (nothing broke that a link fixes). The still-downloading note stays away while the pill itself shows the download.
/// </summary>
public sealed class VoiceNoteBridge : IDisposable
{
    private readonly VoiceSession session;
    private readonly Func<string?> draft;
    private readonly Func<bool> modelDownloading;
    private readonly Func<bool> pillShowsDownload;
    private readonly IProcessorFeatures processor;
    private WorkItem? expiry;
    private bool deviceRemovedPending;

    /// <param name="draft">The composer's draft text now (empty: the field shows the note; text: the notice line does).</param>
    /// <param name="modelDownloading">True while the chosen model's download is under way (the model store's row says Downloading).</param>
    /// <param name="pillShowsDownload">True while the pill shows the first download ("Getting voice ready", VT-15): then the still-downloading note is not shown.</param>
    public VoiceNoteBridge(VoiceSession session, Func<string?> draft, Func<bool> modelDownloading, Func<bool> pillShowsDownload, IProcessorFeatures processor)
    {
        this.session = session;
        this.draft = draft;
        this.modelDownloading = modelDownloading;
        this.pillShowsDownload = pillShowsDownload;
        this.processor = processor;
        session.NoteRaised += OnNote;
        session.StateChanged += OnState;
    }

    /// <summary>The note on show, or null.</summary>
    public VoiceNotePresentation? Current { get; private set; }

    /// <summary>A note appeared, moved to the other surface, or went (null).</summary>
    public event Action<VoiceNotePresentation?>? Changed;

    /// <summary>The draft changed while a note shows: the note moves to the surface the new draft calls for (the user's text is never hidden).</summary>
    public void RefreshSurface()
    {
        if (Current is not { } shown) return;
        var surface = VoiceNotes.SurfaceFor(draft());
        if (surface == shown.Surface) return;
        Current = shown with { Surface = surface };
        Changed?.Invoke(Current);
    }

    public void Dispose()
    {
        session.NoteRaised -= OnNote;
        session.StateChanged -= OnState;
        expiry?.Cancel();
    }

    private void OnNote(VoiceNoteKind kind)
    {
        var facts = new VoiceNoteFacts(kind, session.FailureCause, session.LastDeviceEvent, modelDownloading(), kind == VoiceNoteKind.ModelLoadFailed && LacksAvx2());
        if (VoiceNoteResolver.Resolve(facts) is not { } noteCase) return;
        if (noteCase == VoiceNoteCase.ModelStillDownloading && pillShowsDownload()) return;
        Show(noteCase);
    }

    // The session stopped on a removed device: the final pass runs, and when it hands over (Landing or Rest) the note appears.
    private void OnState(VoiceSessionState from, VoiceSessionState to)
    {
        if (to == VoiceSessionState.Listening) deviceRemovedPending = false;
        if (to == VoiceSessionState.Processing && session.LastStopReason == VoiceStopReason.DeviceEvent && session.LastDeviceEvent == CaptureDeviceEventKind.DeviceRemoved)
            deviceRemovedPending = true;
        if (deviceRemovedPending && from == VoiceSessionState.Processing && to is VoiceSessionState.Landing or VoiceSessionState.Rest)
        {
            deviceRemovedPending = false;
            Show(VoiceNoteCase.MicrophoneUnplugged);
        }
    }

    // Unanswerable means not blamed: a processor that cannot be asked leaves the plain failed-to-load note.
    private bool LacksAvx2()
    {
        try
        {
            return !processor.Avx2Supported;
        }
        catch (SeamNotBoundException)
        {
            return false;
        }
    }

    private void Show(VoiceNoteCase noteCase)
    {
        var note = VoiceNotes.For(noteCase);
        expiry?.Cancel();
        Current = new VoiceNotePresentation(note, VoiceNotes.SurfaceFor(draft()), VoiceNotes.DisplayLength, VoiceNotes.Announcement(note));
        Changed?.Invoke(Current);
        expiry = MainActor.AsyncAfter(VoiceNotes.DisplayLength, () =>
        {
            Current = null;
            Changed?.Invoke(null);
        });
    }
}

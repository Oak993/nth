// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): one voice session at a time (voice-SPEC V-7.11), one language
// question at a time (V-12.11), and the model that is loaded (V-7.9: one model at a time, process-wide).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>
/// The sessions of one app. At most one is Listening or Processing (<see cref="Active"/>); a click on another orb finishes it as a second
/// click would and then starts the new one (V-7.11): the new orb waits in <see cref="Queued"/>, only the latest click waiting
/// (ED-292 (9)). One question is open at a time. The hub also remembers which model the engine has loaded, so a hover that began the
/// load and the click after it share one load. Main-actor only.
/// </summary>
public sealed class VoiceSessionHub
{
    private ISpeechEngine? loadedEngine;
    private SpeechModel? loadedModel;
    private Task<bool>? load;

    public static Ambient<VoiceSessionHub> Shared { get; } = new(() => new VoiceSessionHub());

    /// <summary>The session that is Listening or Processing, if any.</summary>
    public VoiceSession? Active { get; private set; }

    /// <summary>The session whose language question is open, if any.</summary>
    public VoiceSession? Asking { get; private set; }

    /// <summary>The session whose click is waiting for <see cref="Active"/> to finish, if any.</summary>
    public VoiceSession? Queued { get; private set; }

    /// <summary>The exception the last failed load ended with (VT-17: a note names its cause); null after a load that worked.</summary>
    internal Exception? LoadFailure { get; private set; }

    internal void Enter(VoiceSession session) => Active = session;

    internal void Release(VoiceSession session)
    {
        if (!ReferenceEquals(Active, session)) return;
        Active = null;
        if (Queued is not { } next) return;
        Queued = null;
        next.StartFromQueue();
    }

    internal void Queue(VoiceSession session)
    {
        if (Queued is { } earlier && !ReferenceEquals(earlier, session)) Queued = null;
        Queued = session;
    }

    internal void Dequeue(VoiceSession session)
    {
        if (ReferenceEquals(Queued, session)) Queued = null;
    }

    internal void OpenQuestion(VoiceSession session) => Asking = session;

    internal void CloseQuestion(VoiceSession session)
    {
        if (ReferenceEquals(Asking, session)) Asking = null;
    }

    /// <summary>
    /// V-7.9: loads <paramref name="model"/> into <paramref name="engine"/> unless that load is already done or under way; the answer is
    /// false when the load failed (the next call tries again). The load is a task of its own, so cancelling a session never cancels it.
    /// </summary>
    internal Task<bool> EnsureLoaded(ISpeechEngine engine, SpeechModel model)
    {
        if (load is { } existing && ReferenceEquals(loadedEngine, engine) && Equals(loadedModel, model)) return existing;
        loadedEngine = engine;
        loadedModel = model;
        var handle = MainActor.Spawn<bool>(async () =>
        {
            try
            {
                await engine.LoadAsync(model, new EngineLoadOptions(), SwiftTask.CancellationToken);
                LoadFailure = null;
                return true;
            }
            catch (Exception error)
            {
                LoadFailure = error;
                if (ReferenceEquals(loadedEngine, engine) && Equals(loadedModel, model)) load = null;
                return false;
            }
        });
        load = handle.Value;
        return load;
    }
}

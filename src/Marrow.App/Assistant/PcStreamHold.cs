// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6b "Tactus and a task's pause"; unit AS-6; ED-NEW-dextra-as6-5). No Swift twin.
// The seam between a task's Pause and Resume and Tactus's streams. Tactus is not in this tree yet (branch claude/pc-control);
// when it lands it implements this, and the app binds it in StartDextra. Until then no stream is held and a task's Pause
// simply waits for its turn.
namespace Marrow.App.Assistant;

public interface IPcStreamHold
{
    /// <summary>
    /// A task paused: holds, at Tactus's next atomic boundary, the streams its chats (<paramref name="threads"/>) drive. Their
    /// Tactus calls then return waiting_for_user with reason "paused". Returns how many it held; an app the owner paused
    /// alone in the pill is not held (and not counted).
    /// </summary>
    int Hold(Guid mission, IReadOnlyList<Guid> threads);

    /// <summary>
    /// The task resumed: resumes the streams <see cref="Hold"/> held (never an app the owner paused alone in the pill). Tactus
    /// re-reads each window before its first held step, so a resume never acts on a stale view.
    /// </summary>
    void Release(Guid mission);
}

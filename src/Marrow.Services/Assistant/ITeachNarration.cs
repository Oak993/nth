// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1; unit AS-19; ED-NEW-dextra-as19-8). No Swift twin.
// What the recorder asks of the owner's narration: listen, pause, resume, stop. TeachNarrator is the one that listens with the app's own voice seams.
namespace Marrow.Services.Assistant;

public interface ITeachNarration
{
    /// <summary>Opens the microphone. Null when it listens; else why not, in the owner's words (the recording goes on without narration).</summary>
    string? Start();

    void Pause();

    void Resume();

    /// <summary>Stops, and waits for what is still being recognised.</summary>
    Task StopAsync();
}

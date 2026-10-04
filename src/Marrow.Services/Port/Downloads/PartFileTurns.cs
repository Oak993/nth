// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3, Windows review): one transfer at a time per partial file.
namespace Marrow.Services.Port.Downloads;

/// <summary>
/// Files with the same pinned hash share one partial file (<c>&lt;sha8&gt;.part</c>, section 3.1): the byte-identical files of a skill set (the same
/// LICENSE.txt in several skills, empty <c>__init__.py</c> files) or the same file in two jobs. Only one transfer at a time may write it (on Windows a
/// second writer cannot even open it); the next one resumes from what the first left, or fetches its own copy once the first has moved it into place.
/// Process-wide, keyed by the full path, case-insensitively (as the file system is). ED-NEW-f-sk-3 (15).
/// </summary>
internal static class PartFileTurns
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (SemaphoreSlim Turn, int Users)> Turns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Waits for the turn on <paramref name="partPath"/>; give it back with <see cref="Give"/> (also after a cancelled wait it is not held).</summary>
    internal static async Task<SemaphoreSlim> Take(string partPath, CancellationToken token)
    {
        var key = Path.GetFullPath(partPath);
        SemaphoreSlim turn;
        lock (Gate)
        {
            var users = 0;
            if (Turns.TryGetValue(key, out var entry))
            {
                turn = entry.Turn;
                users = entry.Users;
            }
            else turn = new SemaphoreSlim(1, 1);
            Turns[key] = (turn, users + 1);
        }
        try
        {
            await turn.WaitAsync(token);
        }
        catch
        {
            Leave(key);
            throw;
        }
        return turn;
    }

    internal static void Give(string partPath, SemaphoreSlim turn)
    {
        turn.Release();
        Leave(Path.GetFullPath(partPath));
    }

    private static void Leave(string key)
    {
        lock (Gate)
        {
            var entry = Turns[key];
            if (entry.Users == 1) Turns.Remove(key);
            else Turns[key] = (entry.Turn, entry.Users - 1);
        }
    }
}

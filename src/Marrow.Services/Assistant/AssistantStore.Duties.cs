// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2, 15.3; unit AS-10; ED-NEW-dextra-as10-5, -6, -7). No Swift twin.
// Beside duties.json, a duty's watcher keeps what it saw last (a folder's file list, a page's region) so a change that happened
// while nth was closed is still found at the next start: assistants/<id>/watch/<duty>.json, written the same temp-then-rename way.
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    /// <summary>Whether the assistant has a duties file at all (a file that exists but does not parse is not the same as none).</summary>
    public bool HasDuties(string assistantId)
    {
        lock (gate)
        {
            ThrowIfClosed();
            return File.Exists(AssistantFile(assistantId, "duties.json"));
        }
    }

    /// <summary>What a duty's watcher saw last, or null when there is none or it does not parse.</summary>
    public JsonObject? Watch(string assistantId, string dutyId) => ReadDocument(WatchFile(assistantId, dutyId)) as JsonObject;

    public void SaveWatch(string assistantId, string dutyId, JsonObject state) => SaveDocument(WatchFile(assistantId, dutyId), state);

    /// <summary>Forgets what a duty's watcher saw (the duty was deleted or its trigger changed).</summary>
    public void DeleteWatch(string assistantId, string dutyId)
    {
        var path = WatchFile(assistantId, dutyId);
        lock (gate)
        {
            ThrowIfClosed();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>The two background switches and whether their card was shown (<c>background.json</c>, AS-10), or null when there is none or it does not parse.</summary>
    public JsonObject? Background() => ReadDocument(Path.Combine(Root, BackgroundFile)) as JsonObject;

    public void SaveBackground(JsonObject document) => SaveDocument(Path.Combine(Root, BackgroundFile), document);

    public const string BackgroundFile = "background.json";

    private string WatchFile(string assistantId, string dutyId)
    {
        CheckId(dutyId);
        return Path.Combine(Root, "assistants", Checked(assistantId), "watch", dutyId + ".json");
    }
}

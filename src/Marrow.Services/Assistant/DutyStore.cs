// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.3; unit AS-10; ED-NEW-dextra-as10-5). No Swift twin.
// Where the scheduler keeps its duties and what its watchers saw last. The product's store is the assistant store's duties.json
// and watch/ files; a test brings a memory one. A duties file that exists but cannot be read is never replaced by an empty one:
// it is reported, and the scheduler leaves that assistant's duties alone.
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public interface IDutyStore
{
    /// <summary>The duties of one assistant. A file that does not parse comes back with a problem and no duties, and is not written over.</summary>
    DutyFile Load(string assistantId);

    void Save(string assistantId, DutyDocument document);

    JsonObject? LoadWatch(string assistantId, string dutyId);

    void SaveWatch(string assistantId, string dutyId, JsonObject state);

    void DeleteWatch(string assistantId, string dutyId);
}

/// <summary>An assistant's duties file as loaded: the document, and whether it may be written back.</summary>
public sealed record DutyFile(DutyDocument Document, bool WritesAllowed)
{
    public static DutyFile Unwritable(string problem) => new(DutyDocument.Empty with { Problems = [problem] }, false);
}

public sealed class AssistantDutyStore(AssistantStore store) : IDutyStore
{
    public DutyFile Load(string assistantId)
    {
        var node = store.Duties(assistantId);
        if (node is null)
        {
            // Missing is a fresh start. A file that exists but did not parse is somebody's data: it is read-only until they fix it.
            return store.HasDuties(assistantId)
                ? DutyFile.Unwritable("The duties file could not be read, so nth leaves it as it is and runs none of its duties.")
                : new DutyFile(DutyDocument.Empty, true);
        }
        var document = DutyJson.Read(node);
        return new DutyFile(document, !document.Newer);
    }

    public void Save(string assistantId, DutyDocument document) => store.SaveDuties(assistantId, DutyJson.Write(document));

    public JsonObject? LoadWatch(string assistantId, string dutyId) => store.Watch(assistantId, dutyId);

    public void SaveWatch(string assistantId, string dutyId, JsonObject state) => store.SaveWatch(assistantId, dutyId, state);

    public void DeleteWatch(string assistantId, string dutyId) => store.DeleteWatch(assistantId, dutyId);
}

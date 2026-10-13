using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): the scheduler's store in memory. It keeps the JSON a file would hold, so a save and a load go through DutyJson as the real store's do.</summary>
public sealed class MemoryDutyStore : IDutyStore
{
    private readonly Dictionary<string, JsonNode> files = [];
    private readonly Dictionary<string, JsonObject> watches = [];
    private readonly HashSet<string> unreadable = [];

    public int Saves { get; private set; }

    public Exception? SaveFails { get; set; }

    public List<string> Deleted { get; } = [];

    /// <summary>A file that exists but does not parse: it is read-only to the scheduler.</summary>
    public void BreakFile(string assistantId) => unreadable.Add(assistantId);

    public void Put(string assistantId, params Duty[] duties) =>
        files[assistantId] = DutyJson.Write(new DutyDocument(duties));

    public void PutRaw(string assistantId, JsonNode node) => files[assistantId] = node;

    public JsonNode? Raw(string assistantId) => files.GetValueOrDefault(assistantId);

    public DutyDocument Saved(string assistantId) => DutyJson.Read(files.GetValueOrDefault(assistantId));

    public DutyFile Load(string assistantId)
    {
        if (unreadable.Contains(assistantId)) return DutyFile.Unwritable("The duties file could not be read, so nth leaves it as it is and runs none of its duties.");
        var document = DutyJson.Read(files.GetValueOrDefault(assistantId));
        return new DutyFile(document, !document.Newer);
    }

    public void Save(string assistantId, DutyDocument document)
    {
        if (SaveFails is { } failure) throw failure;
        files[assistantId] = DutyJson.Write(document);
        Saves++;
    }

    public JsonObject? LoadWatch(string assistantId, string dutyId) => watches.GetValueOrDefault(assistantId + "/" + dutyId)?.DeepClone() as JsonObject;

    public void SaveWatch(string assistantId, string dutyId, JsonObject state) => watches[assistantId + "/" + dutyId] = (JsonObject)state.DeepClone();

    public void DeleteWatch(string assistantId, string dutyId)
    {
        watches.Remove(assistantId + "/" + dutyId);
        Deleted.Add(dutyId);
    }

    public bool HasWatch(string assistantId, string dutyId) => watches.ContainsKey(assistantId + "/" + dutyId);
}

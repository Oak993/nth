// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 steps 1, 5 and 6, 15.3; unit AS-19; ED-NEW-dextra-as19-1, -7). No Swift twin.
// One assistant's teach recordings over the store: create one, append what the owner does, keep its manifest and its draft, read it back,
// and delete it. It is also where a recording that nobody saved is let go (after its retention, ED-NEW-dextra-as19-7), where one a crash
// left running is marked Interrupted with its counts worked out again from its files, and where a saved recording is deleted the rest of
// the way if the save was cut short between the skill and the delete. It holds no clock of its own: the callers pass the time.
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What a sweep did, for the activity log (ids and reasons only, never content).</summary>
public sealed record TeachSweep(IReadOnlyList<(string Id, string Reason)> Deleted, IReadOnlyList<string> Interrupted);

public sealed class TeachRecordings(AssistantStore store, string assistantId)
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public string AssistantId { get; } = assistantId;

    private static byte[] Bytes(JsonNode document) => Utf8.GetBytes(document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Starts a recording: a folder and a manifest in the Recording state.</summary>
    public TeachRecordingInfo Create(DateTimeOffset started, TeachSource source)
    {
        var info = new TeachRecordingInfo(AssistantStore.NewTeachId(), AssistantId, started, TeachRecordingState.Recording) { Source = source, Changed = started };
        SaveInfo(info);
        return info;
    }

    /// <summary>The manifest, or null when there is none or it does not read.</summary>
    public TeachRecordingInfo? Info(string id)
    {
        try { return TeachJson.ReadInfo(AssistantJson.Parse(ReadText(id, AssistantStore.TeachManifestFile))) is { } info && info.Id == id ? info : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    /// <summary>Writes the manifest. A manifest a newer nth wrote is not written over.</summary>
    public void SaveInfo(TeachRecordingInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Newer) throw new InvalidOperationException("This recording was made by a newer nth; this one leaves it as it is.");
        store.WriteTeachFile(AssistantId, info.Id, AssistantStore.TeachManifestFile, Bytes(TeachJson.InfoDocument(info)));
    }

    /// <summary>Every recording that has a readable manifest, newest first, and the ids whose manifest does not read.</summary>
    public IReadOnlyList<TeachRecordingInfo> List(out IReadOnlyList<string> unreadable)
    {
        var bad = new List<string>();
        var list = new List<TeachRecordingInfo>();
        foreach (var id in store.TeachRecordingIds(AssistantId))
        {
            if (Info(id) is { } info) list.Add(info);
            else bad.Add(id);
        }
        unreadable = bad;
        return [.. list.OrderByDescending(i => i.Started)];
    }

    // ---- what the owner did ----

    public void AppendEvent(string id, TeachEvent e) => store.AppendTeachLine(AssistantId, id, AssistantStore.TeachEventsFile, TeachJson.EventLine(e));

    public void AppendNarration(string id, TeachNarrationLine line) =>
        store.AppendTeachLine(AssistantId, id, AssistantStore.TeachNarrationFile, TeachJson.NarrationLine(line));

    /// <summary>Writes a frame as <c>frames/NNNN.jpg</c> and notes it; returns its name.</summary>
    public string WriteFrame(string id, int number, TimeSpan at, byte[] jpeg)
    {
        var name = "frames/" + number.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + ".jpg";
        store.WriteTeachFile(AssistantId, id, name, jpeg);
        store.AppendTeachLine(AssistantId, id, AssistantStore.TeachFramesFile, TeachJson.FrameLine(at, name));
        return name;
    }

    public byte[]? Frame(string id, string name) => AssistantStore.IsFrameName(name) ? Read(id, name) : null;

    /// <summary>The events in order. A line that does not read (a crash cut it, someone edited it) is left out and counted.</summary>
    public IReadOnlyList<TeachEvent> Events(string id, out int skipped)
    {
        var events = new List<TeachEvent>();
        skipped = 0;
        foreach (var line in store.ReadTeachLines(AssistantId, id, AssistantStore.TeachEventsFile))
        {
            if (TeachJson.ReadEvent(line) is { } e) events.Add(e);
            else skipped++;
        }
        return [.. events.OrderBy(e => e.Seq)];
    }

    public IReadOnlyList<TeachNarrationLine> Narration(string id) =>
        [.. store.ReadTeachLines(AssistantId, id, AssistantStore.TeachNarrationFile).Select(TeachJson.ReadNarration).OfType<TeachNarrationLine>().OrderBy(n => n.Start)];

    // ---- the draft ----

    public TeachDraft? Draft(string id)
    {
        try { return TeachJson.ReadDraft(AssistantJson.Parse(ReadText(id, AssistantStore.TeachDraftFile))) is { } draft && draft.RecordingId == id ? draft : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    public void SaveDraft(string id, TeachDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Version > TeachDraft.CurrentVersion) throw new InvalidOperationException("This draft was made by a newer nth; this one leaves it as it is.");
        store.WriteTeachFile(AssistantId, id, AssistantStore.TeachDraftFile, Bytes(TeachJson.DraftDocument(draft with { RecordingId = id })));
    }

    // ---- the end ----

    /// <summary>Deletes the recording whole: events, frames, narration, draft and manifest.</summary>
    public bool Delete(string id) => store.DeleteTeachRecording(AssistantId, id);

    /// <summary>The recording's counts worked out again from its files (after a crash the manifest may be behind).</summary>
    public TeachRecordingInfo Recount(TeachRecordingInfo info)
    {
        var events = Events(info.Id, out _);
        var frames = store.ReadTeachLines(AssistantId, info.Id, AssistantStore.TeachFramesFile).Count;
        var narration = Narration(info.Id);
        var last = events.Select(e => e.At).Concat(narration.Select(n => n.End)).DefaultIfEmpty(TimeSpan.Zero).Max();
        long bytes = 0;
        foreach (var file in store.TeachFiles(AssistantId, info.Id).Where(AssistantStore.IsFrameName)) bytes += Read(info.Id, file)?.LongLength ?? 0;
        return info with { Events = events.Count, Frames = frames, Narration = narration.Count, FrameBytes = bytes, Elapsed = last > info.Elapsed ? last : info.Elapsed };
    }

    /// <summary>
    /// What happens to the recordings when nth starts and now and then after: one a crash left running becomes Interrupted (what was written
    /// is kept and can still be drafted), one that was saved and not kept is deleted the rest of the way, and one nobody saved within its
    /// retention is deleted. A recording a newer nth wrote is left alone, and one being saved (Saving) is the controller's to settle.
    /// </summary>
    public TeachSweep Sweep(DateTimeOffset now, string? live = null)
    {
        var deleted = new List<(string, string)>();
        var interrupted = new List<string>();
        foreach (var info in List(out _))
        {
            if (info.Newer || info.Id == live) continue;
            if (info.State is TeachRecordingState.Recording or TeachRecordingState.Paused)
            {
                SaveInfo(Recount(info) with { State = TeachRecordingState.Interrupted, Ended = TeachStopReason.Closed, Changed = now });
                interrupted.Add(info.Id);
            }
            else if (info.State == TeachRecordingState.Saved && !info.Keep)
            {
                if (Delete(info.Id)) deleted.Add((info.Id, "saved"));
            }
            else if (info.ExpiresAt is { } expires && expires <= now && info.State != TeachRecordingState.Saving)
            {
                if (Delete(info.Id)) deleted.Add((info.Id, "expired"));
            }
        }
        return new TeachSweep(deleted, interrupted);
    }

    private byte[]? Read(string id, string relative) => store.ReadTeachFile(AssistantId, id, relative);

    private string? ReadText(string id, string relative) => Read(id, relative) is { } bytes ? Utf8.GetString(bytes) : null;
}

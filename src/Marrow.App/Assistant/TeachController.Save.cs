// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 steps 5 and 6; unit AS-19; ED-NEW-dextra-as19-7). No Swift twin.
// The end of teach mode: the owner approves the draft and it becomes an own skill, and the raw recording is deleted unless he keeps it. A save
// is two effects, the skill and the delete, so it records its intent before the first (the manifest says Saving, and with what name) and its
// outcome after it (Saved). If nth is cut off between the two effects, the next start settles it: the skill is looked for by the staging
// folder that carries the recording's id, and a recording whose skill exists is deleted the rest of the way; one whose skill does not exist
// goes back to a draft for the owner to approve again. Nothing is saved twice, and nothing is deleted before the skill is there.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App.Assistant;

public sealed partial class TeachController
{
    /// <summary>
    /// Saves the draft as an own skill. The recording is deleted after, with its draft, unless <paramref name="keep"/> is set. Returns the skill's
    /// id, or why nothing was saved (the draft has a problem, the name is taken, the own-skill service said no); then the recording is untouched.
    /// </summary>
    public async Task<TeachSaved> Save(string id, bool keep, CancellationToken cancel = default)
    {
        if (o.Skills is not { } skills) return new TeachSaved(null, "Saving skills is not available right now.");
        if (recordings.Info(id) is not { } info) return new TeachSaved(null, "That recording is gone or cannot be read.");
        if (info.Newer) return new TeachSaved(null, "This recording was made by a newer nth. Open it there.");
        if (info.State != TeachRecordingState.Drafted) return new TeachSaved(null, info.State is TeachRecordingState.Saving or TeachRecordingState.Saved ? "It was already turned into a skill." : "Draft it first.");
        if (recordings.Draft(id) is not { } draft) return new TeachSaved(null, "There is no draft to save.");
        if (draft.Problem() is { } problem) return new TeachSaved(null, problem);
        if (CurrentRun() is { } running && running.RecordingId == id) return new TeachSaved(null, "A trial of it is running. Finish it first.");

        // Intent first: if nth is cut off from here, the next start knows a skill with this name may exist.
        recordings.SaveInfo(info with { State = TeachRecordingState.Saving, SkillName = draft.Name, Keep = keep, Changed = o.Time.GetUtcNow() });
        string staging;
        try { staging = o.Store.NewTeachStaging(o.AssistantId, id); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Revert(id);
            return new TeachSaved(null, "nth could not make a folder to write the skill in: " + error.GetType().Name + ".");
        }
        TeachSaved saved;
        try
        {
            saved = await new TeachSkillWriter(skills, o.Time).Save(draft, staging, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Revert(id);
            throw;
        }
        finally
        {
            try { o.Store.DeleteTeachStaging(o.AssistantId, staging); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { o.Faulted?.Invoke("teach staging: " + error.GetType().Name); }
        }
        if (!saved.Ok)
        {
            Revert(id);
            return saved;
        }
        Complete(id, saved.ItemId!, keep, draft.Name);
        return saved;
    }

    // Outcome recorded; then the raw recording goes, unless he keeps it.
    private void Complete(string id, string itemId, bool keep, string name)
    {
        if (recordings.Info(id) is { } current && !current.Newer)
            recordings.SaveInfo(current with { State = TeachRecordingState.Saved, SkillId = itemId, SkillName = name, Keep = keep, Changed = o.Time.GetUtcNow() });
        WithdrawCards(id, "The skill was saved.");
        var deleted = false;
        if (!keep)
        {
            try { deleted = recordings.Delete(id); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The skill is saved and the manifest says so; the next start finishes the delete.
                o.Faulted?.Invoke("teach delete: " + error.GetType().Name);
            }
        }
        Log("save", $"recording {id}: skill {name} ({itemId}), recording {(keep ? "kept" : deleted ? "deleted" : "to be deleted")}");
        try { o.SkillsChanged?.Invoke(); }
        catch (Exception error) when (error is not OutOfMemoryException) { o.Faulted?.Invoke("teach skills page: " + error.GetType().Name); }
        Changed?.Invoke();
    }

    // The save did not happen: the recording is a draft again, as it was.
    private void Revert(string id)
    {
        if (recordings.Info(id) is { Newer: false, State: TeachRecordingState.Saving } info)
            recordings.SaveInfo(info with { State = TeachRecordingState.Drafted, SkillName = null, Changed = o.Time.GetUtcNow() });
        Changed?.Invoke();
    }

    /// <summary>Whether the raw recording is kept after the skill is saved (10.6: deleted unless he keeps it). Takes effect at the save.</summary>
    public bool SetKeep(string id, bool keep)
    {
        lock (draftGate)
        {
            if (recordings.Info(id) is not { Newer: false } info || info.State is TeachRecordingState.Saving or TeachRecordingState.Saved) return false;
            recordings.SaveInfo(info with { Keep = keep, Changed = o.Time.GetUtcNow() });
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The owner deletes a recording he does not want (draft, frames, narration and all). Refused while it records or is being tried or saved.</summary>
    public bool Discard(string id)
    {
        if (Active?.Id == id || CurrentRun()?.RecordingId == id) return false;
        if (recordings.Info(id) is { State: TeachRecordingState.Saving }) return false;
        WithdrawCards(id, "The recording was deleted.");
        var gone = recordings.Delete(id);
        if (gone) Log("delete", $"recording {id}: deleted by you");
        Changed?.Invoke();
        return gone;
    }

    /// <summary>Settles the saves nth was cut off in the middle of (see the file's note).</summary>
    private void SettleSaves()
    {
        IReadOnlyList<OwnItem>? items = null;
        foreach (var info in recordings.List(out _).Where(i => i.State == TeachRecordingState.Saving && !i.Newer))
        {
            items ??= o.OwnSkills();
            var made = items.FirstOrDefault(i => i.Kind == CustomAddKind.Skills && i.Name == info.SkillName
                && i.Provenance.Location.Contains(AssistantStore.TeachStagingPrefix + info.Id, StringComparison.Ordinal));
            if (made is null)
            {
                Revert(info.Id);
                Log("settle", $"recording {info.Id}: the save did not finish; it is a draft again");
                continue;
            }
            Complete(info.Id, made.Id, info.Keep, made.Name);
        }
    }
}

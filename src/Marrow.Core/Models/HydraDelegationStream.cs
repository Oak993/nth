using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/HydraDelegationStream.swift (B2 1fbf81e).
// R-FILE-1: an extension X in another file is a partial of X in that file's twin. Only
// delegation(fromEntry:) (:110-118) is ported here, per the orchestrator's ruling on
// unit Core.Models.Hydra part 4's STOP (PR-HYDRA-084/085/098): it is the sole dependency
// Hydra.cs's Delegations(string) needed from this file. The rest of
// HydraDelegationStream.swift (StreamedDelegations, streamedDelegations(in:)/(inBody:),
// isJSONWhitespace, HydraDelegationStreamParser) belongs to unit
// Core.Models.HydraDelegationStream and is left for that unit to add to this file.
public static partial class HydraPrompts
{
    // :110-118. One entry of a block read as a head's task: kept only with a non-empty
    // "prompt" (after trimming); titled from the prompt when "task" is absent or blank
    // (TextCleanup.SingleLine, limit 60), else the trimmed "task" as-is (even if it were
    // to trim to empty, task.isEmpty ? ... uses the Swift `task` local, which is the
    // trimmed-or-empty string, so an entry with an all-whitespace "task" still falls back
    // to the prompt title); "name" then "head", trimmed, blank treated as absent;
    // "project" then "repo", trimmed, blank treated as absent; "profile", trimmed, blank
    // treated as absent.
    public static HydraDelegation? Delegation(JSONValue entry)
    {
        var promptRaw = entry["prompt"]?.JSONValueString?.Trimmed();
        if (string.IsNullOrEmpty(promptRaw)) return null;
        var prompt = promptRaw;
        var task = entry["task"]?.JSONValueString?.Trimmed() ?? "";
        var name = entry["name"]?.JSONValueString?.Trimmed()
            ?? entry["head"]?.JSONValueString?.Trimmed();
        var announced = !string.IsNullOrEmpty(name) ? name : null;
        var project = entry["project"]?.JSONValueString?.Trimmed()
            ?? entry["repo"]?.JSONValueString?.Trimmed();
        var profile = entry["profile"]?.JSONValueString?.Trimmed();
        return new HydraDelegation
        {
            Task = task.Length == 0 ? TextCleanup.SingleLine(prompt, 60) : task,
            Prompt = prompt,
            Name = announced,
            Project = !string.IsNullOrEmpty(project) ? project : null,
            Profile = !string.IsNullOrEmpty(profile) ? profile : null,
        };
    }
}

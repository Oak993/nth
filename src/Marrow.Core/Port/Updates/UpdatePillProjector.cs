// PORT-ONLY: first-release feature unit UE-5 (BUILD-PLAN.md 2.4 UE-5, OD-0100 place D, words U1). What the sidebar's update pill and its popover say, read from the orchestrator's rows. Pure.
namespace Marrow.Core.Port.Updates;

public enum UpdatePillPhase
{
    /// <summary>Nothing is out of date, nothing is running, nothing failed, nothing waits for a restart: there is no pill (OD-0100 "shown only when something is out of date").</summary>
    Hidden,

    Available,
    Running,
    Failed,

    /// <summary>The run is over and an item (Marrow itself) needs the app restarted to take effect.</summary>
    Restart,

    /// <summary>The run is over, something was updated and nothing needs a restart: "N updated", until the user has seen it (the popover was closed after it).</summary>
    Done,
}

public enum UpdatePillAction
{
    None,
    UpdateEverything,
    Retry,
    RestartMarrow,
    CheckNow,
}

/// <summary>One line of the popover's list. <see cref="Subtitle"/> is the line under the name: "The app" for Marrow, "Downloading 40%" while it downloads, the updater's command for a provider CLI (the design page's `sub`). <see cref="ShowsLatest"/> is true when the line reads "installed → latest" (a newer version is known and not yet installed).</summary>
public sealed record UpdatePillRow(string Id, string Title, UpdateItemKind Kind, UpdateRowState State, string? Installed, string? Latest, bool ShowsLatest, string Status, double? Progress, string? Message, string? Subtitle = null);

/// <summary>Everything the pill and its popover draw. <see cref="Fraction"/> is the ring: the finished items of the run plus the running one's own progress, over the items the run has.</summary>
public sealed record UpdatePillState(
    UpdatePillPhase Phase,
    string PillText,
    string Title,
    string Detail,
    string ButtonText,
    UpdatePillAction Action,
    bool ButtonEnabled,
    double Fraction,
    string? BannerTitle,
    string? BannerText,
    IReadOnlyList<UpdatePillRow> Rows)
{
    public static UpdatePillState Hidden { get; } = new(UpdatePillPhase.Hidden, "", "", "", "", UpdatePillAction.None, false, 0, null, null, []);
}

/// <summary>
/// Reads the orchestrator's rows into the pill's state. The words are the design page's U1 "Plain" set (OD-0100 pick). It keeps one thing between calls: which items the run in hand has (the
/// ones it queued), so "Updating 2 of 3" and the ring count that run, and a Retry counts only the item it runs (ENGINEERING DECISION ED-344). ENGINEERING DECISIONS: "1 update" in the
/// singular (the page prints "1 updates"); a failure outranks everything else until it is retried; a finished run that updated something and needs no restart stays as the design's done phase
/// ("N updated", button "Check now") until <see cref="Acknowledge"/> (the popover was closed after it) or the next check or run (lead decision on the UE-5 review, following ue-page.js apply() for D); the popover lists Marrow first, as the page does, and Ignis's model points right after it (ED-NEW-ignis-settings).
/// </summary>
public sealed class UpdatePillProjector
{
    private readonly HashSet<string> targets = new(StringComparer.Ordinal);
    private bool runOver = true;
    private bool doneSeen;

    /// <summary>The user has seen the done phase (the popover was closed after it): the pill leaves. A new run shows it again.</summary>
    public void Acknowledge() => doneSeen = true;

    public UpdatePillState Project(IReadOnlyList<UpdateRow> rows, bool busy)
    {
        var running = rows.Any(r => r.State is UpdateRowState.Queued or UpdateRowState.Updating);
        if (running)
        {
            if (runOver) targets.Clear();
            runOver = false;
            doneSeen = false;
            foreach (var row in rows.Where(r => r.State is UpdateRowState.Queued or UpdateRowState.Updating)) targets.Add(row.Id);
        }
        else runOver = true;
        var list = rows.Where(r => r.State is not (UpdateRowState.Unchecked or UpdateRowState.NotInstalled)).OrderBy(r => r.Kind == UpdateItemKind.Marrow ? 0 : r.Kind == UpdateItemKind.ModelPoints ? 1 : 2).Select(ToRow).ToList();
        if (running) return Running(rows, list);
        var failed = rows.FirstOrDefault(r => r.State == UpdateRowState.Failed);
        if (failed is not null) return Failed(rows, list, failed);
        if (rows.Any(r => r.State == UpdateRowState.UpdatingInBackground))
        {
            var restart = rows.Any(r => r.RestartRequired);
            var due = rows.Any(r => r.State is UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated);
            return new(UpdatePillPhase.Running, "Updating in the background", "Updating in the background",
                NameList(rows.Where(r => r.State == UpdateRowState.UpdatingInBackground).Select(r => r.Title).ToList()) + ".",
                restart ? "Restart Marrow" : due ? "Update everything" : "Check now", restart ? UpdatePillAction.RestartMarrow : due ? UpdatePillAction.UpdateEverything : UpdatePillAction.CheckNow,
                true, 0, null, null, list);
        }
        var available = rows.Where(r => r.State == UpdateRowState.UpdateAvailable).ToList();
        if (available.Count > 0) return Available(available, list);
        if (rows.Any(r => r.RestartRequired)) return Restart(rows, list);
        if (!doneSeen && rows.Any(r => r.State == UpdateRowState.Updated)) return Done(rows, list);
        return UpdatePillState.Hidden;
    }

    private UpdatePillState Running(IReadOnlyList<UpdateRow> rows, List<UpdatePillRow> list)
    {
        var mine = rows.Where(r => targets.Contains(r.Id)).ToList();
        var n = mine.Count;
        var done = mine.Count(r => r.State is UpdateRowState.Updated or UpdateRowState.UpToDate or UpdateRowState.Failed);
        var current = mine.FirstOrDefault(r => r.State == UpdateRowState.Updating) ?? mine.FirstOrDefault(r => r.State == UpdateRowState.Queued);
        var index = Math.Min(done + 1, n);
        var fraction = Math.Clamp((done + (current?.State == UpdateRowState.Updating ? current.Progress ?? 0 : 0)) / n, 0, 1);
        return new UpdatePillState(UpdatePillPhase.Running, $"Updating {index} of {n}", $"Updating {current?.Title}… {index} of {n}",
            "Keep working; Marrow restarts only when you say so.", "Updating…", UpdatePillAction.None, false, fraction, null, null, list);
    }

    private static UpdatePillState Failed(IReadOnlyList<UpdateRow> rows, List<UpdatePillRow> list, UpdateRow failed)
    {
        var updated = rows.Where(r => r.State == UpdateRowState.Updated).Select(r => r.Title).ToList();
        var title = $"{failed.Title} didn’t update";
        var detail = (updated.Count > 0 ? $"Updated {NameList(updated)}. " : "") + "The fix is below.";
        var banner = !string.IsNullOrWhiteSpace(failed.Message) ? failed.Message
            : failed.Installed is { } installed ? $"{failed.Title} is still {installed} after its updater ran. Retry{(UpdateRowCommand.For(failed) is { } command ? $", or run {command} in a terminal" : "")}." : "Retry.";
        return new UpdatePillState(UpdatePillPhase.Failed, title, title, detail, "Retry", UpdatePillAction.Retry, true, 1, title, banner, list);
    }

    private static UpdatePillState Available(List<UpdateRow> available, List<UpdatePillRow> list)
    {
        var n = available.Count;
        var noun = n == 1 ? "update" : "updates";
        var names = available.Select(r => r.Latest is { } latest ? $"{r.Title} {latest}" : r.Title).ToList();
        return new UpdatePillState(UpdatePillPhase.Available, $"{n} {noun}", $"{n} {noun} available", NameList(names) + ".", "Update everything", UpdatePillAction.UpdateEverything, true, 0, null, null, list);
    }

    private static UpdatePillState Restart(IReadOnlyList<UpdateRow> rows, List<UpdatePillRow> list)
    {
        var updated = rows.Where(r => r.State == UpdateRowState.Updated).Select(r => r.Title).ToList();
        if (updated.Count == 0) updated = rows.Where(r => r.RestartRequired).Select(r => r.Title).ToList();
        return new UpdatePillState(UpdatePillPhase.Restart, "Restart to finish", $"{updated.Count} updated", $"Updated {NameList(updated)}. Restart Marrow to finish its update.",
            "Restart Marrow", UpdatePillAction.RestartMarrow, true, 1, null, null, list);
    }

    private static UpdatePillState Done(IReadOnlyList<UpdateRow> rows, List<UpdatePillRow> list)
    {
        var updated = rows.Where(r => r.State == UpdateRowState.Updated).Select(r => r.Title).ToList();
        var title = $"{updated.Count} updated";
        return new UpdatePillState(UpdatePillPhase.Done, title, title, $"Updated {NameList(updated)}.", "Check now", UpdatePillAction.CheckNow, true, 1, null, null, list);
    }

    private static UpdatePillRow ToRow(UpdateRow r)
    {
        var status = r.State switch
        {
            UpdateRowState.UpToDate => "Up to date",
            UpdateRowState.UpdateAvailable => "Update available",
            UpdateRowState.ChecksWhenUpdated => "Checks when updated",
            UpdateRowState.Queued => "Waiting",
            UpdateRowState.Updating => "Updating…",
            UpdateRowState.UpdatingInBackground => "Updating in the background",
            UpdateRowState.Updated => r.RestartRequired ? "Restart to finish" : "Updated",
            UpdateRowState.Failed => "Didn’t update",
            _ => "",
        };
        var shows = (r.State is UpdateRowState.UpdateAvailable or UpdateRowState.Queued or UpdateRowState.Updating or UpdateRowState.Failed) && r.Installed is not null && r.Latest is not null && !VersionText.Same(r.Installed, r.Latest);
        var subtitle = r.Kind switch
        {
            UpdateItemKind.Marrow when r.State == UpdateRowState.Updating && r.Progress is { } done => $"Downloading {(int)Math.Round(done * 100)}%",
            UpdateItemKind.Marrow => "The app",
            UpdateItemKind.ModelPoints => Ignis.IgnisWords.UpdatesRowDetail, // PORT-ONLY (OD-0134, ED-NEW-ignis-settings): listed right after Marrow
            UpdateItemKind.ProviderCli when r.Id.StartsWith("cli:", StringComparison.Ordinal) => r.Id[4..] + " update",
            _ => null,
        };
        return new UpdatePillRow(r.Id, r.Title, r.Kind, r.State, r.Installed, r.Latest, shows, status, r.Progress, r.Message, subtitle);
    }

    /// <summary>"A", "A and B", "A, B and C" (the design page's nameList).</summary>
    private static string NameList(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}

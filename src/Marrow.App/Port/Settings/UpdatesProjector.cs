// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4; OD-0100 place A, words U1 "Update everything / Up to date / Check now / Restart Marrow"). The Updates page's state and words, projected from the
// orchestrator's rows (UE-2, ED-308) the way the picked design page derives them (E:\Droppy\work\design\decisions-2026-10-04\update-everything\src\ue-page.js: phase(), texts(), the U1 word set).
// Every string below is U1's, with these ENGINEERING DECISIONS (ED-343): "1 update available" is singular for one (OD-0076's count rule); U1's "Marrow's skills and tools come with it"
// and the footnote about skills changing "only with a Marrow release" are left out because OD-0100's pick makes the skills and tools separate rows; the section titles for the port's other
// kinds ("Developer tools", "Skills and tools") and the two lines for the case where nothing is known to be available but a version is only known when its updater runs ("Versions are
// checked when updated", "Checks when updated" from BUILD-PLAN 2.4) are the only words not in U1.
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Settings;

internal static class UpdatesProjector
{
    // The page lists Marrow first, as the design does; the orchestrator's run order puts it last. PORT-ONLY (OD-0134, ED-NEW-ignis-settings): Ignis's model points sit right after Marrow, in its section
    // (design Settings › Updates), though they run before it.
    private static readonly UpdateItemKind[] PageOrder = [UpdateItemKind.Marrow, UpdateItemKind.ModelPoints, UpdateItemKind.ProviderCli, UpdateItemKind.DevTool, UpdateItemKind.SkillsAndTools];

    private static UpdateItemKind SectionOf(UpdateItemKind kind) => kind == UpdateItemKind.ModelPoints ? UpdateItemKind.Marrow : kind;

    private const string Ellipsis = "\u2026";
    private const string DidNot = "didn\u2019t update";

    /// <summary>The header's quiet button when nothing is waiting (ED-NEW-providers-updates-1; was U1's "Check now").</summary>
    internal const string CheckForUpdates = "Check for updates";

    /// <summary>A row whose updater carries on by itself (Office's Click-to-Run): the status column's words. The header already says "Updating in the background" and names the item;
    /// the row's 152 pt column holds the spinner and these words whole (ED-NEW-providers-updates-1; the longer phrase was cut to "Updating in the back...").</summary>
    internal const string InTheBackground = "In the background";

    public static UpdatesPageView Project(IReadOnlyList<UpdateRow> rows, UpdatesFlags flags)
    {
        var listed = rows.Where(r => r.State != UpdateRowState.NotInstalled).ToList(); // nothing to update for what is not installed
        var ordered = PageOrder.SelectMany(kind => listed.Where(r => r.Kind == kind)).ToList();
        var restart = ordered.FirstOrDefault(r => r.RestartRequired);
        var phase = PhaseOf(listed, flags, restart is not null);
        var banner = restart is not null && !flags.Later && phase != UpdatesPhase.Restarting;
        var header = HeaderOf(phase, listed, ordered, flags, restart, banner);
        var bannerView = restart is null
            ? new UpdatesBannerView(false, "", "")
            : new UpdatesBannerView(banner, $"{restart.Title} {VersionOf(restart.Latest ?? restart.Installed)} is ready.", "Restart nth to finish its update.");
        var idle = !flags.Busy && !flags.Checking && !flags.Restarting;
        var sections = PageOrder.Where(kind => SectionOf(kind) == kind)
            .Select(kind => new UpdatesSectionView(SectionTitle(kind), ordered.Where(r => SectionOf(r.Kind) == kind).Select(r => RowOf(r, flags, idle)).ToList()))
            .Where(s => s.Rows.Count > 0).ToList();
        return new UpdatesPageView(header, bannerView, sections);
    }

    private static UpdatesPhase PhaseOf(List<UpdateRow> rows, UpdatesFlags flags, bool restartPending)
    {
        if (flags.Restarting) return UpdatesPhase.Restarting;
        if (flags.Checking) return UpdatesPhase.Checking;
        if (rows.Any(r => r.State is UpdateRowState.Queued or UpdateRowState.Updating)) return UpdatesPhase.Running;
        if (rows.Any(r => r.State == UpdateRowState.Failed)) return UpdatesPhase.Failed;
        if (rows.Any(r => r.State == UpdateRowState.UpdatingInBackground)) return UpdatesPhase.Running;
        if (restartPending || rows.Any(r => r.State == UpdateRowState.Updated)) return UpdatesPhase.Done;
        if (rows.Any(r => r.State == UpdateRowState.UpdateAvailable)) return UpdatesPhase.Available;
        if (rows.Any(r => r.State == UpdateRowState.ChecksWhenUpdated)) return UpdatesPhase.Unsure;
        return UpdatesPhase.Current;
    }

    private static UpdatesHeaderView HeaderOf(UpdatesPhase phase, List<UpdateRow> listed, List<UpdateRow> ordered, UpdatesFlags flags, UpdateRow? restart, bool bannerShown)
    {
        string title, detail;
        var background = phase == UpdatesPhase.Running && !listed.Any(r => r.State is UpdateRowState.Queued or UpdateRowState.Updating)
            && listed.Any(r => r.State == UpdateRowState.UpdatingInBackground);
        switch (phase)
        {
            case UpdatesPhase.Restarting:
                (title, detail) = ($"Restarting nth{Ellipsis}", "");
                break;
            case UpdatesPhase.Checking:
                (title, detail) = ($"Checking for updates{Ellipsis}", "Asking each tool for its latest version.");
                break;
            case UpdatesPhase.Running:
                (title, detail) = background ? ("Updating in the background", NameList(ordered.Where(r => r.State == UpdateRowState.UpdatingInBackground).Select(r => r.Title).ToList()) + ".")
                    : (RunningTitle(listed, flags), "Keep working; nth restarts only when you say so.");
                break;
            case UpdatesPhase.Failed:
            {
                var failed = ordered.Where(r => r.State == UpdateRowState.Failed).Select(r => r.Title).ToList();
                var done = ordered.Where(r => r.State == UpdateRowState.Updated).Select(r => r.Title).ToList();
                title = $"{NameList(failed)} {DidNot}";
                detail = (done.Count > 0 ? $"Updated {NameList(done)}. " : "") + "The fix is below.";
                break;
            }
            case UpdatesPhase.Done:
            {
                var done = ordered.Where(r => r.State == UpdateRowState.Updated || r.RestartRequired).Select(r => r.Title).ToList();
                title = $"{done.Count} updated";
                detail = $"Updated {NameList(done)}." + (restart is not null && !bannerShown ? " Restart nth to finish its update." : "");
                break;
            }
            case UpdatesPhase.Available:
            {
                var out_ = ordered.Where(r => r.State == UpdateRowState.UpdateAvailable).ToList();
                title = out_.Count == 1 ? "1 update available" : $"{out_.Count} updates available";
                detail = NameList(out_.Select(r => $"{r.Title} {VersionOf(r.Latest)}").ToList()) + ".";
                break;
            }
            case UpdatesPhase.Unsure:
                title = "Versions are checked when updated";
                detail = NameList(ordered.Where(r => r.State == UpdateRowState.ChecksWhenUpdated).Select(r => r.Title).ToList()) + ".";
                break;
            default:
            {
                title = "Everything is up to date";
                var sentences = new List<string>();
                if (flags.CheckedAgo is { Length: > 0 } ago) sentences.Add($"Checked {ago}.");
                var providers = ordered.Where(r => r.Kind == UpdateItemKind.ProviderCli).Select(r => r.Title).ToList();
                if (providers.Count > 0) sentences.Add($"nth also keeps {NameList(providers)} current in the background.");
                detail = string.Join(" ", sentences);
                break;
            }
        }
        // ED-NEW-providers-updates-1 (the owner, 2026-10-11): one rule for the button when nothing is running and no restart is due. While an update is waiting it is "Update everything";
        // otherwise (everything current, or only failures left) it is "Check for updates", which checks every item again. A failed row keeps its own Retry; the header no longer says Retry.
        var waiting = listed.Any(r => r.State is UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated);
        var (label, action, busy, quiet) = phase switch
        {
            UpdatesPhase.Restarting => ($"Restarting{Ellipsis}", UpdatesAction.None, true, false),
            UpdatesPhase.Checking => ($"Checking{Ellipsis}", UpdatesAction.None, true, false),
            UpdatesPhase.Running when !background => ($"Updating{Ellipsis}", UpdatesAction.None, true, false),
            UpdatesPhase.Running or UpdatesPhase.Done when restart is not null => ("Restart nth", UpdatesAction.Restart, false, false),
            _ when waiting => ("Update everything", UpdatesAction.UpdateAll, false, false),
            _ => (CheckForUpdates, UpdatesAction.Check, false, true),
        };
        return new UpdatesHeaderView(phase, title, detail, label, action, busy, quiet);
    }

    // U1 sumRun: "Updating Codex… 2 of 3". The run is the remembered one when the page knows it, otherwise the rows that are part of one.
    private static string RunningTitle(List<UpdateRow> listed, UpdatesFlags flags)
    {
        var inRun = flags.RunIds is { } ids
            ? listed.Where(r => ids.Contains(r.Id)).ToList()
            : listed.Where(r => r.State is UpdateRowState.Queued or UpdateRowState.Updating or UpdateRowState.Updated or UpdateRowState.Failed).ToList();
        var total = inRun.Count;
        var finished = inRun.Count(r => r.State is not (UpdateRowState.Queued or UpdateRowState.Updating));
        var current = inRun.FirstOrDefault(r => r.State == UpdateRowState.Updating) ?? inRun.FirstOrDefault(r => r.State == UpdateRowState.Queued) ?? listed[0];
        return $"Updating {current.Title}{Ellipsis} {Math.Min(finished + 1, Math.Max(total, 1))} of {Math.Max(total, 1)}";
    }

    private static UpdatesRowView RowOf(UpdateRow r, UpdatesFlags flags, bool idle)
    {
        var installed = r.Installed is null ? "" : VersionText.Number(r.Installed);
        var latest = r.Latest is not null ? VersionText.Number(r.Latest) : r.State is UpdateRowState.ChecksWhenUpdated or UpdateRowState.UpdatingInBackground ? "\u2014" : installed;
        var (status, text) = StatusOf(r, flags);
        var command = UpdateRowCommand.For(r);
        string? lead = null, commandText = null, tail = null;
        if (r.State == UpdateRowState.Failed)
        {
            if (command is not null && installed.Length > 0) (lead, commandText, tail) = ($"{r.Title} is still {installed} after its updater ran. Retry, or run ", command, " in a terminal.");
            else lead = string.IsNullOrWhiteSpace(r.Message) ? $"{r.Title} {DidNot}." : Sentence(r.Message);
        }
        return new UpdatesRowView(
            r.Id, r.Title, r.Kind, SubOf(r), installed, latest, status, text, r.State == UpdateRowState.Updating && !flags.Checking, r.Progress,
            lead, commandText, tail, idle && r.State is UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated, idle && r.State == UpdateRowState.Failed);
    }

    private static (RowStatus, string) StatusOf(UpdateRow r, UpdatesFlags flags)
    {
        if (flags.Checking) return (RowStatus.Checking, $"Checking{Ellipsis}");
        return r.State switch
        {
            UpdateRowState.UpToDate => (RowStatus.Current, "Up to date"),
            UpdateRowState.UpdateAvailable => (RowStatus.Available, "Update available"),
            UpdateRowState.ChecksWhenUpdated => (RowStatus.ChecksWhenUpdated, "Checks when updated"),
            UpdateRowState.Queued => (RowStatus.Waiting, "Waiting"),
            UpdateRowState.Updating when r.Progress is { } p => (RowStatus.Downloading, $"Downloading {(int)Math.Round(Math.Clamp(p, 0, 1) * 100, MidpointRounding.AwayFromZero)}%"),
            UpdateRowState.Updating => (RowStatus.Updating, $"Updating{Ellipsis}"),
            UpdateRowState.UpdatingInBackground => (RowStatus.Updating, InTheBackground),
            UpdateRowState.Updated when r.RestartRequired => (RowStatus.RestartToFinish, "Restart to finish"),
            UpdateRowState.Updated => (RowStatus.Updated, "Updated"),
            UpdateRowState.Failed => (RowStatus.DidNotUpdate, "Didn\u2019t update"),
            _ => (RowStatus.None, ""),
        };
    }

    // U1 sub: Marrow "The app", a provider CLI its own updater command, otherwise nothing. The CLI ids are "cli:<raw provider id>" (ProviderCliUpdateItem); only the two providers whose CLI carries its
    // own updater are items (ProviderRegistry.UpdatableProviders), and their commands are "claude update" and "codex update" (ProviderRegistry.UpdateCLI runs "<cli> update").
    private static string SubOf(UpdateRow r) => r.Kind switch
    {
        UpdateItemKind.Marrow => "The app",
        UpdateItemKind.ModelPoints => Marrow.Core.Port.Ignis.IgnisWords.UpdatesRowDetail,
        UpdateItemKind.ProviderCli => UpdateRowCommand.For(r) ?? "Updates through its own installer",
        _ => "",
    };

    private static string SectionTitle(UpdateItemKind kind) => kind switch
    {
        UpdateItemKind.Marrow => "nth",
        UpdateItemKind.ProviderCli => "Provider tools",
        UpdateItemKind.DevTool => "Developer tools",
        _ => "Skills and tools",
    };

    private static string VersionOf(string? text) => text is null ? "" : VersionText.Number(text);

    private static string NameList(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        var capital = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return capital[^1] is '.' or '!' or '?' ? capital : capital + ".";
    }

    /// <summary>The age in "Checked 2 hours ago": just now under a minute, then whole minutes, hours and days, singular for one.</summary>
    public static string Age(TimeSpan elapsed)
    {
        var seconds = elapsed.TotalSeconds;
        if (seconds < 60) return "just now";
        static string Count(long n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
        if (seconds < 3_600) return Count((long)(seconds / 60), "minute");
        if (seconds < 86_400) return Count((long)(seconds / 3_600), "hour");
        return Count((long)(seconds / 86_400), "day");
    }
}

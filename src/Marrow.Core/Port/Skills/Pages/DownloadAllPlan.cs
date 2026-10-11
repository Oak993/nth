// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 "Download all A"). What a page's Download all would fetch, and the words of its confirmation.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Port.Skills.Pages;

public enum PlanLineKind
{
    /// <summary>On this PC already (installed, with an update on offer, or damaged): Download all takes only what is not here yet.</summary>
    AlreadyHere,

    /// <summary>Will be downloaded when the user presses Continue.</summary>
    Download,

    /// <summary>Under a vendor's terms the user has not accepted: nothing of it is fetched before the terms step (DL-15), so the run leaves it out.</summary>
    NeedsTerms,

    /// <summary>The catalogue holds no size for it yet, so the confirmation could not state it: the run leaves it out.</summary>
    NoSize,

    /// <summary>ED-NEW-dl-6 (review of ED-NEW-dlhonest-1): not on this PC and this build cannot fetch it; the confirmation lists it with the reason instead of dropping it, and the run leaves it out.</summary>
    CantFetch,

    /// <summary>
    /// ED-NEW-dl-13: a skill of the set on the line above that its catalogue entry leaves out (no clear licence of its own, a name the name rule refuses, a name another skill of the set
    /// already uses, ...). It is never fetched; the line says why. It is not an item, so the counts, sizes and hosts leave it out.
    /// </summary>
    SkippedSkill,
}

/// <summary>One line of the confirmation. <see cref="Reason"/> is why a <see cref="PlanLineKind.SkippedSkill"/> is left out.</summary>
public sealed record PlanLine(string Id, string Title, double? SizeMb, PlanLineKind Kind, string? TermsLabel, string? Reason = null);

/// <summary>
/// What the confirmation says. The totals are the sums of the sizes of the items the run would fetch (<see cref="Lines"/> of kind Download). The disk figure is the download figure: a
/// skill set or a plugin is laid out file for file (ENGINEERING DECISION: the catalogue holds one size, and the plan's 3.2 room check adds the 10%).
/// </summary>
public sealed class DownloadAllPlan
{
    private DownloadAllPlan(LibrarySection section, IReadOnlyList<PlanLine> lines, IReadOnlyList<string> hosts, DriveSpace? free)
    {
        Section = section;
        Lines = lines;
        ToDownload = lines.Where(l => l.Kind == PlanLineKind.Download).Select(l => l.Id).ToList();
        DownloadMb = lines.Where(l => l.Kind == PlanLineKind.Download).Sum(l => l.SizeMb ?? 0);
        DiskMb = DownloadMb;
        Hosts = hosts;
        Free = free;
    }

    public LibrarySection Section { get; }

    /// <summary>Every item of the page that is not being fetched right now, in the page's order.</summary>
    public IReadOnlyList<PlanLine> Lines { get; }

    public IReadOnlyList<string> ToDownload { get; }

    public double DownloadMb { get; }

    public double DiskMb { get; }

    public IReadOnlyList<string> Hosts { get; }

    public DriveSpace? Free { get; }

    public bool IsEmpty => ToDownload.Count == 0;

    /// <summary>The owner's rule (OD-0099 review of version 3): the large-download warning only above 100 MB.</summary>
    public bool Large => SizeText.IsLarge(DownloadMb);

    /// <summary>
    /// What the volume must hold (ONDEMAND-PLAN.md 3.2): the download plus the unpacked size plus 10%, in bytes. When the free space is known and smaller, Continue is off and the
    /// confirmation says what it needs (3.9, "Needs 1.2 GB free on C:.").
    /// </summary>
    public long NeedBytes => (long)Math.Ceiling((decimal)(DownloadMb + DiskMb) * 1.1m * 1_000_000m);

    public bool EnoughRoom => Free is null || Free.FreeBytes >= NeedBytes;

    public bool CanContinue => !IsEmpty && EnoughRoom;

    public string Title => LibraryWords.ConfirmTitle(Section);

    public string Sub => LibraryWords.ItemsFrom(ToDownload.Count, HostsText(Hosts));

    /// <summary>The line in the tinted box: the large-download warning, the plain size, or that nothing is left to fetch.</summary>
    public string Statement => IsEmpty ? (Lines.Any(l => l.Kind == PlanLineKind.CantFetch) ? LibraryWords.NothingToDownload : LibraryWords.EverythingHere) : Large ? LibraryWords.LargeWarning(DownloadMb, DiskMb) : LibraryWords.Plain(DownloadMb);

    public string? RoomNote => EnoughRoom || Free is null ? null : LibraryWords.NeedsRoom(SizeText.Required(NeedBytes), Free.Label);

    public string IncludedHeading => LibraryWords.Included(Lines.Count(l => l.Kind != PlanLineKind.SkippedSkill), Lines.Count(l => l.Kind == PlanLineKind.AlreadyHere),
        Lines.Count(l => l.Kind == PlanLineKind.NeedsTerms), Lines.Count(l => l.Kind == PlanLineKind.NoSize), Lines.Count(l => l.Kind == PlanLineKind.CantFetch),
        Lines.Count(l => l.Kind == PlanLineKind.SkippedSkill));

    public string DownloadFact => SizeText.Exact(DownloadMb);

    public string DiskFact => SizeText.Disk(DiskMb);

    public string? FreeFact => Free is null ? null : SizeText.Free(Free.FreeBytes);

    public string HostsFact => string.Join(", ", Hosts);

    /// <summary>"github.com", "a and b", "a, b and c", and for more than three the first three and "N more" (the design's list()).</summary>
    public static string HostsText(IReadOnlyList<string> hosts)
    {
        var shown = hosts.Count > 3 ? hosts.Take(3).Append(hosts.Count - 3 + " more").ToList() : hosts.ToList();
        return shown.Count < 2 ? string.Join("", shown) : string.Join(", ", shown.Take(shown.Count - 1)) + " and " + shown[^1];
    }

    /// <summary>
    /// Builds the plan from the page's rows. A row that is being fetched already (queued, downloading, checking) is not part of it. <paramref name="catalogue"/> gives each item's
    /// host; <paramref name="free"/> is the data root's volume, or null when it cannot be read. <paramref name="canFetch"/>, when given, says whether this build can fetch an item: one that is not here
    /// and cannot be fetched stays in the list as <see cref="PlanLineKind.CantFetch"/>, so the confirmation says it is skipped and why (ED-NEW-dl-6). ED-NEW-dl-13: under the line of a set
    /// that is not here yet come the skills its catalogue entry leaves out, each with its reason (<see cref="PlanLineKind.SkippedSkill"/>).
    /// </summary>
    public static DownloadAllPlan Build(LibrarySection section, IReadOnlyList<LibraryRow> rows, SkillsCatalogue catalogue, DriveSpace? free, Func<string, bool>? canFetch = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(catalogue);
        var lines = new List<PlanLine>();
        var hosts = new List<string>();
        foreach (var row in rows.Where(r => !r.Busy))
        {
            PlanLineKind kind;
            if (row.OnThisPc) kind = PlanLineKind.AlreadyHere;
            else if (canFetch is not null && !canFetch(row.Id)) kind = PlanLineKind.CantFetch;
            else if (row.TermsOwed) kind = PlanLineKind.NeedsTerms;
            else if (row.SizeMb is null) kind = PlanLineKind.NoSize;
            else kind = PlanLineKind.Download;
            lines.Add(new PlanLine(row.Id, row.Title, row.SizeMb, kind, row.TermsLabel));
            var item = catalogue.Items.FirstOrDefault(i => i.Id == row.Id);
            if (kind == PlanLineKind.Download && item is not null && LibraryRows.Host(item) is { } host && !hosts.Contains(host))
                hosts.Add(host);
            if (kind != PlanLineKind.AlreadyHere && item is not null)
                foreach (var skipped in item.Skipped)
                    lines.Add(new PlanLine(row.Id + "/" + skipped.Path, LibraryWords.SkippedSkillTitle(skipped.Name ?? skipped.Path[(skipped.Path.LastIndexOf('/') + 1)..]), null,
                        PlanLineKind.SkippedSkill, null, skipped.Reason));
        }
        return new DownloadAllPlan(section, lines, hosts, free);
    }
}

// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4; OD-0100 place A, words U1). What the Updates page shows, as plain data: UpdatesProjector makes it from the orchestrator's rows (UE-2),
// the page's controls only draw it. Nothing here touches a control, a clock or the network.
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Settings;

/// <summary>The page's overall state, after the picked design's phase(): the header, the button and the banner follow it.</summary>
internal enum UpdatesPhase
{
    /// <summary>Everything is up to date (or nothing is listed).</summary>
    Current,
    Checking,

    /// <summary>At least one update is known to be available.</summary>
    Available,

    /// <summary>Nothing is known to be available, but some versions cannot be known until their updater runs (no public listing).</summary>
    Unsure,
    Running,
    Failed,
    Done,
    Restarting,
}

/// <summary>What the page's one main button does when pressed.</summary>
internal enum UpdatesAction
{
    /// <summary>Busy: the button does nothing.</summary>
    None,
    Check,
    UpdateAll,
    Retry,
    Restart,
}

/// <summary>A row's status cell, after the design's st: kinds.</summary>
internal enum RowStatus
{
    None,
    Current,
    Checking,
    Available,
    ChecksWhenUpdated,
    Waiting,
    Updating,
    Downloading,
    Updated,
    RestartToFinish,
    DidNotUpdate,
}

/// <summary>What the page needs to know besides the rows: whether a check or a restart is going, whether Later was pressed, whether any run is going, how long ago the last check was, and the ids of the run being shown.</summary>
internal sealed record UpdatesFlags(bool Checking, bool Restarting, bool Later, bool Busy, string? CheckedAgo, IReadOnlySet<string>? RunIds);

internal sealed record UpdatesHeaderView(UpdatesPhase Phase, string Title, string Detail, string ButtonLabel, UpdatesAction Action, bool ButtonBusy, bool ButtonQuiet);

internal sealed record UpdatesBannerView(bool Visible, string Title, string Detail);

/// <summary>One row. <see cref="Progress"/> is null while the amount is not known (a CLI's own updater reports none); the failure line is lead, then the command to run by hand in a terminal, then tail (command and tail null when there is no command to name).</summary>
internal sealed record UpdatesRowView(
    string Id, string Title, UpdateItemKind Kind, string Sub, string Installed, string Latest, RowStatus Status, string StatusText, bool ShowProgress, double? Progress,
    string? FailureLead, string? FailureCommand, string? FailureTail, bool CanUpdate, bool CanRetry);

internal sealed record UpdatesSectionView(string Title, IReadOnlyList<UpdatesRowView> Rows);

internal sealed record UpdatesPageView(UpdatesHeaderView Header, UpdatesBannerView Banner, IReadOnlyList<UpdatesSectionView> Sections);

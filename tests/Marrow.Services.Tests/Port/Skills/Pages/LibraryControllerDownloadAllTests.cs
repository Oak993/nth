using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17, OD-0099 "Download all A"): the Download all confirmation sums the sizes and starts nothing on cancel; Continue downloads one item at a time; Pause and Resume;
// Cancel keeps what finished; Retry fetches only the failed ones. No network: the install seam is scripted.
[Trait("Feature", "SK-15")]
public sealed class LibraryControllerDownloadAllTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_confirmation_states_the_summed_size_and_cancel_starts_nothing()
    {
        using var rig = new LibraryRig();
        rig.Page.OpenDownloadAll();
        var dialog = rig.Page.Dialog;
        Assert.Equal(RunPhase.Confirm, dialog.Phase);
        Assert.Equal("Download all skills?", dialog.Title);
        Assert.Equal("3 items from github.com.", dialog.Sub);
        Assert.Equal("About 14 MB to download, about 14 MB on disk.", dialog.Statement);
        Assert.False(dialog.Large);
        Assert.Equal("13.6 MB", dialog.DownloadFact);
        Assert.Equal("about 14 MB", dialog.DiskFact);
        Assert.Equal("github.com", dialog.HostsFact);
        Assert.Equal("Nothing downloads until you press Continue.", dialog.Foot);
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], dialog.Lines.Select(l => l.Id));
        Assert.Equal("Included · 5 · 1 needs its terms first, skipped · 1 has no size yet, skipped", dialog.IncludedHeading);
        Assert.Empty(rig.Install.Calls);

        rig.Page.CancelConfirmation();
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
        Assert.Empty(rig.Install.Calls);
        Assert.All(rig.Page.Rows, r => Assert.Equal(ItemState.Available, r.State));
        Assert.Equal(["S1", "S2", "S3"], rig.Page.Plan.ToDownload);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Opening_the_dialog_downloads_nothing_even_after_a_wait()
    {
        using var rig = new LibraryRig();
        rig.Page.OpenDownloadAll();
        await Task.Delay(50);
        Assert.Empty(rig.Install.Calls);
        Assert.Equal("Download all skills · 14 MB", rig.Page.DownloadAllLabel);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Continue_downloads_the_planned_items_in_order_one_at_a_time_and_ends_done()
    {
        using var rig = new LibraryRig();
        await LibraryRig.Within(rig.Go());
        Assert.Equal(["S1", "S2", "S3"], rig.Install.Calls);
        Assert.Equal(1, rig.Install.MostAtOnce);
        var dialog = rig.Page.Dialog;
        Assert.Equal(RunPhase.Done, dialog.Phase);
        Assert.Equal("Downloaded", dialog.Title);
        Assert.Equal("All 3 are on this PC: about 14 MB.", dialog.Sub);
        Assert.Equal("New skills start as listed in each detail; switch them there.", dialog.Foot);
        Assert.Equal([ItemState.Installed, ItemState.Installed, ItemState.Installed, ItemState.Available, ItemState.Available], rig.Page.Rows.Select(r => r.State));
        Assert.All(dialog.Lines.Take(3), l => Assert.Equal(RunLineState.Downloaded, l.State));
        rig.Page.CloseRun();
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
        Assert.Equal("Download all skills", rig.Page.DownloadAllLabel);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Progress_reaches_the_row_and_the_dialog_and_a_finished_transfer_is_checked()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S1");
        var run = rig.Go();
        await LibraryRig.Within(rig.Install.Started("S1"));
        Assert.Equal(RunPhase.Downloading, rig.Page.Dialog.Phase);
        Assert.Equal("Downloading all skills", rig.Page.Dialog.Title);
        Assert.Equal("One at a time. Hide it and keep using Nth.", rig.Page.Dialog.Sub); // exact Sections TX b:sub, independently pinned by LibraryReviewTests
        rig.Install.Report("S1", 0.5);
        Assert.Equal(0.5, rig.Row("S1").Progress);
        Assert.Equal(ItemState.Downloading, rig.Row("S1").State);
        Assert.Equal(ItemState.Queued, rig.Row("S2").State);
        var dialog = rig.Page.Dialog;
        Assert.Equal(RunLineState.Downloading, dialog.Lines[0].State);
        Assert.Equal("50%", dialog.Lines[0].Right);
        Assert.Equal("Waiting · 8.1 MB", dialog.Lines[1].Right);
        Assert.Equal(0.5 * 3.5 / 13.6, dialog.Overall, 6);
        Assert.Equal("12% · 2 MB of 14 MB · 0 of 3 done", dialog.ProgressText);
        rig.Install.Report("S1", 1);
        Assert.Equal(ItemState.Checking, rig.Row("S1").State);
        Assert.Equal(RunLineState.Checking, rig.Page.Dialog.Lines[0].State);
        rig.Install.Release("S1");
        await LibraryRig.Within(run);
        Assert.Equal(RunPhase.Done, rig.Page.Dialog.Phase);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Pause_stops_the_transfer_and_starts_nothing_until_Resume_which_carries_on_with_the_same_item()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S1");
        var run = rig.Go();
        await LibraryRig.Within(rig.Install.Started("S1"));
        rig.Page.Pause();
        await LibraryRig.Until(() => rig.Page.Dialog.Phase == RunPhase.Paused && rig.Install.Cancelled.Contains("S1"));
        Assert.Equal("Paused", rig.Page.Dialog.Title);
        Assert.Equal("Nothing is downloading. Resume carries on where it stopped.", rig.Page.Dialog.Sub);
        Assert.StartsWith("Paused · ", rig.Page.Dialog.ProgressText, StringComparison.Ordinal);
        Assert.Equal(["S1"], rig.Install.Calls);
        Assert.Equal(ItemState.Queued, rig.Row("S1").State);
        await Task.Delay(50);
        Assert.Equal(["S1"], rig.Install.Calls);

        rig.Page.Resume();
        await LibraryRig.Within(rig.Install.Started("S1"));
        Assert.Equal(RunPhase.Downloading, rig.Page.Dialog.Phase);
        rig.Install.Release("S1");
        await LibraryRig.Within(run);
        Assert.Equal(["S1", "S1", "S2", "S3"], rig.Install.Calls);
        Assert.Equal(RunPhase.Done, rig.Page.Dialog.Phase);
        Assert.Equal(1, rig.Install.MostAtOnce);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Cancel_stops_the_rest_and_what_has_already_downloaded_stays()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S2");
        var run = rig.Go();
        await LibraryRig.Within(rig.Install.Started("S2"));
        rig.Page.StopRun();
        await LibraryRig.Within(run);
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
        Assert.Equal(["S1", "S2"], rig.Install.Calls);
        Assert.Equal(["S2"], rig.Install.Cancelled);
        Assert.Equal([ItemState.Installed, ItemState.Available, ItemState.Available], rig.Page.Rows.Take(3).Select(r => r.State));
        Assert.Equal("Download all skills · 10 MB", rig.Page.DownloadAllLabel);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Cancel_while_paused_ends_the_run_without_starting_anything_again()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S1");
        var run = rig.Go();
        await LibraryRig.Within(rig.Install.Started("S1"));
        rig.Page.Pause();
        await LibraryRig.Until(() => rig.Page.Dialog.Phase == RunPhase.Paused);
        rig.Page.StopRun();
        await LibraryRig.Within(run);
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
        Assert.Equal(["S1"], rig.Install.Calls);
        Assert.All(rig.Page.Rows, r => Assert.Equal(ItemState.Available, r.State));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_failed_item_does_not_stop_the_others_and_Retry_fetches_only_the_failed_one()
    {
        using var rig = new LibraryRig();
        rig.Install.FailOn("S2", "Couldn’t reach github.com.");
        await LibraryRig.Within(rig.Go());
        Assert.Equal(["S1", "S2", "S3"], rig.Install.Calls);
        var dialog = rig.Page.Dialog;
        Assert.Equal(RunPhase.Partial, dialog.Phase);
        Assert.Equal("2 of 3 downloaded", dialog.Title);
        Assert.Equal("1 didn’t download. The rest are on this PC.", dialog.Sub);
        Assert.Equal("Retry tries the failed ones again. Or download them yourself.", dialog.Foot); // exact Sections TX b:foot
        Assert.Equal(1, dialog.Failed);
        Assert.Equal(RunLineState.Failed, dialog.Lines[1].State);
        Assert.Equal("Didn’t download", dialog.Lines[1].Right);
        Assert.Equal(ItemState.Failed, rig.Row("S2").State);
        Assert.Equal("Couldn’t reach github.com.", rig.Row("S2").Failure);
        Assert.Equal(ItemState.Installed, rig.Row("S3").State);

        rig.Install.StopFailing("S2");
        await LibraryRig.Within(rig.Page.RetryFailed());
        Assert.Equal(["S1", "S2", "S3", "S2"], rig.Install.Calls);
        Assert.Equal(RunPhase.Done, rig.Page.Dialog.Phase);
        Assert.Equal(ItemState.Installed, rig.Row("S2").State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_backend_claiming_bound_still_fails_closed_if_its_install_seam_is_unbound()
    {
        using var rig = new LibraryRig(install: new FailClosedFirstInstall());
        rig.Backend.CanFetch = true; // deliberately misreported capability: retain the failure-path proof as well as the new admission tests.
        await LibraryRig.Within(rig.Go());
        Assert.Equal(RunPhase.Partial, rig.Page.Dialog.Phase);
        Assert.Equal("0 of 3 downloaded", rig.Page.Dialog.Title);
        Assert.All(rig.Page.Rows.Take(3), r => Assert.Equal(ItemState.Failed, r.State));
        Assert.All(rig.Page.Rows.Take(3), r => Assert.Equal("Downloading isn’t available in this build yet.", r.Failure));
        Assert.Empty(rig.Install.Calls);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Items_under_terms_not_accepted_and_items_with_no_size_are_never_fetched_and_accepted_terms_bring_the_item_in()
    {
        using var rig = new LibraryRig();
        await LibraryRig.Within(rig.Go());
        Assert.DoesNotContain("S4", rig.Install.Calls);
        Assert.DoesNotContain("S5", rig.Install.Calls);
        Assert.Equal(RunLineState.NeedsTerms, rig.Page.Dialog.Lines[3].State);
        Assert.Equal("Under Anthropic's terms", rig.Page.Dialog.Lines[3].Right);
        Assert.Equal(RunLineState.NoSize, rig.Page.Dialog.Lines[4].State);
        Assert.Equal("Size not known yet", rig.Page.Dialog.Lines[4].Right);
        rig.Page.CloseRun();
        rig.Page.AcceptTerms("S4");
        Assert.Equal(["S4"], rig.Page.Plan.ToDownload);
        await LibraryRig.Within(rig.Go());
        Assert.Equal(["S1", "S2", "S3", "S4"], rig.Install.Calls);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Above_one_hundred_megabytes_the_confirmation_carries_the_large_download_warning()
    {
        using var rig = new LibraryRig(catalogue: LibraryFixtures.WithSize(LibraryFixtures.Catalogue(), "S2", 1040.2));
        rig.Page.OpenDownloadAll();
        var dialog = rig.Page.Dialog;
        Assert.True(dialog.Large);
        Assert.StartsWith("This can be a large download: 1.05 GB (1,045.7 MB)", dialog.Statement, StringComparison.Ordinal);
        Assert.Equal("1.05 GB (1,045.7 MB)", dialog.DownloadFact);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_too_little_room_Continue_is_off_and_starts_nothing()
    {
        using var rig = new LibraryRig();
        rig.Backend.FreeSpace = new DriveSpace("C:", 5_000_000);
        rig.Page.OpenDownloadAll();
        var dialog = rig.Page.Dialog;
        Assert.False(dialog.CanContinue);
        Assert.Equal("Needs 30 MB free on C:.", dialog.RoomNote);
        Assert.Equal("5 MB", dialog.FreeFact);
        Assert.Equal("C:", dialog.FreeLabel);
        await LibraryRig.Within(rig.Page.Continue());
        Assert.Empty(rig.Install.Calls);
        Assert.Equal(RunPhase.Confirm, rig.Page.Dialog.Phase);
        rig.Backend.FreeSpace = new DriveSpace("C:", 212_000_000_000);
        rig.Page.CancelConfirmation();
        rig.Page.OpenDownloadAll();
        Assert.True(rig.Page.Dialog.CanContinue);
        Assert.Equal("212 GB", rig.Page.Dialog.FreeFact);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void When_everything_is_here_the_button_says_so_and_nothing_opens()
    {
        using var rig = new LibraryRig();
        foreach (var id in new[] { "S1", "S2", "S3", "S4", "S5" }) rig.Backend.Install(id, LibraryFixtures.Pin);
        Assert.Equal("All skills are here", rig.Page.DownloadAllLabel);
        Assert.False(rig.Page.CanDownloadAll);
        rig.Page.OpenDownloadAll();
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_plugins_page_has_its_own_Download_all_over_the_plugins_only()
    {
        using var rig = new LibraryRig(LibrarySection.Plugins);
        Assert.Equal("Download all plugins · 8.1 MB", rig.Page.DownloadAllLabel);
        await LibraryRig.Within(rig.Go());
        Assert.Equal(["P1"], rig.Install.Calls);
        Assert.Equal("Downloaded", rig.Page.Dialog.Title);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Pause_and_Cancel_do_nothing_when_no_run_goes_and_Continue_does_nothing_without_a_confirmation()
    {
        using var rig = new LibraryRig();
        rig.Page.Pause();
        rig.Page.Resume();
        rig.Page.StopRun();
        await LibraryRig.Within(rig.Page.Continue());
        await LibraryRig.Within(rig.Page.RetryFailed());
        Assert.Empty(rig.Install.Calls);
        Assert.Equal(RunPhase.None, rig.Page.Dialog.Phase);
    }
}

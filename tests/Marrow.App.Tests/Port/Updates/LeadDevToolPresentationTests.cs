using Marrow.App.Port.Settings;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Updates;

[Trait("Decision", "ED-308")]
public sealed class LeadDevToolPresentationTests
{
    [Fact]
    public void Git_in_use_message_is_plain_on_the_page_and_pill_and_the_row_and_pill_offer_Retry()
    {
        const string message = "Git is in use by another app. Close it and try again.";
        var row = new UpdateRow("dev:Git.Git", "Git", UpdateItemKind.DevTool, UpdateRowState.Failed, "2.54.0", "2.55.0.5", null, message, false);
        var page = UpdatesProjector.Project([row], new(false, false, false, false, null, null));
        var projected = Assert.Single(page.Sections.SelectMany(s => s.Rows));
        Assert.Equal(message, projected.FailureLead); Assert.True(projected.CanRetry);
        Assert.Equal(("Check for updates", UpdatesAction.Check), (page.Header.ButtonLabel, page.Header.Action)); // ED-NEW-providers-updates-1: the header checks again; Retry is the row's
        var pill = new UpdatePillProjector().Project([row], false);
        Assert.Equal(message, pill.BannerText); Assert.Equal(UpdatePillAction.Retry, pill.Action);
    }

    [Fact]
    public void Office_background_state_is_visible_on_both_surfaces_and_has_no_done_or_repeat_update_action()
    {
        var row = new UpdateRow("dev:office", "Office", UpdateItemKind.DevTool, UpdateRowState.UpdatingInBackground, "16.0.20430.20092", null, null, "Updating in the background", false);
        var page = UpdatesProjector.Project([row], new(false, false, false, false, null, null));
        var projected = Assert.Single(page.Sections.SelectMany(s => s.Rows));
        Assert.Equal(("In the background", RowStatus.Updating), (projected.StatusText, projected.Status)); // ED-NEW-providers-updates-1: the row's words fit its column; the header says the rest
        Assert.Equal("Updating in the background", page.Header.Title);
        Assert.Equal("—", projected.Latest); // Office has no proven public listing (OfficeC2RUpdater.LatestVersion): nothing is invented while it updates
        Assert.False(projected.CanUpdate); Assert.False(projected.CanRetry); Assert.Null(projected.FailureLead);
        Assert.Equal(("Check for updates", UpdatesAction.Check), (page.Header.ButtonLabel, page.Header.Action)); // ED-NEW-providers-updates-1 (was U1's "Check now")
        var pill = new UpdatePillProjector().Project([row], false);
        Assert.Equal("Updating in the background", pill.PillText);
        Assert.Equal("Updating in the background", Assert.Single(pill.Rows).Status);
        Assert.Equal(UpdatePillAction.CheckNow, pill.Action);
        Assert.NotEqual(UpdatePillPhase.Done, pill.Phase);
    }

    [Fact]
    public void Shared_service_rechecks_background_versions_on_the_manual_clock_and_never_relaunches()
    {
        using var runtime = new TestRuntime();
        var office = new BackgroundUpdateItem();
        var service = runtime.OnMain(() => new UpdateEverythingService([office], () => { }));
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Assert.Equal(UpdateRowState.UpdatingInBackground, Assert.Single(service.Rows).State);
        Assert.Equal("Updating in the background", service.State.PillText);
        var before = office.Reads;
        runtime.Advance(TimeSpan.FromSeconds(10));
        Assert.True(office.Reads > before); Assert.Equal(1, office.Updates);
        office.Version = "16.0.20430.20100";
        runtime.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(UpdateRowState.Updated, Assert.Single(service.Rows).State); Assert.Equal(1, office.Updates);
        before = office.Reads; runtime.Advance(TimeSpan.FromSeconds(30)); Assert.Equal(before, office.Reads);
    }

    [Fact]
    public void A_page_started_run_also_gets_background_checks_and_reconfiguration_stops_old_reads()
    {
        using var runtime = new TestRuntime(); var office = new BackgroundUpdateItem();
        var service = runtime.OnMain(() => new UpdateEverythingService([office], () => { }));
        var controller = runtime.OnMain(() => new UpdatesController(service));
        runtime.OnMain(() => controller.Attach().GetAwaiter().GetResult());
        runtime.OnMain(() => controller.Press().GetAwaiter().GetResult());
        var before = office.Reads; runtime.Advance(TimeSpan.FromSeconds(10));
        Assert.True(office.Reads > before); Assert.Equal(1, office.Updates);
        runtime.OnMain(() => service.Configure([], () => { })); before = office.Reads;
        runtime.Advance(TimeSpan.FromSeconds(30)); Assert.Equal(before, office.Reads);
        runtime.OnMain(controller.Detach);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_Office_with_a_finished_or_failed_other_item_never_claims_global_done(bool gitFailed)
    {
        var office = new UpdateRow("dev:office", "Office", UpdateItemKind.DevTool, UpdateRowState.UpdatingInBackground, "16.0.20430.20092", null, null, "Updating in the background", false);
        var other = new UpdateRow("dev:Git.Git", "Git", UpdateItemKind.DevTool, gitFailed ? UpdateRowState.Failed : UpdateRowState.Updated, "2.54.0", "2.55.0.5", null,
            gitFailed ? "Git is in use by another app. Close it and try again." : null, false);
        var page = UpdatesProjector.Project([office, other], new(false, false, false, false, null, null));
        Assert.NotEqual(UpdatesPhase.Done, page.Header.Phase); Assert.NotEqual(UpdatesPhase.Current, page.Header.Phase);
        Assert.Equal("In the background", page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == office.Id).StatusText); // ED-NEW-providers-updates-1
        if (gitFailed) Assert.True(page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == other.Id).CanRetry);
        var pill = new UpdatePillProjector().Project([office, other], false);
        Assert.NotEqual(UpdatePillPhase.Done, pill.Phase); Assert.NotEqual(UpdatePillPhase.Hidden, pill.Phase);
        Assert.Equal("Updating in the background", pill.Rows.Single(r => r.Id == office.Id).Status);
    }

    [Fact]
    public void Background_checks_defer_during_another_update_and_resume_without_relaunching_Office()
    {
        using var runtime = new TestRuntime(); var office = new BackgroundUpdateItem();
        var other = new FakeUpdateItem("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "1.0", "2.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([other, office], () => { }));
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        other.Latest = "3.0";
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); other.Gate = gate.Task;
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything)); Assert.True(service.IsBusy);
        var before = office.Reads; runtime.Advance(TimeSpan.FromSeconds(10)); Assert.Equal(before, office.Reads);
        gate.SetResult(); runtime.Loop.RunUntil(() => !service.IsBusy);
        runtime.Advance(TimeSpan.FromSeconds(10)); Assert.True(office.Reads > before); Assert.Equal(1, office.Updates);
        office.Version = "16.0.20430.20100"; runtime.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(UpdateRowState.Updated, service.Rows.Single(r => r.Id == office.Id).State);
    }
}

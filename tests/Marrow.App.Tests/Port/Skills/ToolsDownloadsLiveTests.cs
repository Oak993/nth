// PORT-ONLY: RR-14 (ED-NEW-dl-11). The owner's run of the installed 1.0 ended Settings > Updates > Tools' Download all "0 of 16 downloaded", every row "Didn't download". The Tools page over
// the production backend and a real SkillsHost, with the release catalogue's 17 runnable items (their pins, kinds, needs and installer parameters; ED-NEW-dl-13: pin run 4 added T4) and
// fixture bytes on SK-0's loopback https fake: Download all installs all 17 and the pill counts them; offline, a row says why and keeps Retry, which installs once the source answers.
// No network, no installer runs.
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
[Trait("Feature", "RR-14")]
[Trait("Spec", "DL-17")]
[Trait("Rule", "R-TEST-10")]
[Trait("Decision", "ED-NEW-dl-11")]
public sealed class ToolsDownloadsLiveTests(HeadlessSession headless)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(120);

    private void Live(Action<LibraryPageHarness, CatalogueToolRig, LibraryController, LibraryModel> check) => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var rig = new CatalogueToolRig();
        try
        {
            using var controller = new LibraryController(LibrarySection.Skills, new ToolsLibraryBackend(new HostLibraryBackend(rig.Host, rig.Catalogue)));
            check(h, rig, controller, new LibraryModel(controller, tools: true));
        }
        finally { HeadlessSession.RunDispatcherUntil(rig.DisposeAsync().AsTask(), Bound); } // pumped: a blocking wait here would hold the dispatcher the disposal resumes on
    });

    [Fact]
    public void Download_all_tools_installs_all_seventeen_runnable_items_and_the_pill_counts_them_from_1_of_17_to_Downloaded() => Live((h, rig, controller, model) =>
    {
        Assert.Equal(rig.Catalogue.Items.Select(i => i.Id).Order(StringComparer.Ordinal), controller.Rows.Select(r => r.Id).Order(StringComparer.Ordinal));
        Assert.Equal(17, controller.Rows.Count);
        Assert.Contains(controller.Rows, r => r.Id == "T4");
        Assert.True(controller.CanDownloadAll);
        controller.OpenDownloadAll();
        Assert.Equal("Download all tools?", model.Dialog.Title);
        Assert.Equal(17, model.Dialog.Lines.Count);
        Assert.DoesNotContain(model.Dialog.Lines, l => l.State is RunLineState.CantFetch or RunLineState.NoSize or RunLineState.NeedsTerms or RunLineState.AlreadyHere);

        model.DialogHidden = true;
        var pill = new SkillsProgressPill(model, _ => { });
        h.Root.Children.Add(pill);
        var hold = rig.Server.HoldRequests();
        var run = controller.Continue();
        HeadlessSession.RunDispatcherUntil(hold.WaitForArrivals(1), Bound);
        h.Settle();
        Assert.Equal("Downloading 1 of 17", pill.LabelText);

        hold.Release();
        HeadlessSession.RunDispatcherUntil(run, Bound);
        h.Settle();
        var view = controller.Dialog;
        Assert.Equal((RunPhase.Done, LibraryWords.DoneTitle, 0), (view.Phase, view.Title, view.Failed));
        Assert.Equal(17, view.Lines.Count(l => l.State == RunLineState.Downloaded));
        Assert.Equal("Downloaded", pill.LabelText);
        Assert.All(rig.Catalogue.Items, i => Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get(i.Id).State));
        Assert.All(controller.Rows, r => Assert.True(r.OnThisPc, r.Id));
    });

    [Fact]
    public void Offline_a_tool_reads_Couldnt_reach_with_Retry_enabled_and_Retry_installs_it_once_the_source_answers() => Live((h, rig, controller, model) =>
    {
        rig.Offline = true;
        HeadlessSession.RunDispatcherUntil(controller.Download("T5"), Bound);
        h.Settle();
        var row = controller.Rows.Single(r => r.Id == "T5");
        Assert.Equal((ItemState.Failed, "Couldn't reach 127.0.0.1."), (row.State, row.Failure));
        Assert.True(controller.CanFetchItem("T5")); // the row's Retry stays enabled
        Assert.Empty(rig.Server.Requests);
        Assert.Null(rig.Host.Pipeline.InstalledFolder("T5"));

        rig.Offline = false;
        HeadlessSession.RunDispatcherUntil(controller.Download("T5"), Bound);
        h.Settle();
        row = controller.Rows.Single(r => r.Id == "T5");
        Assert.Equal(ItemState.Installed, row.State);
        Assert.Null(row.Failure);
        Assert.NotNull(rig.Host.Pipeline.InstalledFolder("T5"));
    });
}

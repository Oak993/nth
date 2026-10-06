// PORT-ONLY: first-release feature unit SK-14 (DL-18, ONDEMAND 5.4/5.5: a decline is written before the provider is answered, and it is remembered per chat). No network.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class PipelineDeclinedTests
{
    [Fact]
    public async Task Declined_offers_are_written_to_state_and_survive_a_restart_without_touching_installed_items()
    {
        using var rig = new FetchRig();
        await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        var next = rig.Host.Pipeline.State.Decline("chat-1", "S3");
        rig.Host.Pipeline.SetDeclined(next);
        Assert.Equal(["S3"], rig.Host.Pipeline.State.Declined.Single().ItemIds);
        var again = rig.Reopen();
        Assert.Equal("chat-1", again.Pipeline.State.Declined.Single().ChatId);
        Assert.Equal(ItemState.Installed, again.Pipeline.State.Get("S1").State);
    }

    [Fact]
    public void Only_the_declined_list_is_taken_from_the_state_given_so_an_offer_cannot_change_an_installed_record()
    {
        using var rig = new FetchRig();
        var forged = AddonsState.Restore([new ItemRecord("S1", ItemState.Installed, new string('c', 40), null, null)], [], [new ChatDeclined("c", ["S3"])], []);
        rig.Host.Pipeline.SetDeclined(forged);
        Assert.Empty(rig.Host.Pipeline.State.Items);
        Assert.Single(rig.Host.Pipeline.State.Declined);
    }

    [Fact]
    public void The_host_gives_the_catalogue_it_was_opened_with()
    {
        using var rig = new FetchRig();
        Assert.Same(rig.Catalogue, rig.Host.Catalogue);
    }

    [Fact]
    public async Task A_decline_written_during_a_download_is_kept_when_the_download_commits()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        rig.Host.Pipeline.SetDeclined(rig.Host.Pipeline.State.Decline("chat-2", "S3"));
        rig.Hold["S1"].SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("S1").State);
        Assert.Equal("chat-2", Assert.Single(rig.Host.Pipeline.State.Declined).ChatId);
        Assert.Equal("chat-2", Assert.Single(rig.Reopen().Pipeline.State.Declined).ChatId);
    }
}

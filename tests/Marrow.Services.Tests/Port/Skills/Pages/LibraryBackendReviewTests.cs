using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pages;

public sealed class LibraryBackendReviewTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Unbound_download_and_Continue_never_queue_or_fail_an_available_item()
    {
        using var rig = new LibraryRig(install: new FailClosedFirstInstall());
        await rig.Page.Download("S1");
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
        Assert.False(rig.Page.CanDownloadAll);
        rig.Page.OpenDownloadAll();
        Assert.False(rig.Page.Dialog.CanContinue);
        await rig.Page.Continue();
        Assert.Equal(RunPhase.Confirm, rig.Page.Dialog.Phase);
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
    }
}

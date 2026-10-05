// PORT-ONLY: SK-13 / DL-15. "Non-open items require the terms step before any byte is fetched": the guard in front of the download seam.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Terms;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
public sealed class TermsGateTests
{
    private static Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>> Inner(Action ran) =>
        (_, _, _, _) => { ran(); return Task.FromException<InstallPlan>(new IOException("the fixture download ran")); };

    private static Task Run(Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>> download, CatalogueItem item) =>
        download(item, new ItemRecord(item.Id, ItemState.Checking, null, item.Pin, null), new Progress<double?>(), CancellationToken.None);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_non_open_item_without_accepted_terms_starts_no_fetch_at_all()
    {
        var rig = new TermsRig();
        var ran = 0;
        var guarded = TermsGate.Guard(() => rig.State, Inner(() => ran++));
        await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => Run(guarded, rig.Item));
        Assert.Equal(0, ran);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task After_the_terms_are_accepted_the_fetch_goes_through()
    {
        var rig = new TermsRig();
        rig.Step().Accept("A2", readControlChecked: true);
        var ran = 0;
        await Assert.ThrowsAsync<IOException>(() => Run(TermsGate.Guard(() => rig.State, Inner(() => ran++)), rig.Item));
        Assert.Equal(1, ran);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_open_item_is_never_held_back()
    {
        var rig = new TermsRig(terms: TermsClass.Open);
        var ran = 0;
        await Assert.ThrowsAsync<IOException>(() => Run(TermsGate.Guard(() => rig.State, Inner(() => ran++)), rig.Item));
        Assert.Equal(1, ran);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_changed_licence_hash_stops_the_fetch_again()
    {
        var accepted = new TermsRig();
        accepted.Step().Accept("A2", readControlChecked: true);
        var changed = new TermsRig(licence: "A later text of the Marrow-made fixture terms.\n");
        var ran = 0;
        await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => Run(TermsGate.Guard(() => accepted.State, Inner(() => ran++)), changed.Item));
        Assert.Equal(0, ran);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_licence_without_a_catalogue_hash_stops_the_fetch()
    {
        var rig = new TermsRig();
        var ran = 0;
        await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => Run(TermsGate.Guard(() => rig.State, Inner(() => ran++)), rig.Item with { LicenceFile = null, Files = [] }));
        Assert.Equal(0, ran);
    }
}

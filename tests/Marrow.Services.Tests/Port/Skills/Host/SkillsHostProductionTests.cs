// PORT-ONLY: SK-13. The production composition: the embedded catalogue, the Windows protection and lock, and every seam that reaches a network, a process or a vendor left unbound so it fails closed
// until SK-16 binds it. A host opened this way on a fresh data root installs nothing, fetches nothing and leaves the first-run folders only.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Host;

[Trait("Feature", "SK-13")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsHostProductionTests
{
    [Fact]
    public void The_production_options_bind_the_embedded_catalogue_and_leave_every_network_process_and_vendor_seam_unbound()
    {
        var options = SkillsHostOptions.Production();
        Assert.Equal(SkillsCatalogueLoader.LoadEmbedded().Items.Select(i => i.Id), options.Catalogue.Items.Select(i => i.Id));
        Assert.Null(options.Downloads);
        Assert.Null(options.Bindings);
        Assert.Null(options.LicenceText);
        Assert.NotNull(options.InstallLock);
        Assert.NotNull(options.Protection);
    }

    [Fact]
    public async Task A_production_host_on_a_fresh_root_has_nothing_installed_and_refuses_everything_that_needs_a_seam()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production() with { ClashSource = _ => [] });
            Assert.Empty(host.Pipeline.State.Items);
            Assert.Empty(host.Unrestored);
            Assert.Null(host.GuardedDownloads);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.Terms.LicenceTextAsync("A2"));
            Assert.Empty(host.Pipeline.Snapshot());
            Assert.True(Directory.Exists(host.Paths.Addons));
            Assert.False(Directory.Exists(host.Paths.Tools));
            Assert.False(Directory.Exists(host.Paths.Store));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    public void The_real_catalogue_owes_terms_for_the_non_open_sets_and_accepts_them_only_where_the_pin_run_recorded_the_licence_hash()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production() with { ClashSource = _ => [] });
            // ED-NEW-dl-11: pin run 3 recorded A2's licence file hash; O5 stays blocked (no licence file covers it), so its terms still cannot be accepted.
            var a2 = host.Terms.Offer("A2")!;
            Assert.Equal("Under Anthropic's terms", a2.Label);
            Assert.True(a2.CanAccept);
            var o5 = host.Terms.Offer("O5")!;
            Assert.Equal("Under OpenAI's terms", o5.Label);
            Assert.False(o5.CanAccept);
            Assert.Null(host.Terms.Offer("A1")); // an open set owes none
            Assert.Throws<InvalidOperationException>(() => host.Terms.Accept("O5", readControlChecked: true));
            Assert.Throws<InvalidOperationException>(() => host.Terms.Accept("A2", readControlChecked: false));
            host.Terms.Accept("A2", readControlChecked: true);
        }
        finally { SkillsHostRig.Delete(root); }
    }
}

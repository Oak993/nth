// PORT-ONLY: SK-13 / DL-15 through the composed host: the acceptance is stored in state.json with the date and the licence hash, survives a restart, and a changed licence hash asks again.
using System.Text.Json;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Host;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsHostTermsTests
{
    [Fact]
    public void An_accepted_term_is_in_state_json_with_its_date_and_licence_hash_and_survives_a_restart()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            var options = SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text, new FixedClock(TermsRig.At));
            using (var host = SkillsHost.Open(root, options))
            {
                Assert.NotNull(host.Terms.Offer("A2"));
                host.Terms.Accept("A2", readControlChecked: true);
                Assert.Null(host.Terms.Offer("A2"));
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "addons", "state.json")));
            var term = doc.RootElement.GetProperty("acceptedTerms").EnumerateArray().Single();
            Assert.Equal(ItemTerms.LicenceHash(rig.Item), term.GetProperty("licenceSha256").GetString());
            Assert.Equal("A2", term.GetProperty("itemId").GetString());
            Assert.StartsWith("2026-10-05T09:30:00", term.GetProperty("acceptedAt").GetString(), StringComparison.Ordinal);
            using var restarted = SkillsHost.Open(root, options);
            Assert.Null(restarted.Terms.Offer("A2"));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    public async Task A_changed_licence_hash_asks_again_after_a_restart_and_the_gate_in_front_of_the_download_holds()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var first = new TermsRig();
            using (var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), first.Catalogue, first.Text)))
                host.Terms.Accept("A2", readControlChecked: true);
            var changed = new TermsRig(licence: "A later text of the Marrow-made fixture terms.\n");
            var ran = 0;
            var options = SkillsHostRig.Options(new PipelineProtection(), changed.Catalogue, changed.Text) with
            {
                Downloads = (_, _, _, _) => { ran++; throw new IOException("the fixture download ran"); },
            };
            using var restarted = SkillsHost.Open(root, options);
            Assert.NotNull(restarted.Terms.Offer("A2"));
            var download = restarted.GuardedDownloads ?? throw new InvalidOperationException("the host holds no download seam");
            var record = new ItemRecord(changed.Item.Id, ItemState.Checking, null, changed.Item.Pin, null);
            await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => download(changed.Item, record, new Progress<double?>(), CancellationToken.None));
            Assert.Equal(0, ran);
            restarted.Terms.Accept("A2", readControlChecked: true);
            await Assert.ThrowsAsync<IOException>(() => download(changed.Item, record, new Progress<double?>(), CancellationToken.None));
            Assert.Equal(1, ran);
        }
        finally { SkillsHostRig.Delete(root); }
    }
}

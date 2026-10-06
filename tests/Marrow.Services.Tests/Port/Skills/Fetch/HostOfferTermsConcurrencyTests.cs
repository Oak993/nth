// PORT-ONLY: first-release feature unit SK-14 (ONDEMAND 5.4: terms step inside asked; DL-18). Concurrency: accept terms while an install holds the operation semaphore.
using System.Text.Json;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Fetch;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Xunit;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class HostOfferTermsConcurrencyTests
{
    [Fact]
    public async Task AcceptTerms_does_not_deadlock_on_operation_held_by_concurrent_install()
    {
        using var rig = new FetchRig(bound: true);
        var host = rig.Host;

        rig.Hold["S1"] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var install = host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);

        Task? accept = null;
        var acceptEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await rig.Entered("S1").WaitAsync(TimeSpan.FromSeconds(20));

            accept = Task.Run(() =>
            {
                acceptEntered.TrySetResult();
                host.OfferTerms.Accept("N1");
            });

            await acceptEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await accept.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(rig.Hold["S1"].Task.IsCompleted);
            Assert.False(install.IsCompleted);

            var persisted = AddonsStateStore.Load(host.Paths.StateFile);
            var term = persisted.AcceptedTerms.Single();
            Assert.Equal(ItemTerms.LicenceHash(rig.Item("N1")), term.LicenceSha256);
            Assert.Equal("N1", term.ItemId);
            Assert.Null(persisted.Items.FirstOrDefault(r => r.Id == "S1")?.InstalledPin);
        }
        finally
        {
            rig.Hold["S1"].TrySetResult();
            await Task.WhenAll(install, accept ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(20));
        }

        Assert.NotNull(host.Pipeline.InstalledFolder("S1"));

        var afterInstall = AddonsStateStore.Load(host.Paths.StateFile);
        Assert.Single(afterInstall.AcceptedTerms);
        Assert.Equal(ItemTerms.LicenceHash(rig.Item("N1")), afterInstall.AcceptedTerms[0].LicenceSha256);

        var reopened = rig.Reopen();
        Assert.Null(reopened.OfferTerms.Offer("N1"));
    }
}
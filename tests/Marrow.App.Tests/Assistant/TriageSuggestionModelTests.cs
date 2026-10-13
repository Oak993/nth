using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-3")]
public sealed class TriageSuggestionModelTests
{
    [Fact]
    public void Private_batches_cannot_prepare_a_cloud_call_and_admission_refusal_never_asks()
    {
        using var harness = new AppModelHarness();
        var cloud = new BrainModel("claude", "cheap", "claude:default", "Fake");
        var preparations = 0; var asks = 0;
        var adapter = new TriageSuggestionModel(_ => new BrainPick(cloud, "fake", false), _ =>
        {
            preparations++;
            return new DelegateCall(ProviderKind.Claude, (_, _) => { asks++; return Task.FromResult<string?>("fake"); }) { Admit = _ => false };
        });
        Assert.Null(harness.Main.Loop.Run(() => adapter.Reword("fake private", true, CancellationToken.None)));
        Assert.Equal(0, preparations);
        Assert.Null(harness.Main.Loop.Run(() => adapter.Reword("fake public", false, CancellationToken.None)));
        Assert.Equal(0, asks);
    }

    [Fact]
    public void A_text_call_releases_its_booking_when_the_provider_cancels()
    {
        using var harness = new AppModelHarness();
        var releases = 0;
        var local = new BrainModel("local", "cheap", "local:default", "Fake") { RunsLocally = true };
        var adapter = new TriageSuggestionModel(_ => new BrainPick(local, "fake", false), _ =>
            new DelegateCall(ProviderKind.Local, (_, _) => Task.FromCanceled<string?>(new CancellationToken(true))) { Admit = _ => true, Release = () => releases++ });
        harness.Main.Loop.Run(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.Reword("fake private", true, CancellationToken.None)));
        Assert.Equal(1, releases);
    }
}

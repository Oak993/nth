using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Tests.Port.Skills;

public sealed class W3_LiveOfferTests
{
    [Fact]
    [Trait("Decision", "ED-326")]
    [Trait("Rule", "R-TEST-9")]
    public async Task The_real_flow_factory_attaches_to_a_live_host_download_without_persisting_it_or_asking_again()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent));
        var root = Path.Combine(parent!, "w3-live-offer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var pin = new string('a', 40);
        var item = new CatalogueItem
        {
            Id = "S1", Kind = ItemKind.Skillset, Title = "Fixture skill", Summary = "Offline held download", Vendor = "Marrow tests",
            CountState = CountState.Added, PinState = PinState.Pinned, Pin = pin, SourceUrl = "https://github.com/example/fixture",
            LicenceName = "fixture", LicenceUrl = "https://raw.githubusercontent.com/example/fixture/" + pin + "/LICENSE",
            LicenceFile = "LICENSE", TermsClass = TermsClass.Open, ApproxSizeMb = null, Needs = [], Provides = [], Artifacts = [],
            SkillCount = 1, SkillsListed = true,
            Files = [new CatalogueFile { Path = "LICENSE", Size = 8, Sha256 = new string('b', 64) },
                new CatalogueFile { Path = "skill/SKILL.md", Size = 10, Sha256 = new string('c', 64) }],
            Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [] }],
        };
        var catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-06", Items = [item] };
        var options = SkillsHostOptions.Production(catalogue) with
        {
            Downloads = async (_, _, progress, cancellation) =>
            {
                Interlocked.Increment(ref calls);
                progress.Report(.25);
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellation);
                throw new InvalidOperationException("Fixture never publishes an install.");
            },
            Bindings = (_, _) => throw new InvalidOperationException("Fixture never binds an install."),
        };
        try
        {
            using var host = SkillsHost.Open(root, options);
            using var r = new SkillsOfferRig(runtime => SkillsOfferFlows.Create(runtime, host));
            using var cancel = new CancellationTokenSource();
            var initial = host.Downloads.Install(item, new Progress<double?>(), cancel.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                r.Turn.Send("fixture");
                r.Main(() => r.Flow.Observe(new ToolRequested("S1", "fixture need")));
                Assert.Equal(OfferStage.Downloading, r.Flow.Stage);
                Assert.NotNull(r.Flow.Progress);
                Assert.Equal(4L, r.Flow.Progress.BytesDone);
                Assert.Equal(18L, r.Flow.Progress.BytesTotal);
                Assert.Equal(["S1"], r.Flow.Card!.Requested);
                Assert.Equal(1, Volatile.Read(ref calls));
                Assert.Empty(host.Pipeline.State.Items);
                Assert.Empty(AddonsStateStore.Load(host.Paths.StateFile).Items);
                Assert.False(release.Task.IsCompleted);
            }
            finally
            {
                r.Main(r.Flow.Cancel);
                cancel.Cancel();
                r.Turn.H.Main.Loop.RunUntil(() => initial.IsCompleted, TimeSpan.FromSeconds(20));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial);
                r.Turn.H.Main.RunUntilIdle();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

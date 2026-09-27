using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeSweepTests
{
    [Theory]
    [InlineData("droppy", 375L)]
    [InlineData("native", 17L)]
    [InlineData("lead", 17L)]
    [InlineData("plain", 17L)]
    [Trait("Req", "PR-RUNTIME-253")] // App/Runtime/ThreadRuntime.swift:556-560
    public void Install_sums_stored_and_live_spends_only_for_Droppy_heads(string kind, long expected)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Tokens", h.NewFolder("tokens"));
        var thread = AppModelSamples.Thread(project) with
        {
            HydraEnabled = kind == "lead",
            Hydra = kind is "droppy" or "native" ? AppModelSamples.Head(HydraHeadInfo.Status.Completed) with
            { HydraHeadInfoKind = kind == "droppy" ? HydraHeadInfo.Kind.Droppy : HydraHeadInfo.Kind.Native } : null,
        };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("In-memory turn");
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.Rehearse(new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Codex, 25)));
            runtime.HydraTokens = 17;
            var liveId = Assert.Single(runtime.Turns).Id;
            var stored = new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([
                    new TurnRecord { Index = 1, Status = TurnStatus.Completed, TokenSpends = new ValueList<TokenSpend>([
                        new TokenSpend(ProviderKind.Codex, 100, inputTokens: 900, outputTokens: 800, cachedInputTokens: 700),
                        new TokenSpend(ProviderKind.Claude, 250)]) },
                    new TurnRecord { Index = 2, Status = TurnStatus.Completed },
                ]),
            };
            typeof(ThreadRuntime).GetMethod("Install", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, [stored]);
            Assert.Equal(expected, runtime.HydraTokens);
            Assert.Equal(new[] { stored.Turns[0].Id, stored.Turns[1].Id, liveId }, runtime.Turns.Select(turn => turn.Id));
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-253")] // App/Runtime/ThreadRuntime.swift:556-560
    public void Droppy_history_without_any_spends_replaces_the_old_count_with_zero()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("No spends", h.NewFolder("zero"));
        var thread = AppModelSamples.Thread(project) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(thread.Id);
            runtime.HydraTokens = 99;
            typeof(ThreadRuntime).GetMethod("Install", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, [new ThreadDocument(thread.Id)]);
            Assert.Equal(0, runtime.HydraTokens);
            runtime.CancelPersistence();
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-251")] // App/Runtime/ThreadRuntime.swift:2172-2176,2437-2443
    [Trait("Req", "PR-RUNTIME-160")] // App/Runtime/ThreadRuntime.swift:3696-3706
    public void Token_event_marks_dirty_without_scheduling_a_save_and_survives_the_snapshot_and_disk()
    {
        using var h = new AppModelHarness();
        var fixture = ThreadRuntimePart8Fixtures.Create(h);
        h.Main.OnMain(() =>
        {
            var runtime = fixture.Runtime;
            runtime.RehearseTurn("Store tokens");
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            runtime.SaveNow();
            DiskWriter.Shared.WaitForQueuedWrites();
            var saveTask = typeof(ThreadRuntime).GetField("saveTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Null(saveTask.GetValue(runtime));
            var spend = new TokenSpend(ProviderKind.Codex, 100, inputTokens: 60, outputTokens: 40);
            runtime.Rehearse(new ProviderEvent.TokenSpend(spend));
            Assert.Null(saveTask.GetValue(runtime));
            var dirty = runtime.UnsavedSnapshot();
            Assert.NotNull(dirty);
            Assert.Equal(spend, Assert.Single(Assert.Single(dirty.Turns).TokenSpends!));
            var snapshot = runtime.SnapshotForPersistence();
            Assert.Equal(spend, Assert.Single(Assert.Single(snapshot.Turns).TokenSpends!));
            runtime.SaveNow();
            DiskWriter.Shared.WaitForQueuedWrites();
            var disk = Storage.DecodeDocument(fixture.Thread.Id);
            Assert.NotNull(disk);
            Assert.Equal(spend, Assert.Single(Assert.Single(disk.Turns).TokenSpends!));
            runtime.CancelPersistence();
        });
    }

    // STOP App.Runtime.ThreadRuntime #1
    // Kind: outside unit scope. Swift: App/Views/Chat/TimelineRows.swift:2385-2431.
    // Records: UI-RUNTIME-001, UI-RUNTIME-003. These own notice ROW geometry, type and colors in TimelineRows.cs,
    // which this prompt explicitly excludes. Runtime CLI notice messages already have dedicated passing tests.
    // Needed: the TimelineRows chat's rendering implementation and verification; no row change made here.

    // STOP App.Runtime.ThreadRuntime #2
    // Kind: missing dependency. Swift: App/Runtime/ThreadRuntime.swift:3696-3706; Core/Models/Timeline.swift:306,329.
    // Records: PR-RUNTIME-160, PR-RUNTIME-251. The snapshot carries spends and the event marks dirty without
    // scheduling, but TurnRecordConverter still explicitly omits tokenSpends on encode/decode. TokenSpend itself
    // now has Codable; wiring it into Timeline.cs belongs to Core.Models.Timeline, outside this prompt's units.
    // Needed: that unit's converter implementation. Keep the disk regression assertion above intact and red.
}

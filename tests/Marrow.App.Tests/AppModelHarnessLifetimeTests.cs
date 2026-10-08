using System.Diagnostics;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

public sealed class AppModelHarnessLifetimeTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-NEW-windows-45")]
    public async Task Teardown_stops_palette_lifetimes_of_directly_constructed_models_before_retiring_the_actor()
    {
        using var fake = new Marrow.Services.Tests.Fixtures.PalettePipelineFixture();
        using var transport = Marrow.Core.Port.URLSession.Transport.Override(fake.Http);
        var harness = new AppModelHarness();
        var model = harness.Main.OnMain(() => new Marrow.App.AppModel());
        var facade = harness.Main.OnMain(() => model.PaletteShades);
        var engine = model.PaletteEngine;
        await engine.Observe(["synthetic/lifetime-only"]);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Assert.NotNull(typeof(Marrow.App.Port.ModelsPalette.ModelShades).GetField("timer", flags)!.GetValue(facade));
        harness.Dispose();
        // StopPaletteChecks owns the whole engine/facade pair and clears them. No new
        // getter is called after teardown: that would create a new lifetime in dead roots.
        Assert.Null(typeof(Marrow.App.AppModel).GetField("paletteShades", flags)!.GetValue(model));
        Assert.Null(typeof(Marrow.App.AppModel).GetField("paletteEngine", flags)!.GetValue(model));
        Assert.Empty(fake.Http.Received);
    }
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public async Task Teardown_finishes_a_real_queued_write_before_retiring_its_manual_clock_and_scratch_directory()
    {
        var harness = new AppModelHarness();
        var clock = harness.Main.Time;
        var destination = Path.Combine(harness.Scratch, "owned-pending-write.json");
        File.WriteAllText(destination, "before");
        FileStream? sharingGuard = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task? drained = null;
        var disposed = false;
        try
        {
            Assert.Equal(0, clock.ActiveTimers);
            DiskWriter.Shared.EncodeAndWrite(new Dictionary<string, string> { ["value"] = "persisted before teardown" }, destination);
            // A real Windows sharing conflict forces production FileOps into its first retry timer.
            Assert.True(SpinWait.SpinUntil(() => clock.ActiveTimers > 0, TimeSpan.FromSeconds(5)),
                "The actual queued write never reached its manual-clock retry.");
            Assert.Null(DiskWriter.Shared.LastWrittenSize(destination));
            sharingGuard.Dispose(); sharingGuard = null;
            harness.Dispose(); disposed = true;
            // LastWrittenSize is set only after the asynchronous production write succeeds. Merely
            // deleting the folder and letting that write fail later cannot satisfy this assertion.
            Assert.True(DiskWriter.Shared.LastWrittenSize(destination) is > 0,
                "The harness retired its clock/directory before the actual queued write completed.");
            Assert.False(Directory.Exists(harness.Scratch));
            drained = Task.Factory.StartNew(DiskWriter.Shared.WaitForQueuedWrites, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await drained.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(drained.IsCompletedSuccessfully, "The shared writer barrier is still blocked after teardown.");
            using var next = new AppModelHarness();
            next.WriteLibrary([], []);
            Assert.IsType<LibraryLoadResult.Loaded>(Storage.LoadLibrary());
        }
        finally
        {
            sharingGuard?.Dispose();
            // A deliberately red old implementation must not poison later tests. Keep the original
            // clock object and drive its parked retry even after the old harness has disposed its scopes.
            drained ??= Task.Factory.StartNew(DiskWriter.Shared.WaitForQueuedWrites, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var cleanup = Stopwatch.StartNew();
            while (!drained.IsCompleted && cleanup.Elapsed < TimeSpan.FromSeconds(10))
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                await Task.Delay(2);
            }
            if (!drained.IsCompleted) throw new TimeoutException("The regression could not clean up its owned queued write.");
            await drained;
            if (!disposed) harness.Dispose();
        }
    }
}

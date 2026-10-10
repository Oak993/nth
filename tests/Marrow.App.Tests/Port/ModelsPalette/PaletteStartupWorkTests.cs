using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Port.ModelsPalette;

public sealed class PaletteStartupWorkTests
{
    [Fact]
    [Trait("Decision", "ED-622")]
    public async Task A_failed_validation_is_observed_at_publication_and_is_never_retried()
    {
        var failure = new InvalidDataException("invalid immutable pack");
        var calls = 0;
        var work = new PaletteStartupWork(() => { Interlocked.Increment(ref calls); throw failure; });
        work.Begin();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(work.FinishAsync));
        Assert.Same(failure, Assert.Throws<InvalidDataException>(work.Finish));
        work.Begin();
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Decision", "ED-622")]
    public async Task Pack_validation_begins_without_waiting_and_finishes_once_before_publication()
    {
        using var fixture = new PalettePipelineFixture();
        var pack = PaletteReleasePack.Bake(fixture.Pack, 1, []);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var work = new PaletteStartupWork(() =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
            return pack;
        });
        var begin = Task.Run(work.Begin);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(begin, await Task.WhenAny(begin, Task.Delay(TimeSpan.FromMilliseconds(250))));
            await begin;
            work.Begin();
            var finish = Task.Run(work.Finish);
            Assert.NotSame(finish, await Task.WhenAny(finish, Task.Delay(TimeSpan.FromMilliseconds(50))));
            release.Set();
            Assert.Same(pack, await finish);
            Assert.Same(pack, work.Finish());
            Assert.Equal(1, calls);
        }
        finally { release.Set(); await begin; }
    }
}

using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-9")]
public sealed class C3bUiaPatternsTests
{
    [Theory]
    [InlineData(PcElementAction.Invoke, false)]
    [InlineData(PcElementAction.Toggle, false)]
    [InlineData(PcElementAction.Select, false)]
    [InlineData(PcElementAction.Expand, false)]
    [InlineData(PcElementAction.Collapse, false)]
    [InlineData(PcElementAction.ScrollIntoView, false)]
    [InlineData(PcElementAction.Minimize, false)]
    [InlineData(PcElementAction.Maximize, false)]
    [InlineData(PcElementAction.Restore, false)]
    [InlineData(PcElementAction.Close, false)]
    [InlineData(PcElementAction.SetValue, false)]
    [InlineData(PcElementAction.SetValue, true)]
    [InlineData(PcElementAction.Scroll, false)]
    public async Task Stop_during_pattern_resolution_prevents_mutation(PcElementAction action, bool range)
    {
        using var stop = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = new PcFakeUiaPatterns { BeforeResolve = () => { entered.Set(); release.Wait(); } };
        var work = Task.Run(() => Perform(provider, action, range, stop.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await stop.CancelAsync();
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(provider.Mutations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Stop_during_range_property_reads_prevents_set_value(int cancelledRead)
    {
        using var stop = new CancellationTokenSource();
        var reads = 0;
        var provider = new PcFakeUiaPatterns { BeforeRangeRead = () => { if (++reads == cancelledRead) stop.Cancel(); } };
        Assert.ThrowsAny<OperationCanceledException>(() => Perform(provider, PcElementAction.SetValue, true, stop.Token));
        Assert.Empty(provider.Mutations);
    }

    [Fact]
    public void Stop_after_one_scroll_prevents_all_remaining_scroll_calls()
    {
        using var stop = new CancellationTokenSource();
        var provider = new PcFakeUiaPatterns { AfterMutation = stop.Cancel };
        Assert.ThrowsAny<OperationCanceledException>(() => Perform(provider, PcElementAction.Scroll, false, stop.Token));
        Assert.Equal("scroll", Assert.Single(provider.Mutations));
    }

    [Fact]
    public void An_already_cancelled_focus_never_calls_the_provider()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var provider = new PcFakeUiaPatterns();
        Assert.ThrowsAny<OperationCanceledException>(() => Perform(provider, PcElementAction.Focus, false, stop.Token));
        Assert.Empty(provider.Mutations);
    }

    [Theory]
    [InlineData(PcElementAction.Focus, false)]
    [InlineData(PcElementAction.Invoke, false)]
    [InlineData(PcElementAction.Toggle, false)]
    [InlineData(PcElementAction.Select, false)]
    [InlineData(PcElementAction.Expand, false)]
    [InlineData(PcElementAction.Collapse, false)]
    [InlineData(PcElementAction.ScrollIntoView, false)]
    [InlineData(PcElementAction.Minimize, false)]
    [InlineData(PcElementAction.Maximize, false)]
    [InlineData(PcElementAction.Restore, false)]
    [InlineData(PcElementAction.Close, false)]
    [InlineData(PcElementAction.SetValue, false)]
    [InlineData(PcElementAction.SetValue, true)]
    [InlineData(PcElementAction.Scroll, false)]
    public void Uncancelled_actions_still_reach_the_provider(PcElementAction action, bool range)
    {
        var provider = new PcFakeUiaPatterns();
        Assert.True(Perform(provider, action, range, CancellationToken.None).Done);
        Assert.Equal(action == PcElementAction.Scroll ? 3 : 1, provider.Mutations.Count);
    }

    [Theory]
    [InlineData(UiaPatterns.ElementNotEnabled, PcErrorCode.Disabled)]
    [InlineData(UiaPatterns.ElementNotAvailable, PcErrorCode.StaleElement)]
    [InlineData(UiaPatterns.Timeout, PcErrorCode.Timeout)]
    public void Pattern_resolution_errors_keep_their_existing_mapping(int result, PcErrorCode expected)
    {
        var provider = new PcFakeUiaPatterns { ResolveResult = result };
        Assert.Equal(expected, Perform(provider, PcElementAction.Invoke, false, CancellationToken.None).Error);
        Assert.Empty(provider.Mutations);
    }

    [Fact]
    public void A_missing_pattern_remains_not_supported()
    {
        var provider = new PcFakeUiaPatterns { MissingPattern = true };
        Assert.Equal(PcErrorCode.NotSupported, Perform(provider, PcElementAction.Invoke, false, CancellationToken.None).Error);
        Assert.Empty(provider.Mutations);
    }

    private static PcUiaOutcome Perform(PcFakeUiaPatterns provider, PcElementAction action, bool range, CancellationToken token) =>
        UiaPatterns.Perform(provider.Resolve, provider.Focus,
            new PcUiNode("1", "edit", "Target") { Actions = range ? PcActions.RangeValue : PcActions.Value },
            new PcElementCommand(action) { Value = "5", Count = 3, Direction = PcScrollDirection.Down, Unit = PcScrollUnit.Page }, token);
}

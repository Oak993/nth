using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

public sealed class SwiftAnimationCompletionTests
{
    [Theory]
    [InlineData(0.01234564, 123456L)] // rounded down to the clock's tick units
    [InlineData(0.01234566, 123457L)] // rounded up to the clock's tick units
    [Trait("Decision", "ED-117")] // (2): elapsed ticks + 1 ms >= rounded span ticks; ChatView.swift:275-288,1096-1101.
    public void Insert_and_remove_complete_at_the_due_tick_but_not_one_tick_before(double seconds, long spanTicks)
    {
        using var main = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var animation = Animation.Linear(seconds);
        var content = new Border().Transition(SwiftTransition.Opacity);
        var host = new Panel();
        Assert.Equal(spanTicks, SwiftTask.Seconds(seconds).Ticks);
        var justBeforeDue = TimeSpan.FromTicks(spanTicks - TimeSpan.TicksPerMillisecond - 1);

        main.OnMain(() => Transaction.WithAnimation(animation, () => SwiftTransition.Insert(host, 0, content)));
        main.Advance(justBeforeDue);
        main.OnMain(() => frames.Advance(TimeSpan.Zero));
        Assert.True(content.Opacity < 1);
        Assert.Equal(1, frames.Pending);
        main.Advance(TimeSpan.FromTicks(1));
        main.OnMain(() => frames.Advance(TimeSpan.Zero));
        Assert.Equal(1, content.Opacity);
        Assert.Equal(0, frames.Pending);

        main.OnMain(() => Transaction.WithAnimation(animation, () => SwiftTransition.Remove(host, content)));
        main.Advance(justBeforeDue);
        main.OnMain(() => frames.Advance(TimeSpan.Zero));
        Assert.Contains(content, host.Children);
        Assert.True(content.Opacity > 0);
        main.Advance(TimeSpan.FromTicks(1));
        main.OnMain(() => frames.Advance(TimeSpan.Zero));
        Assert.Equal(0, content.Opacity);
        Assert.Empty(host.Children);
        Assert.Equal(0, frames.Pending);
    }
}

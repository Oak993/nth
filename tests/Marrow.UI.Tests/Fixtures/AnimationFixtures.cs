using System.Collections.Immutable;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// The two clocks an animation reads (ED-098 (1), ED-117 (1)): frames from a ManualFrameSource are the only wake-ups,
/// and elapsed time is read on SwiftClock, here a ManualTimeProvider. Both are overridden for the test's flow.
/// </summary>
public sealed class AnimationClocks : IDisposable
{
    private readonly IDisposable source;
    private readonly IDisposable clock;

    public AnimationClocks()
    {
        source = FrameClock.Source.Override(Frames);
        clock = SwiftClock.Provider.Override(Time);
    }

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    /// <summary>Moves SwiftClock by <paramref name="seconds"/>, then delivers one frame the same span later.</summary>
    public void Frame(double seconds)
    {
        var span = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        Time.Advance(span);
        Frames.Advance(span);
    }

    public void Dispose()
    {
        clock.Dispose();
        source.Dispose();
    }
}

/// <summary>
/// UI/Chrome/Chrome.swift:976-993 SettleGlideModifier, trimmed to its animatable data: `progress` is its
/// `animatableData` (:970-973), one Double. Body and Update are logged so a test sees how often the wrapper is built
/// and which progress each frame applies.
/// </summary>
public sealed record SettleGlideSample(double Progress, List<Control> Bodies, List<double> Updates) : IAnimatableModifier
{
    public ImmutableArray<double> AnimatableData => [Progress];

    public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) => this with { Progress = data[0] };

    public Control Body(Control content)
    {
        var body = new Decorator { Child = content };
        Bodies.Add(body);
        return body;
    }

    public void Update(Control body)
    {
        Assert.Contains(body, Bodies);
        Updates.Add(Progress);
    }
}

/// <summary>
/// App/Views/Composer/ComposerView.swift:949-959 ChipRowCollapse, trimmed: `let collapsed: Bool` is not Animatable, so
/// its animatable data is empty (SwiftUI's EmptyAnimatableData, ED-098).
/// </summary>
public sealed record ChipRowCollapseSample(bool Collapsed, List<Control> Bodies, List<bool> Updates) : IAnimatableModifier
{
    public ImmutableArray<double> AnimatableData => [];

    public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) => this;

    public Control Body(Control content)
    {
        var body = new Decorator { Child = content };
        Bodies.Add(body);
        return body;
    }

    public void Update(Control body)
    {
        Assert.Contains(body, Bodies);
        Updates.Add(Collapsed);
    }
}

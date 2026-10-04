using Avalonia.Controls;
using Marrow.App.Views.Composer;

namespace Marrow.App.Port.FirstRun;

// GR-1 skeleton (test-first: the behaviour follows once the tests are seen to fail).

public sealed record FirstRunStep(string Id, string Name, int Order);

public static class FirstRunSteps
{
    public static FirstRunStep Graphics { get; } = new("gpu", "Graphics card", 10);
    public static FirstRunStep Knob { get; } = new("knob", "Effort slider knob", 20);
    public static FirstRunStep Lights { get; } = new("lights", "Composer light", 30);
    public static FirstRunStep Liquid { get; } = new("liquid", "Slider liquid", 40);
    public static FirstRunStep Strike { get; } = new("strike", "Fast mode lightning", 50);
    public static FirstRunStep Voice { get; } = new("hw", "Voice hardware", 60);
    public static IReadOnlyList<FirstRunStep> W1 { get; } = [Graphics, Knob, Lights, Liquid, Strike, Voice];
}

public sealed record FirstRunStepInfo(FirstRunStep Step, int Parts);

public enum FirstRunStepState { Waiting, Running, Done }

public sealed record FirstRunProgress(FirstRunStep Step, int Index, int Count, FirstRunStepState State, int PartsDone, int Parts, TimeSpan Elapsed);

public enum FirstRunOutcomeKind { NotShown, Skipped, Prepared, Failed, Cancelled }

public sealed record FirstRunOutcome(FirstRunOutcomeKind Kind, string? Error = null);

public interface IFirstRunScreen
{
    void Begin(IReadOnlyList<FirstRunStepInfo> steps);
    void Report(FirstRunProgress progress);
    Task EndAsync(FirstRunOutcome outcome);
}

public sealed class FirstRunPreparation
{
    internal sealed record Seams(Func<bool> IsPrepared, Func<IReadOnlyList<GpuProgram>> Registered, Func<CancellationToken, Task> PrepareGraphics,
        Func<TopLevel, IProgress<GpuWarmupStep>, CancellationToken, Task> Warm);

    public FirstRunPreparation() { }
    internal FirstRunPreparation(Seams seams, TimeProvider clock) { }
    public static FirstRunPreparation Default { get; } = new();
    public bool IsPrepared => throw new NotImplementedException();
    public IReadOnlyList<FirstRunStepInfo> Plan() => throw new NotImplementedException();
    public Task<FirstRunOutcome> RunAsync(TopLevel window, IFirstRunScreen? screen, CancellationToken cancellation = default) => throw new NotImplementedException();
}

public static class FirstRunStartup
{
    public static IFirstRunScreen? Screen { get; set; }
    public static Task<FirstRunOutcome> RunAsync(TopLevel window, CancellationToken cancellation = default) => throw new NotImplementedException();
    internal static Task<FirstRunOutcome> RunAsync(TopLevel window, FirstRunPreparation preparation, IFirstRunScreen? screen, CancellationToken cancellation = default) => throw new NotImplementedException();
}

using Marrow.Services.Port.Updates;

namespace Marrow.Services.Tests.Fixtures;

public sealed class TrialHReplay
{
    private readonly Queue<(DevToolCommand Command, DevToolCommandResult Result)> script = new();
    public List<DevToolCommand> Calls { get; } = [];
    public void Add(string recorded, DevToolCommand? expected = null) => script.Enqueue((expected ?? TrialHFixtures.Command(recorded), TrialHFixtures.Result(recorded)));
    public void Add(DevToolCommand command, DevToolCommandResult result) => script.Enqueue((command, result));
    public Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Calls.Add(command);
        Assert.NotEmpty(script); // Unexpected calls fail closed; there is no real-process fallback.
        var next = script.Dequeue();
        Assert.Equal(next.Command.Executable, command.Executable);
        Assert.Equal(next.Command.Arguments, command.Arguments);
        return Task.FromResult(next.Result);
    }
    public void Complete() => Assert.Empty(script);
}

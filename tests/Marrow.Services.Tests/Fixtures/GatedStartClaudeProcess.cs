using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Port.Skills.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// The offline Claude transport with a launch the test holds: StartAsync finishes when the test says so (ED-NEW-perf-ui-thread-2, the child is created off the session's turn),
/// and Start, the synchronous launch, is counted so a test can see it is never taken. As the real launcher does, a Terminate before the child exists ends nothing.
/// </summary>
internal sealed class GatedStartClaudeProcess : IClaudeProcess
{
    private readonly RecordingClaudeProcess inner;
    private readonly TaskCompletionSource launched = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GatedStartClaudeProcess(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment) => inner = new RecordingClaudeProcess(arguments, environment);

    public int StartCalls { get; private set; }
    public int StartAsyncCalls { get; private set; }
    public int TerminatesBeforeTheChildExisted { get; private set; }
    public int TerminatesOfTheRunningChild { get; private set; }

    /// <summary>Lets the held launch finish: the child now exists.</summary>
    public void Launch() => launched.SetResult();

    public AsyncStream<JSONValue> Messages => inner.Messages;
    public bool IsRunning => launched.Task.IsCompleted;
    public string ErrorTail => "";
    public void Start() => StartCalls++;
    public async Task StartAsync()
    {
        StartAsyncCalls++;
        await launched.Task;
        inner.Start();
    }
    public void Send(JSONValue message) => inner.Send(message);
    public void Terminate()
    {
        if (!IsRunning) { TerminatesBeforeTheChildExisted++; return; }
        TerminatesOfTheRunningChild++;
        inner.Terminate();
    }
    public Task<int> WaitForExit() => inner.WaitForExit();
}

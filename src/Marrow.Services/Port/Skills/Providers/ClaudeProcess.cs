// PORT-ONLY: SK-11a transport delegation, with no protocol or framing changes.
using Marrow.Core.Port;
using Marrow.Core.Support;
namespace Marrow.Services.Port.Skills.Providers;

internal sealed class ClaudeProcess(string executable, IReadOnlyList<string> arguments, string directory,
    IReadOnlyDictionary<string, string> environment) : IClaudeProcess
{
    private readonly StdioProcess process = new(executable, arguments, directory, environment);
    public AsyncStream<JSONValue> Messages => process.Messages;
    public bool IsRunning => process.IsRunning;
    public string ErrorTail => process.ErrorTail;
    public void Start() => process.Start();
    public Task StartAsync() => process.StartAsync();
    public void Send(JSONValue message) => process.Send(message);
    public void Terminate() => process.Terminate();
    public Task<int> WaitForExit() => process.WaitForExit();
    public void JoinJob(JobObject job) => process.Job = job;
}

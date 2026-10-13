// PORT-ONLY: SK-11a offline transport seam; production delegates unchanged to StdioProcess.
using Marrow.Core.Port;
using Marrow.Core.Support;
namespace Marrow.Services.Port.Skills.Providers;

internal interface IClaudeProcess
{
    AsyncStream<JSONValue> Messages { get; }
    bool IsRunning { get; }
    string ErrorTail { get; }
    void Start();
    /// <summary>ED-NEW-perf-ui-thread-2: <see cref="Start"/> without holding the caller while the child is created. A transport that has nothing to wait for starts at once.</summary>
    Task StartAsync() { Start(); return Task.CompletedTask; }
    void Send(JSONValue message);
    void Terminate();
    Task<int> WaitForExit();
    /// <summary>PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-5): runs the process in a mission agent's job; before Start.</summary>
    void JoinJob(JobObject job) { }
}

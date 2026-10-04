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
    void Send(JSONValue message);
    void Terminate();
}

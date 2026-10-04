// PORT-ONLY: synthetic transcripts; no live CLI or user data. Copilot fields: CopilotSession.cs HandleNotification,
// ToolRequested, HandlePermission, HandleToolResult (copilot-sdk formats cited by research-loading 3.3).
// ACP fields: ACPSession.cs HandleNotification, HandleRequest, MakeToolUpdate; session/update and
// session/request_permission from the ACP format already implemented by the pinned port.
using Marrow.Core.Support;
namespace Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
public static class ProviderTranscripts
{
    public static IEnumerable<JSONValue> Read(string name)
    {
        var root = Environment.GetEnvironmentVariable("SK11C_SOURCE_ROOT");
        if (root is null)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
            root = directory?.FullName ?? throw new InvalidOperationException("The committed fixture root is required.");
        }
        return File.ReadLines(Path.Combine(root, "tests", "Marrow.Services.Tests", "Port", "Skills", "Providers", "Fixtures", name + ".jsonl"))
            .Select(line => JSONValue.Parse(line)!);
    }
}

using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

// The actual API turn loop runs; only model answers and a controlled disk edit are offline stand-ins.
public sealed class AccountStepSession(SessionConfiguration configuration) : OpenAICompatibleSession(new()
{
    Kind = ProviderKind.Deepseek, Title = "Offline", AgentIdentity = _ => "Offline", DefaultModel = "offline",
    ChatURL = new("https://offline.invalid/chat"), ContextWindow = 64000, IsVisionModel = _ => false,
}, configuration)
{
    public TaskCompletionSource EditRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Editing { get; private set; }
    public int Rounds { get; private set; }
    public List<string> Executed { get; } = [];
    protected override Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true)
    {
        if (++Rounds == 1)
        {
            OnEvent?.Invoke(new ProviderEvent.MessageCompleted("source-prefix", "Original prefix. "));
            return Task.FromResult(new StreamRound { Content = "Original prefix. ", ToolCalls = new([new("edit", "write_file", "{}"), new("read", "read_file", "{}")]) });
        }
        OnEvent?.Invoke(new ProviderEvent.MessageCompleted("source-prefix", "Original prefix. Original continuation."));
        return Task.FromResult(new StreamRound { Content = "Original continuation." });
    }
    protected override async Task<string> ExecuteTool(PendingToolCall tool)
    {
        Executed.Add(tool.Id);
        OnEvent?.Invoke(new ProviderEvent.ToolStarted(tool.Id, new() { ToolCallKind = tool.Id == "edit" ? ToolCall.Kind.Edit : ToolCall.Kind.Read, Title = tool.Name }));
        if (tool.Id == "edit")
        {
            Editing = true;
            await EditRelease.Task;
            File.WriteAllText(Path.Combine(Configuration.WorkingDirectory.Path, "finished-step.txt"), "Completed once.");
            Editing = false;
        }
        OnEvent?.Invoke(new ProviderEvent.ToolUpdated(tool.Id, new() { Output = "Completed once.", Status = ToolCall.Status.Completed }));
        return "Completed once.";
    }
}

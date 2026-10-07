using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Local;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Local;

// PORT-ONLY: OD-0114 / ED-410. Reuses the real SSE/tool/approval loop. Never accepts or transmits an API key.
public sealed class LocalSession : OpenAICompatibleSession
{
    private readonly LocalTarget target;
    private LocalModel current;

    public LocalSession(SessionConfiguration configuration, LocalTarget target)
        : base(ConfigFor(target), configuration with { ApiKey = null, Model = target.Model.Id })
    {
        this.target = target;
        current = target.Model;
    }

    private static Config ConfigFor(LocalTarget target) => new()
    {
        Kind = ProviderKind.Local, Title = "Local", AgentIdentity = model => $"Local coding agent ({model})",
        DefaultModel = target.Model.Id, ChatURL = new Uri(target.Server.BaseURL, "chat/completions"),
        ContextWindow = target.Model.Capabilities.ContextWindow, RequiresAPIKey = false, FollowRedirects = false, UseCookies = false,
        ReplaysReasoning = false,
        IsVisionModel = id => target.Models.FirstOrDefault(model => model.Id == id)?.Capabilities.Vision == true,
    };

    protected override Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true)
    {
        current = target.Models.FirstOrDefault(item => item.Id == model || item.SelectionId == model)
            ?? throw new ProviderError(new ProviderError.Case.Failed("That local model is not on this server. Refresh the model list and select it again."));
        return base.StreamOneRound(current.Id, effort, withTools && current.Capabilities.ToolCalling == true);
    }

    protected override string ModelForInput(string model) => target.Models.FirstOrDefault(item => item.Id == model || item.SelectionId == model)?.Id
        ?? throw new ProviderError(new ProviderError.Case.Failed("That local model is not on this server. Refresh the model list and select it again."));

    protected override int ContextWindowFor(string model) => target.Models.FirstOrDefault(item => item.Id == model)?.Capabilities.ContextWindow ?? 4096;

    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        payload["max_tokens"] = Math.Max(1, Math.Min(2048, ContextWindowFor(model) / 4));
        if (current.Capabilities.ToolCalling != true) { payload.Remove("tools"); payload.Remove("tool_choice"); }
    }

    protected override Task<string> ExecuteTool(PendingToolCall tool) => current.Capabilities.ToolCalling == true
        ? base.ExecuteTool(tool)
        : Task.FromResult("Tool calling is not enabled for this local model.");
}

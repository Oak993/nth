// PORT-ONLY: OD-0114; ED-403. Display aliases preserve one interrupted reply; original events remain journalled.
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

internal sealed class HandshakeReplyBridge(Guid turnId, string assistantId, string prefix, string idPrefix)
{
    private readonly Dictionary<string, string> segments = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    public Guid TurnId { get; } = turnId;

    public ProviderEvent Map(ProviderEvent value, bool continueReply = true)
    {
        switch (value)
        {
            case ProviderEvent.MessageDelta delta:
                if (!continueReply) return delta with { Id = Id(delta.Id) };
                Segment(delta.Id, delta.Text, append: true);
                return new ProviderEvent.MessageDelta(assistantId, delta.Text);
            case ProviderEvent.MessageCompleted final:
                if (!continueReply) return final with { Id = Id(final.Id) };
                if (final.Text.Length > 0) Segment(final.Id, final.Text, append: false);
                return new ProviderEvent.MessageCompleted(assistantId, prefix + string.Concat(order.Select(id => segments[id])));
            case ProviderEvent.ToolStarted tool: return tool with { Id = Id(tool.Id) };
            case ProviderEvent.ToolOutput tool: return tool with { Id = Id(tool.Id) };
            case ProviderEvent.ToolUpdated tool: return tool with { Id = Id(tool.Id) };
            case ProviderEvent.ReasoningDelta reasoning: return reasoning with { Id = Id(reasoning.Id) };
            case ProviderEvent.ReasoningCompleted reasoning: return reasoning with { Id = Id(reasoning.Id) };
            case ProviderEvent.PlanDelta plan: return plan with { Id = Id(plan.Id) };
            case ProviderEvent.PlanCompleted plan: return plan with { Id = Id(plan.Id) };
            case ProviderEvent.Approval approval when approval.Value.ToolItemID is { } itemId:
                return approval with { Value = approval.Value with { ToolItemID = Id(itemId) } };
            default: return value;
        }
    }

    private string Id(string id) => idPrefix + id;
    private void Segment(string id, string text, bool append)
    {
        if (!segments.ContainsKey(id)) order.Add(id);
        segments[id] = append ? segments.GetValueOrDefault(id, "") + text : text;
    }
}

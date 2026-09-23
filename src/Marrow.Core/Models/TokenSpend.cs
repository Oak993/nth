namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/TokenSpend.swift (B2 1fbf81e), :1-66.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Codable conformance (:56-65 init(from:) and the synthesized encoder) is left to the implementing unit.

// :15-66
public sealed record TokenSpend
{
    public ProviderKind Provider { get; init; }     // :16
    public long TotalTokens { get; init; }          // :17
    public long? InputTokens { get; init; }         // :18
    public long? OutputTokens { get; init; }        // :19
    public long? CachedInputTokens { get; init; }   // :20
    public string? Model { get; init; }             // :21
    public string? AgentID { get; init; }           // :22

    // :24-40
    public TokenSpend(ProviderKind provider, long totalTokens, long? inputTokens = null, long? outputTokens = null,
        long? cachedInputTokens = null, string? model = null, string? agentID = null)
    {
        Provider = provider;
        TotalTokens = totalTokens;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CachedInputTokens = cachedInputTokens;
        Model = model;
        AgentID = agentID;
    }

    // :42-47 mutating func merge(_:): returns the merged copy (R-VAL-1).
    public TokenSpend Merge(TokenSpend other) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/TokenSpend.swift:42");
}

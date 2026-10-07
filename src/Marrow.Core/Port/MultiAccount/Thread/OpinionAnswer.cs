// PORT-ONLY: multi-account steps 3b-5, job 0. One account's answer to a one-message "Ask others": the text so far, and whether it is complete.
namespace Marrow.Core.Port.MultiAccount.Thread;

public sealed record OpinionAnswer(Guid AccountId, string Text, bool Done)
{
    /// <summary>A failed or cancelled head is complete but cannot be combined; preserve the reason for its card.</summary>
    public string? Error { get; init; }
}

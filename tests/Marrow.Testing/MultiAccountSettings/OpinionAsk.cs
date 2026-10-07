namespace Marrow.Testing.MultiAccountSettings;

/// <summary>One "Ask others" a test made: the thread, the message and the accounts it was handed to.</summary>
public sealed record OpinionAsk(Guid Thread, string Message, IReadOnlyList<Guid> Accounts);

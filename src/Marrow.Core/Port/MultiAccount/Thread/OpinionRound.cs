// PORT-ONLY: multi-account steps 3b-5, job 0. One "Ask others" for one message: who was asked, what they said so far, and whether it was combined.
namespace Marrow.Core.Port.MultiAccount.Thread;

public sealed record OpinionRound(Guid Id, Guid Thread, string Message, IReadOnlyList<OpinionAnswer> Answers, bool Combined);

// PORT-ONLY: multi-account steps 3b-5, job 0. One account's answer to a one-message "Ask others": the text so far, and whether it is complete.
namespace Marrow.Core.Port.MultiAccount.Thread;

public sealed record OpinionAnswer(Guid AccountId, string Text, bool Done);

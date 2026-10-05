namespace Marrow.Core.Accounts;

// The host evaluates this at its atomic commit, after every asynchronous final-context operation.
public interface IAccountSwitchCommitGuard
{
    Func<bool>? CanCommit { get; set; }
}

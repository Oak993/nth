// PORT-ONLY: multi-account steps 3b-5, job 0 (OD-0115 opinions=B). "Ask others for this message": the message goes to the picked accounts for this message only, their answers stream back as cards, and Combine writes one answer from them.
// The engine is a later job (E1); the composer and the answer cards bind this.
namespace Marrow.Core.Port.MultiAccount.Thread;

public interface IOpinionsPort
{
    /// <summary>Raised with the thread whose rounds changed (an answer grew or finished, a round was combined).</summary>
    event Action<Guid>? Changed;

    /// <summary>Asks the accounts about the message, for this message only. Returns the round's id. At least one account is needed.</summary>
    Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default);

    /// <summary>The thread's rounds, oldest first.</summary>
    IReadOnlyList<OpinionRound> Rounds(Guid thread);

    /// <summary>Writes one combined answer from the round's answers. A round is combined once.</summary>
    Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default);
}

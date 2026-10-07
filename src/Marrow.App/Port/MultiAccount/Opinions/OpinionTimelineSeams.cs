// PORT-ONLY: MA-3b-7 / wiring W. IOpinionsPort intentionally exposes no timeline-entry IDs or head errors in job 0.
// W records AskOthersView.Sent's round -> sent entry, PostCombined's round -> appended entry, and projects E1 head errors.
// Do not guess association from prompt text: two identical messages can have different opinion rounds.
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed record OpinionTimelineContext(Func<Guid, string?> PromptEntry, Func<Guid, string?> CombinedEntry, Func<Guid, Guid, string?> HeadError,
    Func<Guid, string?>? PostedByline = null, Func<Guid, IReadOnlyList<ThreadAccount>?>? AccountsAtAsk = null);
internal static class OpinionTimelineSeams
{
    internal static Ambient<Func<Guid, OpinionTimelineContext?>> Offer { get; } = new(_ => null);
}

// PORT-ONLY: card presentation values, MA-3b-10.
using Marrow.Core.Accounts;
namespace Marrow.Core.Port.MultiAccount.Cards;
public sealed record CardOptionGroup(DecisionKind Kind, string Label, IReadOnlyList<CardButton> Options);

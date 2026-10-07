// PORT-ONLY: card presentation values, MA-3b-10.
namespace Marrow.Core.Port.MultiAccount.Cards;
public sealed record CardButton(string Value, string Text, bool Primary, bool Selected = false);

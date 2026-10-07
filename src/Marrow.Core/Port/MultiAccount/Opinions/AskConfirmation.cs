// PORT-ONLY: OD-0120 pending message snapshot; checkbox starts unchecked on every confirmation.
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.Core.Port.MultiAccount.Opinions;
public sealed record AskConfirmation(string Message,IReadOnlyList<Guid> Accounts,IReadOnlyList<ThreadAccount> OtherServices,bool DontAskAgain=false)
{
    public string Text=>string.Join("\n",OtherServices.Select(a=>a.Name+" is another service: it will see this conversation."));
}

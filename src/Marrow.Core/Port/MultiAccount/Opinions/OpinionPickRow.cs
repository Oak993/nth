// PORT-ONLY: opinions B list :4805-4806, account and model, selected check and room.
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.Core.Port.MultiAccount.Opinions;
public sealed record OpinionPickRow(ThreadAccount Account,string Model,bool CanAsk,bool Selected)
{
    public string Text=>Account.Name+" / "+Model;
}

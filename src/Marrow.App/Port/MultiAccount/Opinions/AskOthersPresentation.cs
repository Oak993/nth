// PORT-ONLY: MA-3b-6 glass presenters share the model's pending-confirmation state.
using Marrow.Core.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersPresentation : MainActorModel
{
    private bool list, confirm;
    internal bool List { get => Read(list); set => Set(ref list, value); }
    internal bool Confirm { get => Read(confirm); set => Set(ref confirm, value); }
}

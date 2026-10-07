// PORT-ONLY: MA-3b-6 optional composer model; unbound until W. Tests supply job-0 fakes.
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Opinions;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal static class AskOthersComposerSeams
{
    internal static Ambient<Func<Guid, AskOthersModel?>> Offer { get; } = new(_ => null);
}

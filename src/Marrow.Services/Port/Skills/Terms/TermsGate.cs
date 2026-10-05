// PORT-ONLY: SK-13 / DL-15. "Non-open items require the terms step before any byte is fetched."
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Port.Skills.Terms;

public static class TermsGate
{
    /// <summary>
    /// The download seam with the terms checked first: when <see cref="ItemTerms.Required"/> holds (not open, and the licence hash not accepted, or not known), the inner seam is
    /// never called and <see cref="PipelineTermsRequiredException"/> is thrown, so no byte of the item is requested.
    /// </summary>
    public static Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>> Guard(
        Func<AddonsState> state, Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>> inner)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inner);
        return (item, record, progress, cancellation) =>
        {
            if (ItemTerms.Required(item, state())) throw new PipelineTermsRequiredException(item.Id);
            return inner(item, record, progress, cancellation);
        };
    }
}

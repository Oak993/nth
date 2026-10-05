// PORT-ONLY: SK-13 / DL-15. The unbound licence-text seam: no network is reached by default.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Terms;

public sealed class FailClosedLicenceText : ILicenceText
{
    public Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation) => throw new InvalidOperationException("Licence text seam is not bound.");
}

// PORT-ONLY: SK-13 / DL-15. The seam that reads an item's licence file so the terms step can show it before any of the item is fetched.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Terms;

/// <summary>
/// Reads the bytes of the item's pinned licence file (<c>LicenceFile</c> at <c>LicenceUrl</c>) and nothing else of the item. The terms step checks them against the catalogue's size
/// and hash, so this seam is not a trust root. Unbound it fails closed (<see cref="FailClosedLicenceText"/>); tests script it and never reach a network.
/// </summary>
public interface ILicenceText
{
    Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation);
}

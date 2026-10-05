// PORT-ONLY: first-release feature unit SK-15 (DL-17). The seam a first download goes through; SK-16 binds it (ED-326 (6)(a): the pipeline has no first-install path yet).
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Pages;

/// <summary>
/// Fetches, verifies and installs one catalogue item for the first time, reporting its progress (0 to 1; 1 means the bytes are in and the item is being checked). It returns when the item is
/// installed and throws when it is not: the message of the exception is what the row shows as the reason it did not download (3.9). A cancelled token stops the transfer and keeps what was
/// fetched for a later resume (3.2). Unbound it fails closed (<see cref="FailClosedFirstInstall"/>); tests script it and never reach a network.
/// </summary>
public interface IFirstInstall
{
    Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation);
}

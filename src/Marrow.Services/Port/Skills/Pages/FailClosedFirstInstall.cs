// PORT-ONLY: first-release feature unit SK-15 (DL-17). The unbound first-install seam: nothing downloads and the item fails with the reason.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Services.Port.Skills.Pages;

public sealed class FirstInstallUnavailableException() : InvalidOperationException(LibraryWords.DownloadsUnavailable);

/// <summary>Until SK-16 wires the pipeline's first install (ED-326 (6)(a)) every item fails closed: no connection is made and no file is written.</summary>
public sealed class FailClosedFirstInstall : IFirstInstall
{
    public bool IsAvailable => false;

    public Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => Task.FromException(new FirstInstallUnavailableException());
}

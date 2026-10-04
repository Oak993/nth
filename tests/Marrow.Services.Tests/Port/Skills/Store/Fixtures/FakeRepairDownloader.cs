// PORT-ONLY: records the requested catalogue pin and writes only Marrow-made files; no socket or CLI.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class FakeRepairDownloader : IStoreRepairDownloader
{
    public CatalogueItem? Requested { get; private set; }
    public int Calls { get; private set; }
    public bool Corrupt { get; set; }
    public TaskCompletionSource? Started { get; set; }
    public TaskCompletionSource? Continue { get; set; }
    public async Task DownloadAsync(CatalogueItem pinnedItem, string downloadsFolder, CancellationToken cancellationToken)
    {
        Calls++;
        Requested = pinnedItem;
        Started?.SetResult();
        if (Continue is not null) await Continue.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        StoreFixture.WriteContent(downloadsFolder);
        if (Corrupt) File.WriteAllBytes(Path.Combine(downloadsFolder, "sample", "native", "fixture.exe"), [9, 9, 9, 9]);
    }
}

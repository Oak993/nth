// PORT-ONLY: integ-sk8-sk9 (ED-NEW-integ-sk4-sk5 (5)(a)). The published folder's and fresh spares' GC exclusion, handed to the owner.

namespace Marrow.Services.Port.Skills.Install;

internal sealed class InstallReservation(IReadOnlyList<IDisposable> holds) : IDisposable
{
    private int disposed;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        foreach (var hold in holds) hold.Dispose();
    }
}

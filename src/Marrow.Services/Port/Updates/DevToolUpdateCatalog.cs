// PORT-ONLY: UE-3, BUILD-PLAN 2.4; returns installed tools only, for UE-2's item model.
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Port.Updates;

public static class DevToolUpdateCatalog
{
    public static Task<IReadOnlyList<IUpdateItem>> Discover(Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, Func<CancellationToken, Task<OfficeInstallation?>>? office = null, CancellationToken cancellation = default) => throw new NotImplementedException();
}

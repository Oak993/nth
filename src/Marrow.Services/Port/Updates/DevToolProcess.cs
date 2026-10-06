// PORT-ONLY: UE-3; fail-closed seam. Only application composition supplies the Windows process runner.
namespace Marrow.Services.Port.Updates;

public static class DevToolProcess
{
    public static Task<DevToolCommandResult> Refuse(DevToolCommand command, CancellationToken cancellation) =>
        throw new InvalidOperationException("The developer-tool process seam is not configured.");
}
